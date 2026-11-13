using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace AviSite.Web.Content.Md;

/// <summary>
/// One parsed content file (content/**/*.md). The format is plain Markdown with three conventions:
/// <code>
/// ---                      optional front matter: key: value lines
/// title: Page title
/// ---
/// ## section-key           a section; Razor asks for it by this key
/// photo: some-photo        optional "key: value" field lines right under the heading
///                          (lowercase key, colon, space)
/// Markdown body…
/// ### Item title           records inside a section (people, moments, crews…), same rules
/// </code>
/// Lines that are only an HTML comment (&lt;!-- … --&gt;) are notes for editors and are ignored,
/// and so is a top-level "# Heading".
/// </summary>
public sealed partial class MdDocument
{
    public required string Path { get; init; }
    public Dictionary<string, string> Meta { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Text after the front matter and before the first section (e.g. a person's bio).</summary>
    public string Intro { get; private set; } = "";
    public List<MdSection> Sections { get; } = [];
    /// <summary>Typed views built from this document (timeline, people…), rebuilt when the file changes.</summary>
    public ConcurrentDictionary<string, object> Derived { get; } = new();

    public MdSection? Section(string key) => Sections.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^([a-z][a-z0-9-]*):(?:[ \t]+(.*))?$")]
    private static partial Regex FieldLine();

    public static MdDocument Parse(string path, string text)
    {
        var doc = new MdDocument { Path = path };
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            for (i = 1; i < lines.Length && lines[i].Trim() != "---"; i++)
            {
                var m = FieldLine().Match(lines[i].TrimEnd());
                if (m.Success) doc.Meta[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }
            i++;
        }

        var intro = new StringBuilder();
        MdSection? section = null, item = null;
        var buf = intro;
        var inComment = false;
        for (; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var t = line.TrimStart();
            if (inComment) { if (t.Contains("-->")) inComment = false; continue; }
            if (t.StartsWith("<!--")) { if (!t.Contains("-->")) inComment = true; continue; }
            if (line.StartsWith("## "))
            {
                Finish(section, item);
                section = new MdSection { Key = line[3..].Trim(), Line = i + 1 };
                doc.Sections.Add(section);
                item = null;
                buf = section.Raw;
            }
            else if (line.StartsWith("### ") && section is not null)
            {
                Finish(null, item);
                item = new MdSection { Key = line[4..].Trim(), Line = i + 1 };
                section.Items.Add(item);
                buf = item.Raw;
            }
            else if (line.StartsWith("# ") && section is null) { /* file title, for editors */ }
            else buf.Append(line).Append('\n');
        }
        Finish(section, item);
        doc.Intro = intro.ToString().Trim();
        return doc;

        static void Finish(MdSection? s, MdSection? it)
        {
            it?.Seal();
            s?.Seal();
        }
    }

    internal static (Dictionary<string, string> Fields, string Body) SplitFields(string raw)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = raw.Split('\n');
        var k = 0;
        while (k < lines.Length && lines[k].Trim().Length == 0) k++;
        for (; k < lines.Length; k++)
        {
            var m = FieldLine().Match(lines[k]);
            if (!m.Success) break;
            fields[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }
        return (fields, string.Join('\n', lines.Skip(k)).Trim());
    }
}

public sealed class MdSection
{
    public required string Key { get; init; }
    public int Line { get; init; }
    public Dictionary<string, string> Fields { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Markdown body (without field lines and without ### items).</summary>
    public string Body { get; private set; } = "";
    public List<MdSection> Items { get; } = [];
    internal StringBuilder Raw { get; } = new();

    internal void Seal()
    {
        (Fields, Body) = MdDocument.SplitFields(Raw.ToString());
        Raw.Clear();
    }

    public string? Field(string key) => Fields.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    /// <summary>The body's "- item" lines (continuation lines are joined), in order.</summary>
    public IReadOnlyList<string> ListItems => MdRenderer.ListItems(Body);

    /// <summary>The body without its list: the prose paragraphs only.</summary>
    public string Prose => MdRenderer.WithoutLists(Body);
}
