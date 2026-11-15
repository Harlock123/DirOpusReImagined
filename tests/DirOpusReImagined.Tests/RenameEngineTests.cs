using System.Globalization;

namespace DirOpusReImagined.Tests;

public class RenameEngineTests
{
    private static RenameOptions D => RenameOptions.Default;

    private static string Rename(string name, RenameOptions o, int ordinal = 1, bool isDir = false)
    {
        var result = RenameEngine.ComputeNewName(name, isDir, ordinal, o, out var err);
        Assert.Null(err);
        return result;
    }

    [Fact]
    public void Default_options_leave_name_unchanged()
    {
        Assert.Equal("IMG_0042.jpg", Rename("IMG_0042.jpg", D));
        Assert.Equal("folder.name", Rename("folder.name", D, isDir: true));
    }

    [Fact]
    public void Prefix_and_suffix_wrap_the_stem_and_keep_the_extension()
    {
        var o = D with { Prefix = "pre_", Suffix = "_suf" };
        Assert.Equal("pre_photo_suf.jpg", Rename("photo.jpg", o));
    }

    [Fact]
    public void Ordinal_token_uses_start_step_and_width()
    {
        var o = D with { Basename = "Holiday_%ORD%", NumberStart = 10, NumberStep = 5, NumberWidth = 3 };
        Assert.Equal("Holiday_010.jpg", Rename("a.jpg", o, ordinal: 1));
        Assert.Equal("Holiday_015.jpg", Rename("b.jpg", o, ordinal: 2));
        Assert.Equal("Holiday_020.jpg", Rename("c.jpg", o, ordinal: 3));
    }

    [Fact]
    public void Ordinal_width_zero_means_no_padding()
    {
        var o = D with { Basename = "%ORD%", NumberWidth = 0 };
        Assert.Equal("7.txt", Rename("x.txt", o, ordinal: 7));
    }

    [Fact]
    public void Negative_ordinals_keep_the_minus_sign_outside_the_padding()
    {
        var o = D with { Basename = "%ORD%", NumberStart = -3, NumberWidth = 3 };
        Assert.Equal("-003.txt", Rename("x.txt", o));
    }

    [Fact]
    public void Plain_find_replace_is_case_sensitive_by_default()
    {
        var o = D with { Find = "img", Replace = "pic" };
        Assert.Equal("IMG_pic.jpg", Rename("IMG_img.jpg", o));
    }

    [Fact]
    public void Plain_find_replace_can_ignore_case()
    {
        var o = D with { Find = "img", Replace = "pic", IgnoreCase = true };
        Assert.Equal("pic_pic.jpg", Rename("IMG_img.jpg", o));
    }

    [Fact]
    public void Find_replace_applies_to_the_stem_only_not_the_extension()
    {
        var o = D with { Find = "txt", Replace = "doc" };
        Assert.Equal("mydoc.txt", Rename("mytxt.txt", o));
    }

    [Fact]
    public void Regex_find_replace_supports_groups()
    {
        var o = D with { Find = @"(\d+)_(\w+)", Replace = "$2-$1", UseRegex = true };
        Assert.Equal("abc-123.png", Rename("123_abc.png", o));
    }

    [Fact]
    public void Invalid_regex_reports_an_error_and_returns_the_original_name()
    {
        var o = D with { Find = "([", UseRegex = true };
        var result = RenameEngine.ComputeNewName("file.txt", false, 1, o, out var err);
        Assert.Equal("file.txt", result);
        Assert.NotNull(err);
    }

    [Fact]
    public void Case_transforms_apply_to_the_stem()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("HELLO WORLD.txt", Rename("Hello World.txt", D with { Case = CaseTransform.Upper }));
            Assert.Equal("hello world.txt", Rename("Hello World.txt", D with { Case = CaseTransform.Lower }));
            Assert.Equal("Hello World.txt", Rename("hELLO wORLD.txt", D with { Case = CaseTransform.Title }));
            Assert.Equal("Abc Def.txt", Rename("ABC DEF.txt", D with { Case = CaseTransform.Title }));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Extension_is_not_doubled_when_the_pattern_already_ends_with_it()
    {
        var o = D with { Suffix = ".TXT" };
        Assert.Equal("name.TXT", Rename("name.txt", o));
    }

    [Fact]
    public void Without_keep_extension_the_pattern_owns_the_whole_name()
    {
        var o = D with { Basename = "%NAME%", Suffix = ".bak", KeepExtension = false };
        Assert.Equal("report.bak", Rename("report.txt", o));
    }

