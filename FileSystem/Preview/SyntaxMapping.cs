using System;
using System.Collections.Generic;
using System.IO;
using SyntaxColorizer;
using SyntaxColorizer.Themes;

namespace DirOpusReImagined.FileSystem.Preview;

/// <summary>
/// Maps files to a <see cref="SyntaxLanguage"/> and the app's theme to a <see cref="SyntaxTheme"/>,
/// so the viewer can colour a text preview.
///
/// <para>SyntaxColorizer knows how to highlight a language but not how to recognise one, so the
/// extension table lives here. Unlike preview <em>content</em> detection — which sniffs magic bytes
/// because binary formats have reliable signatures — source code has no header to read, so the
/// name is genuinely the best signal available.</para>
/// </summary>
public static class SyntaxMapping
{
    /// <summary>
    /// Files above this size are shown unhighlighted. Tokenising runs on every preview, and the
    /// preview re-runs on every cursor move; a cap keeps arrowing through a folder of large files
    /// responsive. Well above any hand-written source file.
    /// </summary>
    public const int MaxHighlightBytes = 100 * 1024;

    // Extension (lower-case, no dot) -> language.
    private static readonly Dictionary<string, SyntaxLanguage> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = SyntaxLanguage.CSharp,
        ["csx"] = SyntaxLanguage.CSharp,
        ["vb"] = SyntaxLanguage.VisualBasic,
        ["fs"] = SyntaxLanguage.FSharp,
        ["fsx"] = SyntaxLanguage.FSharp,
        ["java"] = SyntaxLanguage.Java,
        ["kt"] = SyntaxLanguage.Kotlin,
        ["kts"] = SyntaxLanguage.Kotlin,
        ["scala"] = SyntaxLanguage.Scala,
        ["swift"] = SyntaxLanguage.Swift,
        ["m"] = SyntaxLanguage.ObjectiveC,
        ["mm"] = SyntaxLanguage.ObjectiveC,
        ["c"] = SyntaxLanguage.C,
        ["h"] = SyntaxLanguage.C,
        ["cpp"] = SyntaxLanguage.Cpp,
        ["cxx"] = SyntaxLanguage.Cpp,
        ["cc"] = SyntaxLanguage.Cpp,
        ["hpp"] = SyntaxLanguage.Cpp,
        ["hxx"] = SyntaxLanguage.Cpp,
        ["rs"] = SyntaxLanguage.Rust,
        ["go"] = SyntaxLanguage.Go,
        ["dart"] = SyntaxLanguage.Dart,
        ["ex"] = SyntaxLanguage.Elixir,
        ["exs"] = SyntaxLanguage.Elixir,
        ["hs"] = SyntaxLanguage.Haskell,
        ["r"] = SyntaxLanguage.R,
        ["groovy"] = SyntaxLanguage.Groovy,
        ["lua"] = SyntaxLanguage.Lua,
        ["rb"] = SyntaxLanguage.Ruby,
        ["php"] = SyntaxLanguage.Php,
        ["py"] = SyntaxLanguage.Python,
        ["pyw"] = SyntaxLanguage.Python,
        ["js"] = SyntaxLanguage.JavaScript,
        ["mjs"] = SyntaxLanguage.JavaScript,
        ["cjs"] = SyntaxLanguage.JavaScript,
        ["jsx"] = SyntaxLanguage.JavaScript,
        ["ts"] = SyntaxLanguage.TypeScript,
        ["tsx"] = SyntaxLanguage.TypeScript,
        ["html"] = SyntaxLanguage.Html,
        ["htm"] = SyntaxLanguage.Html,
        ["xhtml"] = SyntaxLanguage.Html,
        ["css"] = SyntaxLanguage.Css,
        ["scss"] = SyntaxLanguage.Scss,
        ["sass"] = SyntaxLanguage.Scss,
        ["less"] = SyntaxLanguage.Scss,
        ["json"] = SyntaxLanguage.Json,
        ["jsonc"] = SyntaxLanguage.Json,
        ["yaml"] = SyntaxLanguage.Yaml,
        ["yml"] = SyntaxLanguage.Yaml,
        ["toml"] = SyntaxLanguage.Toml,
        ["xml"] = SyntaxLanguage.Xml,
        ["xaml"] = SyntaxLanguage.Xml,
        ["axaml"] = SyntaxLanguage.Xml,
        ["csproj"] = SyntaxLanguage.Xml,
        ["props"] = SyntaxLanguage.Xml,
        ["targets"] = SyntaxLanguage.Xml,
        ["svg"] = SyntaxLanguage.Xml,
        ["md"] = SyntaxLanguage.Markdown,
        ["markdown"] = SyntaxLanguage.Markdown,
        ["graphql"] = SyntaxLanguage.GraphQL,
        ["gql"] = SyntaxLanguage.GraphQL,
        ["sql"] = SyntaxLanguage.MsSql,
        ["sh"] = SyntaxLanguage.Bash,
        ["bash"] = SyntaxLanguage.Bash,
        ["zsh"] = SyntaxLanguage.Bash,
        ["ps1"] = SyntaxLanguage.PowerShell,
        ["psm1"] = SyntaxLanguage.PowerShell,
        ["psd1"] = SyntaxLanguage.PowerShell,
    };

    // Files that carry their language in the name rather than an extension.
    private static readonly Dictionary<string, SyntaxLanguage> ByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile"] = SyntaxLanguage.Dockerfile,
        ["containerfile"] = SyntaxLanguage.Dockerfile,
        ["makefile"] = SyntaxLanguage.Bash,
        ["cmakelists.txt"] = SyntaxLanguage.Bash,
        [".bashrc"] = SyntaxLanguage.Bash,
        [".bash_profile"] = SyntaxLanguage.Bash,
        [".zshrc"] = SyntaxLanguage.Bash,
        [".gitconfig"] = SyntaxLanguage.Toml,
    };

    /// <summary>
    /// The language to highlight <paramref name="fileName"/> as, or
    /// <see cref="SyntaxLanguage.None"/> when it is not a recognised source file — in which case the
    /// caller should show it as plain text rather than guessing.
    /// </summary>
    public static SyntaxLanguage ForFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return SyntaxLanguage.None;

        string name = Path.GetFileName(fileName);
        if (ByFileName.TryGetValue(name, out var byName)) return byName;

        // "Dockerfile.dev" and friends: the base name still identifies the language.
        string stem = Path.GetFileNameWithoutExtension(name);
        if (ByFileName.TryGetValue(stem, out var byStem)) return byStem;

        string ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext)) return SyntaxLanguage.None;

        return ByExtension.TryGetValue(ext.TrimStart('.'), out var lang) ? lang : SyntaxLanguage.None;
    }

    /// <summary>
    /// The syntax palette matching the app's current theme. Four of DORI's named themes exist
    /// verbatim in <see cref="BuiltInThemes"/>, so those pair exactly; plain Light and Dark fall
    /// back to the Visual Studio palettes, which are the closest neutral equivalents.
    /// </summary>
    public static SyntaxTheme ForTheme(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Dracula        => BuiltInThemes.Dracula,
        ThemeChoice.Nord           => BuiltInThemes.Nord,
        ThemeChoice.SolarizedLight => BuiltInThemes.SolarizedLight,
        ThemeChoice.SolarizedDark  => BuiltInThemes.SolarizedDark,
        ThemeChoice.Dark           => BuiltInThemes.VisualStudioDark,
        ThemeChoice.Light          => BuiltInThemes.VisualStudioLight,

        // System follows the OS, which ThemeManager has already resolved onto the application's
        // actual variant; ask that rather than guessing.
        _ => Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? BuiltInThemes.VisualStudioDark
            : BuiltInThemes.VisualStudioLight,
    };
}
