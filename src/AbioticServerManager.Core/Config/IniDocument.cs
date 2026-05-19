using System.Text;

namespace AbioticServerManager.Core.Config;

/// <summary>
/// A comment-preserving, order-preserving INI document. Parsing then writing an untouched
/// document reproduces the original text byte-for-byte (including line endings and a
/// missing trailing newline). Editing a value rewrites only that value.
/// </summary>
public sealed class IniDocument
{
    private readonly List<IniLine> _lines = [];
    private readonly List<string> _terminators = [];

    public IReadOnlyList<IniLine> Lines => _lines;

    public IReadOnlyList<string> SectionNames =>
        [.. _lines.OfType<IniSectionLine>().Select(s => s.Name)];

    public static IniDocument Parse(string text)
    {
        var doc = new IniDocument();
        doc.Load(text);
        return doc;
    }

    private void Load(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var i = 0;
        var currentSection = string.Empty;
        while (i < text.Length)
        {
            var lineStart = i;
            while (i < text.Length && text[i] != '\n' && text[i] != '\r')
            {
                i++;
            }

            var content = text[lineStart..i];

            var terminator = string.Empty;
            if (i < text.Length)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    terminator = "\r\n";
                    i += 2;
                }
                else
                {
                    terminator = text[i].ToString();
                    i++;
                }
            }

            currentSection = AppendParsed(content, currentSection);
            _terminators.Add(terminator);
        }

        return;

        string AppendParsed(string content, string section)
        {
            var trimmed = content.TrimStart();
            if (trimmed.Length == 0)
            {
                _lines.Add(new IniBlankLine());
                return section;
            }

            if (trimmed[0] is ';' or '#')
            {
                _lines.Add(new IniCommentLine(content));
                return section;
            }

            if (trimmed[0] == '[')
            {
                var close = trimmed.IndexOf(']');
                var name = close > 1 ? trimmed[1..close] : trimmed.Trim('[', ']');
                _lines.Add(new IniSectionLine(name, content));
                return name;
            }

            var eq = content.IndexOf('=');
            if (eq < 0)
            {
                // Not a recognised key/value; preserve verbatim as a comment-like line so
                // nothing the app does not understand is ever silently dropped.
                _lines.Add(new IniCommentLine(content));
                return section;
            }

            var key = content[..eq].Trim();
            var valueStart = eq + 1;
            while (valueStart < content.Length && content[valueStart] is ' ' or '\t')
            {
                valueStart++;
            }

            _lines.Add(new IniKeyValueLine
            {
                Section = section,
                Key = key,
                Value = content[valueStart..],
                Raw = content,
            });
            return section;
        }
    }

    public bool TryGetValue(string section, string key, out string value)
    {
        foreach (var line in _lines)
        {
            if (line is IniKeyValueLine kv &&
                string.Equals(kv.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public IReadOnlyList<IniKeyValueLine> GetSection(string section) =>
        [.. _lines.OfType<IniKeyValueLine>()
            .Where(kv => string.Equals(kv.Section, section, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Updates an existing key in place, or appends it under the section (creating the
    /// section header if absent). Untouched lines are never reformatted.
    /// </summary>
    public void SetValue(string section, string key, string value)
    {
        for (var idx = 0; idx < _lines.Count; idx++)
        {
            if (_lines[idx] is IniKeyValueLine kv &&
                string.Equals(kv.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[idx] = kv with { Value = value };
                return;
            }
        }

        InsertNewKey(section, key, value);
    }

    private void InsertNewKey(string section, string key, string value)
    {
        var sectionHeaderIdx = -1;
        for (var idx = 0; idx < _lines.Count; idx++)
        {
            if (_lines[idx] is IniSectionLine s &&
                string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase))
            {
                sectionHeaderIdx = idx;
                break;
            }
        }

        var newLine = new IniKeyValueLine
        {
            Section = section,
            Key = key,
            Value = value,
            Raw = $"{key}={value}",
        };

        if (sectionHeaderIdx < 0)
        {
            EnsureTrailingNewline();
            if (_lines.Count > 0)
            {
                _lines.Add(new IniBlankLine());
                _terminators.Add(NewLine);
            }

            _lines.Add(new IniSectionLine(section, $"[{section}]"));
            _terminators.Add(NewLine);
            _lines.Add(newLine);
            _terminators.Add(string.Empty);
            return;
        }

        var insertAt = sectionHeaderIdx + 1;
        while (insertAt < _lines.Count && _lines[insertAt] is not IniSectionLine)
        {
            insertAt++;
        }

        var hadTerminatorBefore = insertAt - 1 < _terminators.Count &&
                                  _terminators[insertAt - 1].Length > 0;
        if (!hadTerminatorBefore && insertAt - 1 >= 0)
        {
            _terminators[insertAt - 1] = NewLine;
        }

        _lines.Insert(insertAt, newLine);
        _terminators.Insert(insertAt, insertAt == _lines.Count - 1 ? string.Empty : NewLine);
    }

    private void EnsureTrailingNewline()
    {
        if (_terminators.Count > 0 && _terminators[^1].Length == 0)
        {
            _terminators[^1] = NewLine;
        }
    }

    private string NewLine =>
        _terminators.FirstOrDefault(t => t.Length > 0) is { Length: > 0 } nl ? nl : Environment.NewLine;

    public string ToText()
    {
        var sb = new StringBuilder();
        for (var idx = 0; idx < _lines.Count; idx++)
        {
            sb.Append(_lines[idx].Render());
            sb.Append(idx < _terminators.Count ? _terminators[idx] : string.Empty);
        }

        return sb.ToString();
    }
}
