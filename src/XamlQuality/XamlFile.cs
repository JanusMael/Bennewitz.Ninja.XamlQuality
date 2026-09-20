using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>One markup file, read once and offered to every rule in both the forms rules need.</summary>
/// <remarks>
/// <para>
/// ⭐ <b>Read once, not once per rule.</b> The guards this library was extracted from each walked
/// the tree and re-read every file — tolerable for one, wasteful at eleven, and it made the
/// exclusion logic (<c>bin</c>, <c>obj</c>) something every author had to remember to repeat.
/// </para>
/// <para>
/// ⚠ <b>Both forms, because rules genuinely need both.</b> Structural rules want
/// <see cref="Document"/>; rules about how something is SPELLED want <see cref="Text"/>, because
/// an XML parse discards exactly the detail they are asking about — a literal font stack, an
/// attribute's original formatting. Offering one form would push half the rules into re-reading
/// the file themselves, which is where the duplication started last time.
/// </para>
/// <para>
/// ⛔ <b>An unparseable file is surfaced, never silently skipped.</b> A rule that quietly ignores
/// documents it cannot parse reports zero for a file that may be full of violations — and broken
/// markup is exactly when review lapses. <see cref="ParseError"/> is non-null in that case and
/// <see cref="Document"/> is null.
/// </para>
/// </remarks>
public sealed class XamlFile
{
    internal XamlFile(string path, string relativePath, string text)
    {
        Path = path;
        RelativePath = relativePath;
        Text = text;

        try
        {
            // SetLineInfo so findings can point at a line rather than at a whole file.
            Document = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException ex)
        {
            Document = null;
            ParseError = ex.Message;
        }
    }

    /// <summary>Absolute path on disk.</summary>
    public string Path { get; }

    /// <summary>Forward-slashed path relative to the scan root, for messages.</summary>
    public string RelativePath { get; }

    /// <summary>The file's raw text, exactly as on disk.</summary>
    public string Text { get; }

    /// <summary>The parsed document, or <c>null</c> when the file could not be parsed.</summary>
    public XDocument? Document { get; }

    /// <summary>Why parsing failed, or <c>null</c> when it did not.</summary>
    public string? ParseError { get; }

    /// <summary>
    /// The file's lines, split once and reused by rules that report line numbers.
    /// ⚠ Split on <c>\n</c> only, with any trailing <c>\r</c> left in place: the INDEX is what a
    /// line number needs, and normalising here would cost an allocation per line to change
    /// nothing a caller reads.
    /// </summary>
    public IReadOnlyList<string> Lines => _lines ??= Text.Split('\n');

    private string[]? _lines;
}
