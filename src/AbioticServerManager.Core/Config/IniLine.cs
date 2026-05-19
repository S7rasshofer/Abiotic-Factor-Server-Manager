namespace AbioticServerManager.Core.Config;

/// <summary>
/// One physical line of an INI file. The whole point of this model is loss-less
/// round-tripping: comments, blank lines, ordering, casing and unknown keys all survive
/// a parse/edit/write cycle. "Do not hardcode the facility. Discover it."
/// </summary>
public abstract record IniLine
{
    /// <summary>Renders the line content without its terminator.</summary>
    public abstract string Render();
}

public sealed record IniBlankLine : IniLine
{
    public override string Render() => string.Empty;
}

/// <summary>A comment line. <see cref="Raw"/> keeps the marker and original spacing.</summary>
public sealed record IniCommentLine(string Raw) : IniLine
{
    public override string Render() => Raw;
}

/// <summary>A <c>[Section]</c> header. <see cref="Raw"/> preserves exact bracket spacing.</summary>
public sealed record IniSectionLine(string Name, string Raw) : IniLine
{
    public override string Render() => Raw;
}

/// <summary>
/// A <c>key=value</c> assignment. When unmodified the original text is re-emitted verbatim.
/// When the value changes, only the value portion is rewritten while the
/// <c>key</c>/separator/whitespace prefix is preserved.
/// </summary>
public sealed record IniKeyValueLine : IniLine
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required string Raw { get; init; }

    /// <summary>Index of the first '=' in <see cref="Raw"/>, including following whitespace.</summary>
    private int SeparatorEnd
    {
        get
        {
            var eq = Raw.IndexOf('=');
            if (eq < 0)
            {
                return Raw.Length;
            }

            var end = eq + 1;
            while (end < Raw.Length && (Raw[end] == ' ' || Raw[end] == '\t'))
            {
                end++;
            }

            return end;
        }
    }

    public override string Render()
    {
        var eq = Raw.IndexOf('=');
        if (eq < 0)
        {
            return Raw;
        }

        var originalValue = Raw[SeparatorEnd..];
        if (string.Equals(originalValue, Value, StringComparison.Ordinal))
        {
            return Raw;
        }

        return Raw[..SeparatorEnd] + Value;
    }
}
