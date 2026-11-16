namespace DirOpusReImagined.Tests;

public class ColumnsTests
{
    [Fact]
    public void Every_file_entry_property_is_a_choosable_column_in_display_order()
    {
        Assert.Equal(new[] { "Typ", "Name", "FileSize", "FileDate", "Dirs", "Files", "Flags", "Tags", "Git" },
                     MainWindow.AllColumns.Select(c => c.Name));
    }

    [Theory]
    [InlineData("Typ", "Icon")]
    [InlineData("FileSize", "Size")]
    [InlineData("FileDate", "Date")]
    [InlineData("Dirs", "Folders inside")]
    [InlineData("Files", "Files inside")]
    [InlineData("Flags", "Attributes")]
    [InlineData("Name", "Name")]
    public void Columns_have_readable_labels(string name, string label)
        => Assert.Equal(label, MainWindow.ColumnLabel(name));

    [Fact]
    public void Hidden_columns_are_saved_in_display_order()
        => Assert.Equal("Typ,Dirs,Flags", MainWindow.FormatHiddenColumns(new[] { "Flags", "Typ", "Dirs" }));

    [Fact]
    public void Nothing_hidden_saves_as_empty()
        => Assert.Equal("", MainWindow.FormatHiddenColumns(Array.Empty<string>()));

    [Fact]
    public void Reading_keeps_only_real_columns_and_never_hides_name()
    {
        var hidden = MainWindow.ParseHiddenColumns(" Dirs, Name ,Bogus,,Files,Dirs ").ToList();
        Assert.Equal(new[] { "Dirs", "Files" }, hidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_saved_hides_nothing(string? saved)
        => Assert.Empty(MainWindow.ParseHiddenColumns(saved));

    [Fact]
    public void Saved_value_reads_back_the_same()
    {
        var hidden = new[] { "Typ", "FileDate", "Flags" };
        Assert.Equal(hidden, MainWindow.ParseHiddenColumns(MainWindow.FormatHiddenColumns(hidden)));
    }
}

public class OptionalColumnsTests
{
    [Fact]
    public void Tags_and_git_are_the_columns_that_only_show_with_something_in_them()
        => Assert.Equal(new[] { "Tags", "Git" }, TaiDataGrid.OptionalColumns);
}
