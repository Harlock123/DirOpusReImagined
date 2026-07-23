using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DirOpusReImagined;

/// <summary>How the working name's letter case is transformed before tokens are applied.</summary>
public enum CaseTransform
{
    None,
    Upper,
    Lower,
    Title,
}

/// <summary>
/// Everything the rename dialog collects, in one immutable value so the transform can be a pure,
/// testable function. The first three fields are the original prefix/basename/suffix pattern; the
/// rest are the Phase-1 additions (find/replace, case, numbering).
/// </summary>
public sealed record RenameOptions(
    string Prefix,
    string Basename,
    string Suffix,
    string Find,
    string Replace,
    bool UseRegex,
    bool IgnoreCase,
    CaseTransform Case,
    int NumberStart,
    int NumberStep,
    int NumberWidth,
    bool KeepExtension)
{
    /// <summary>The current dialog's defaults: keep the name, keep the extension, 1-based numbering.</summary>
    public static RenameOptions Default { get; } = new(
        Prefix: "", Basename: "%NAME%", Suffix: "",
        Find: "", Replace: "", UseRegex: false, IgnoreCase: false,
        Case: CaseTransform.None,
        NumberStart: 1, NumberStep: 1, NumberWidth: 4,
        KeepExtension: true);
}

/// <summary>
/// Pure name-transform for the batch rename tool. No file I/O, no UI — given an original name and
/// the options, it returns the proposed new name. Kept separate from the dialog so the logic can be
/// unit-tested headlessly and reused by the live preview and the apply pass alike.
/// </summary>
public static class RenameEngine
{
    /// <summary>The two literal tokens accepted in the prefix/basename/suffix fields.</summary>
    public const string TokenOrdinal = "%ORD%";
    public const string TokenName = "%NAME%";

    /// <summary>
    /// Computes the proposed new file/folder name (no path component).
    /// </summary>
    /// <param name="originalName">The current on-disk name, e.g. <c>IMG_0042.jpg</c>.</param>
    /// <param name="isDirectory">Folders have no extension; the whole name is treated as the stem.</param>
    /// <param name="ordinal">1-based position of this item in the selection (drives <c>%ORD%</c>).</param>
    /// <param name="o">The rename options.</param>
    /// <param name="error">
    /// Non-null when the pattern itself is unusable (currently only an invalid regex). In that case
    /// the original name is returned unchanged so the caller can flag the row without crashing.
    /// </param>
    public static string ComputeNewName(
        string originalName, bool isDirectory, int ordinal, RenameOptions o, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(originalName)) return originalName;

        // 1. Split into stem + extension, matching the app's existing helpers exactly (Path.*),
        //    so dotfiles (.gitignore) and multi-dot names (archive.tar.gz) behave as they do today.
        string stem, ext;
        if (isDirectory)
        {
            stem = originalName;
            ext = "";
        }
        else
        {
            stem = Path.GetFileNameWithoutExtension(originalName);
            ext = Path.GetExtension(originalName); // includes the leading dot, or "" if none
        }

        // 2. Find / replace on the stem (the "edit the existing name" step).
        string workingStem = ApplyFindReplace(stem, o, out error);
        if (error != null) return originalName;

        // 3. Case transform on the working stem.
        workingStem = ApplyCase(workingStem, o.Case);

        // 4. Resolve tokens inside each pattern field.
        string ordText = FormatOrdinal(ordinal, o);
        string prefix = ResolveTokens(o.Prefix, ordText, workingStem);
        string basename = ResolveTokens(o.Basename, ordText, workingStem);
        string suffix = ResolveTokens(o.Suffix, ordText, workingStem);

        string result = prefix + basename + suffix;

        // 5. Ensure the extension is preserved unless the user opted out (then the pattern owns the
        //    whole name, extension included). Case-insensitive check so "FILE.TXT" + ".txt" isn't doubled.
        if (o.KeepExtension && !isDirectory && !string.IsNullOrEmpty(ext) &&
            !result.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            result += ext;
        }

        return result;
    }

    private static string ApplyFindReplace(string stem, RenameOptions o, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(o.Find)) return stem;

        if (o.UseRegex)
        {
            try
            {
                var opts = o.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                return Regex.Replace(stem, o.Find, o.Replace ?? "", opts);
            }
            catch (ArgumentException ex)
            {
                error = "Invalid regular expression: " + ex.Message;
                return stem;
            }
        }

        var cmp = o.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return stem.Replace(o.Find, o.Replace ?? "", cmp);
    }

    private static string ApplyCase(string s, CaseTransform c) => c switch
    {
        CaseTransform.Upper => s.ToUpper(CultureInfo.CurrentCulture),
        CaseTransform.Lower => s.ToLower(CultureInfo.CurrentCulture),
        // Lowercase first so existing ALL-CAPS words get proper title-casing, not left untouched.
        CaseTransform.Title => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower(CultureInfo.CurrentCulture)),
        _ => s,
    };

    private static string FormatOrdinal(int ordinal, RenameOptions o)
    {
        long value = (long)o.NumberStart + (long)(ordinal - 1) * o.NumberStep;
        string digits = value.ToString(CultureInfo.InvariantCulture);

        if (o.NumberWidth <= 0) return digits;

        // Pad the digits, keeping a leading minus sign outside the padding.
        if (digits.StartsWith('-'))
            return "-" + digits.Substring(1).PadLeft(o.NumberWidth, '0');
        return digits.PadLeft(o.NumberWidth, '0');
    }

    private static string ResolveTokens(string field, string ordText, string workingStem)
    {
        if (string.IsNullOrEmpty(field)) return field ?? "";
        return field
            .Replace(TokenOrdinal, ordText)
            .Replace(TokenName, workingStem);
    }
}
