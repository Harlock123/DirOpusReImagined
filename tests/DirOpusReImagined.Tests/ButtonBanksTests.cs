using System.Xml.Linq;

namespace DirOpusReImagined.Tests;

public class ButtonBanksTests
{
    private const string Legacy = """
        <Settings>
          <Buttons>
            <Button><Name>LpButton1</Name><Content>Code</Content><Action>code</Action><Args>%FD%</Args></Button>
            <Button><Name>LPButton31</Name><Content>Configure</Content><Action>%BUTTONCONFIG%</Action></Button>
          </Buttons>
          <Terminal><Command>wt.exe</Command></Terminal>
        </Settings>
        """;

    [Fact]
    public void A_file_from_before_banks_reads_as_one_bank()
    {
        var banks = ButtonBanks.Read(XElement.Parse(Legacy));
        var bank = Assert.Single(banks);
        Assert.Equal("Bank 1", bank.Name);
        Assert.Equal(new[] { "Code", "Configure" }, bank.Buttons.Select(b => b.Content));
        Assert.Equal("%FD%", bank.Buttons[0].Args);
        Assert.Null(bank.Buttons[1].Args);        // missing stays null, as the loader expects
        Assert.Null(bank.Buttons[0].ShellExecute);
    }

    [Fact]
    public void Several_named_banks_read_in_order()
    {
        var root = XElement.Parse("""
            <Settings>
              <Buttons name="Main"><Button><Name>LpButton1</Name><Content>A</Content></Button></Buttons>
              <Buttons name=" Cloud "><Button><Name>LpButton2</Name><Content>B</Content></Button></Buttons>
              <Buttons><Button><Name>LpButton3</Name><Content>C</Content></Button></Buttons>
            </Settings>
            """);
        var banks = ButtonBanks.Read(root);
        Assert.Equal(new[] { "Main", "Cloud", "Bank 3" }, banks.Select(b => b.Name));
        Assert.Equal("C", banks[2].Buttons.Single().Content);
    }

    [Fact]
    public void No_buttons_at_all_gives_one_empty_bank()
    {
        Assert.Empty(Assert.Single(ButtonBanks.Read(XElement.Parse("<Settings/>"))).Buttons);
        Assert.Single(ButtonBanks.Read(null));
    }

    [Fact]
    public void Writing_replaces_the_banks_where_they_were_and_keeps_everything_else()
    {
        var root = XElement.Parse(Legacy);
        var banks = ButtonBanks.Read(root);
        banks.Add(new ButtonBank("Tools", new List<ButtonSettings>
        {
            new() { Name = "LPButton5", Content = "Grep", Action = "grep", Args = null!, ShellExecute = "False" },
        }));

        ButtonBanks.Write(root, banks);

        Assert.Equal(new[] { "Buttons", "Buttons", "Terminal" }, root.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("wt.exe", (string?)root.Element("Terminal")?.Element("Command"));

        var again = ButtonBanks.Read(root);
        Assert.Equal(new[] { "Bank 1", "Tools" }, again.Select(b => b.Name));
        Assert.Equal(new[] { "Code", "Configure" }, again[0].Buttons.Select(b => b.Content));
        var grep = again[1].Buttons.Single();
        Assert.Equal(("LPButton5", "grep", "False"), (grep.Name, grep.Action, grep.ShellExecute));
        Assert.Null(grep.Args);
    }

    [Fact]
    public void Writing_fewer_banks_removes_the_rest()
    {
        var root = XElement.Parse("<Settings><Buttons name='a'/><Buttons name='b'/><Buttons name='c'/></Settings>");
        ButtonBanks.Write(root, new[] { new ButtonBank("only") });
        Assert.Equal("only", (string?)Assert.Single(root.Elements("Buttons")).Attribute("name"));
    }

    [Fact]
    public void Written_button_fields_keep_their_old_order()
    {
        var root = new XElement("Settings");
        ButtonBanks.Write(root, new[] { new ButtonBank("x", new List<ButtonSettings> { new() }) });
        var fields = root.Element("Buttons")!.Element("Button")!.Elements().Select(e => e.Name.LocalName);
        Assert.Equal(new[] { "Name", "Content", "Background", "Foreground", "HorizontalAlignment", "VerticalAlignment",
                             "Margin", "Action", "Args", "ShellExecute", "ShowWindow", "ToolTip" }, fields);
    }

    [Theory]
    [InlineData("<Settings><ActiveButtonBank>2</ActiveButtonBank></Settings>", 3, 2)]
    [InlineData("<Settings><ActiveButtonBank>3</ActiveButtonBank></Settings>", 3, 0)]   // out of range
    [InlineData("<Settings><ActiveButtonBank>-1</ActiveButtonBank></Settings>", 3, 0)]
    [InlineData("<Settings><ActiveButtonBank>two</ActiveButtonBank></Settings>", 3, 0)]
    [InlineData("<Settings/>", 3, 0)]
    public void The_bank_showing_last_time_is_read_back(string xml, int count, int expected)
        => Assert.Equal(expected, ButtonBanks.ReadActive(XElement.Parse(xml), count));

    [Fact]
    public void The_showing_bank_is_saved_once()
    {
        var root = new XElement("Settings");
        ButtonBanks.WriteActive(root, 1);
        ButtonBanks.WriteActive(root, 2);
        Assert.Equal("2", Assert.Single(root.Elements(ButtonBanks.ActiveElement)).Value);
    }

    [Theory]
    [InlineData(0, 1, 3, 1)]
    [InlineData(2, 1, 3, 0)]     // wraps past the end
    [InlineData(0, -1, 3, 2)]    // and before the start
    [InlineData(0, 1, 1, 0)]
    [InlineData(0, 1, 0, 0)]
    public void Stepping_wraps_round(int index, int delta, int count, int expected)
        => Assert.Equal(expected, ButtonBanks.Step(index, delta, count));

    [Fact]
    public void New_banks_get_the_first_free_name()
        => Assert.Equal("Bank 2", ButtonBanks.NewName(new[] { "bank 1", "Bank 3", "Tools" }));

    [Theory]
    [InlineData("LpButton1", 1)]
    [InlineData("LPButton36", 36)]
    [InlineData("lpbutton12", 12)]
    [InlineData("LpButton37", 0)]
    [InlineData("LpButton0", 0)]
    [InlineData("LpButton", 0)]
    [InlineData("Button5", 0)]
    [InlineData(null, 0)]
    public void Saved_names_map_to_button_positions(string? name, int slot)
        => Assert.Equal(slot, ButtonBanks.SlotOf(name));

    [Fact]
    public void A_cloned_bank_is_independent()
    {
        var bank = new ButtonBank("a", new List<ButtonSettings> { new() { Content = "x" } });
        var copy = bank.Clone();
        copy.Name = "b";
        copy.Buttons[0].Content = "y";
        copy.Buttons.Add(new ButtonSettings());
        Assert.Equal(("a", "x", 1), (bank.Name, bank.Buttons[0].Content, bank.Buttons.Count));
    }

    [Fact]
    public void The_shipped_configurations_still_read()
    {
        string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        foreach (var file in new[] { "Configuration.xml", "LINUXConfiguration.xml", "MACConfiguration .xml" })
        {
            var banks = ButtonBanks.Read(XDocument.Load(Path.Combine(repo, file)).Root);
            Assert.NotEmpty(banks[0].Buttons);
            Assert.All(banks.SelectMany(b => b.Buttons), b => Assert.InRange(ButtonBanks.SlotOf(b.Name), 1, 36));
        }
    }
}
