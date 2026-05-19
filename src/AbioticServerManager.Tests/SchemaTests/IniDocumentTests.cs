using AbioticServerManager.Core.Config;

namespace AbioticServerManager.Tests.SchemaTests;

public class IniDocumentTests
{
    private const string Sample =
        "; Abiotic Factor sandbox settings\n" +
        "\n" +
        "[SandboxSettings]\n" +
        "EnemySpawnRate=1.0\n" +
        "HardcoreMode=False\n" +
        "; an unknown future key follows\n" +
        "MysteryFutureKey=42\n";

    [Fact]
    public void Round_trips_untouched_document_byte_for_byte()
    {
        var doc = IniDocument.Parse(Sample);
        Assert.Equal(Sample, doc.ToText());
    }

    [Fact]
    public void Reads_values()
    {
        var doc = IniDocument.Parse(Sample);

        Assert.True(doc.TryGetValue("SandboxSettings", "EnemySpawnRate", out var spawn));
        Assert.Equal("1.0", spawn);
        Assert.True(doc.TryGetValue("SandboxSettings", "HardcoreMode", out var hc));
        Assert.Equal("False", hc);
    }

    [Fact]
    public void Updating_one_value_preserves_comments_and_unknown_keys()
    {
        var doc = IniDocument.Parse(Sample);
        doc.SetValue("SandboxSettings", "HardcoreMode", "True");

        var text = doc.ToText();

        Assert.Contains("HardcoreMode=True", text);
        Assert.Contains("; Abiotic Factor sandbox settings", text);
        Assert.Contains("; an unknown future key follows", text);
        Assert.Contains("MysteryFutureKey=42", text);
        Assert.Contains("EnemySpawnRate=1.0", text);
    }

    [Fact]
    public void Setting_new_key_appends_under_section()
    {
        var doc = IniDocument.Parse(Sample);
        doc.SetValue("SandboxSettings", "BrandNewKey", "hello");

        Assert.True(doc.TryGetValue("SandboxSettings", "BrandNewKey", out var v));
        Assert.Equal("hello", v);
    }

    [Fact]
    public void Setting_value_in_missing_section_creates_section()
    {
        var doc = IniDocument.Parse(Sample);
        doc.SetValue("NewSection", "Key", "Value");

        Assert.Contains("[NewSection]", doc.ToText());
        Assert.True(doc.TryGetValue("NewSection", "Key", out var v));
        Assert.Equal("Value", v);
    }

    [Fact]
    public void Preserves_crlf_line_endings()
    {
        const string crlf = "[S]\r\nA=1\r\nB=2\r\n";
        var doc = IniDocument.Parse(crlf);
        Assert.Equal(crlf, doc.ToText());
    }

    [Fact]
    public void Empty_input_produces_empty_output()
    {
        var doc = IniDocument.Parse(string.Empty);
        Assert.Equal(string.Empty, doc.ToText());
    }
}
