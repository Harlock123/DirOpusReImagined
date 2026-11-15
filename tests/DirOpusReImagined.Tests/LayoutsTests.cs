using System.Xml.Linq;

namespace DirOpusReImagined.Tests;

public class LayoutsTests
{
    private static SavedLayout Sample(string name = "Photos") => new(name,
        new PanelLayout(new[] { "/a", "/b" }, 1, new[] { "Dirs", "Files" }, GridViewMode.Thumbnails),
        new PanelLayout(new[] { "/c" }, 0, Array.Empty<string>(), GridViewMode.List),
        "Tools");

    [Fact]
    public void A_layout_reads_back_what_was_written()
    {
        var root = new XElement("Settings", new XElement("Theme", "Dark"));
        Layouts.Write(root, new[] { Sample(), Sample("Work") with { ButtonBank = null } });

        var back = Layouts.Read(root);
        Assert.Equal(2, back.Count);
        var p = back[0];
        Assert.Equal("Photos", p.Name);
        Assert.Equal(new[] { "/a", "/b" }, p.Left.Tabs);
        Assert.Equal(1, p.Left.ActiveTab);
        Assert.Equal(new[] { "Dirs", "Files" }, p.Left.HiddenColumns);
        Assert.Equal(GridViewMode.Thumbnails, p.Left.View);
        Assert.Equal(GridViewMode.List, p.Right.View);
        Assert.Equal("Tools", p.ButtonBank);
        Assert.Null(back[1].ButtonBank);
        Assert.Equal("Dark", (string?)root.Element("Theme"));   // the rest of the file is untouched
    }

    [Fact]
    public void Writing_none_removes_the_section()
    {
        var root = new XElement("Settings");
        Layouts.Write(root, new[] { Sample() });
        Layouts.Write(root, Array.Empty<SavedLayout>());
        Assert.Null(root.Element(Layouts.ElementName));
    }

    [Fact]
    public void Broken_entries_are_skipped_and_odd_values_defaulted()
    {
        var root = XElement.Parse("""
            <Settings><Layouts>
              <Layout name=""><Left><Tab>/x</Tab></Left><Right><Tab>/y</Tab></Right></Layout>
              <Layout name="No right"><Left><Tab>/x</Tab></Left></Layout>
              <Layout name="Odd"><Left active="9" view="Sideways" columns="Bogus,Dirs"><Tab>/x</Tab></Left><Right><Tab>/y</Tab></Right></Layout>
            </Layouts></Settings>
            """);
        var odd = Assert.Single(Layouts.Read(root));
        Assert.Equal("Odd", odd.Name);
        Assert.Equal(0, odd.Left.ActiveTab);
        Assert.Equal(GridViewMode.List, odd.Left.View);
        Assert.Equal(new[] { "Dirs" }, odd.Left.HiddenColumns);
    }

    [Fact]
    public void Saving_under_an_existing_name_replaces_it_in_place()
    {
        var list = new List<SavedLayout> { Sample("A"), Sample("B"), Sample("C") };
        var updated = Layouts.Upsert(list, Sample("b") with { ButtonBank = "Main" });
        Assert.Equal(new[] { "A", "b", "C" }, updated.Select(l => l.Name));
        Assert.Equal("Main", updated[1].ButtonBank);
        Assert.Equal(4, Layouts.Upsert(list, Sample("D")).Count);
    }

    [Fact]
    public void Folders_that_are_gone_are_left_out_and_the_showing_tab_kept()
    {
        var panel = new PanelLayout(new[] { "/gone1", "/kept", "/gone2", "/shown" }, 3, Array.Empty<string>(), GridViewMode.List);
        var usable = Layouts.Usable(panel, p => !p.StartsWith("/gone"))!;
        Assert.Equal(new[] { "/kept", "/shown" }, usable.Tabs);
        Assert.Equal(1, usable.ActiveTab);

        // The showing tab gone: the first that survived shows instead.
        var other = Layouts.Usable(panel with { ActiveTab = 2 }, p => !p.StartsWith("/gone"))!;
        Assert.Equal(0, other.ActiveTab);

        Assert.Null(Layouts.Usable(panel, _ => false));
    }
}