    [Fact]
    public void Directories_treat_the_whole_name_as_the_stem()
    {
        var o = D with { Suffix = "_old" };
        Assert.Equal("my.folder_old", Rename("my.folder", o, isDir: true));
    }

    [Fact]
    public void Multi_dot_names_split_on_the_last_dot()
    {
        var o = D with { Prefix = "x_" };
        Assert.Equal("x_archive.tar.gz", Rename("archive.tar.gz", o));
    }

    [Fact]
    public void Empty_name_is_returned_as_is()
    {
        Assert.Equal("", RenameEngine.ComputeNewName("", false, 1, D, out var err));
        Assert.Null(err);
    }

    // ---------------- BuildPlan ----------------

    private static IReadOnlyList<RenamePlanRow> Plan(RenameOptions o, IEnumerable<string>? others = null,
        bool ci = false, params string[] names)
        => RenameEngine.BuildPlan(names.Select(n => (n, false)).ToList(), o, others ?? Array.Empty<string>(), ci);

    [Fact]
    public void Plan_marks_unchanged_and_changed_rows()
    {
        var rows = Plan(D with { Find = "a", Replace = "b" }, null, false, "a.txt", "c.txt");
        Assert.Equal(RenameStatus.Change, rows[0].Status);
        Assert.Equal("b.txt", rows[0].NewName);
        Assert.True(rows[0].WillRename);
        Assert.Equal(RenameStatus.Unchanged, rows[1].Status);
        Assert.False(rows[1].WillRename);
    }

    [Fact]
    public void Plan_numbers_items_in_selection_order()
    {
        var rows = Plan(D with { Basename = "img_%ORD%", NumberWidth = 2 }, null, false, "z.jpg", "a.jpg", "m.jpg");
        Assert.Equal(new[] { "img_01.jpg", "img_02.jpg", "img_03.jpg" }, rows.Select(r => r.NewName));
        Assert.All(rows, r => Assert.Equal(RenameStatus.Change, r.Status));
    }

    [Fact]
    public void Plan_flags_both_items_that_collide_on_one_target()
    {
        var rows = Plan(D with { Basename = "same" }, null, false, "a.txt", "b.txt", "c.dat");
        Assert.Equal(RenameStatus.DuplicateTarget, rows[0].Status);
        Assert.Equal(RenameStatus.DuplicateTarget, rows[1].Status);
        Assert.Equal(RenameStatus.Change, rows[2].Status); // same.dat is distinct
        Assert.NotNull(rows[0].Message);
    }

    [Fact]
    public void Plan_flags_a_target_that_exists_outside_the_selection()
    {
        var rows = Plan(D with { Prefix = "new_" }, new[] { "new_a.txt" }, false, "a.txt", "b.txt");
        Assert.Equal(RenameStatus.ExistsOnDisk, rows[0].Status);
        Assert.Equal(RenameStatus.Change, rows[1].Status);
    }

    [Fact]
    public void Plan_collision_checks_respect_case_sensitivity()
    {
        var o = D with { Prefix = "new_" };
        Assert.Equal(RenameStatus.Change, Plan(o, new[] { "NEW_A.TXT" }, false, "a.txt")[0].Status);
        Assert.Equal(RenameStatus.ExistsOnDisk, Plan(o, new[] { "NEW_A.TXT" }, true, "a.txt")[0].Status);
    }

    [Fact]
    public void Plan_treats_a_pure_case_change_as_a_real_rename_even_on_case_insensitive_fs()
    {
        var rows = Plan(D with { Case = CaseTransform.Upper }, null, true, "abc.txt");
        Assert.Equal("ABC.txt", rows[0].NewName);
        Assert.Equal(RenameStatus.Change, rows[0].Status);
    }

    [Fact]
    public void Plan_flags_invalid_names()
    {
        var rows = Plan(D with { Basename = "", KeepExtension = false }, null, false, "a.txt");
        Assert.Equal(RenameStatus.Invalid, rows[0].Status);

        var slash = Plan(D with { Prefix = "a/b" }, null, false, "c.txt");
        Assert.Equal(RenameStatus.Invalid, slash[0].Status);

        var dots = Plan(D with { Basename = "..", KeepExtension = false }, null, false, "c.txt");
        Assert.Equal(RenameStatus.Invalid, dots[0].Status);
    }

    [Fact]
    public void Plan_flags_regex_errors()
    {
        var rows = Plan(D with { Find = "(", UseRegex = true }, null, false, "a.txt");
        Assert.Equal(RenameStatus.RegexError, rows[0].Status);
        Assert.False(rows[0].WillRename);
    }
}
