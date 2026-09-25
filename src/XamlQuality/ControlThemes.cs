using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>
/// The <c>ControlTheme</c>s a scan's markup declares, each with the control it targets and where it is.
/// </summary>
/// <remarks>
/// ⭐ <b>One reading for every rule that asks which controls are themed.</b> BNXQ1003 checks a themed
/// control's parts and BNXQ1006 its automation peer, and two readings of <c>TargetType</c> would drift
/// apart on the spelling one of them forgot.
/// </remarks>
internal static class ControlThemes
{
    /// <summary>Every <c>ControlTheme</c> in the scan that names a target type, in file order.</summary>
    internal static IEnumerable<(XamlFile File, XElement Theme, string Target, int? Line)> In(XamlScanContext context)
    {
        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement theme in file.Document!.Descendants()
                         .Where(e => string.Equals(e.Name.LocalName, "ControlTheme", StringComparison.Ordinal)))
            {
                if (TargetTypeOf(theme) is not { } target)
                {
                    continue;
                }

                int? line = (theme as IXmlLineInfo).HasLineInfo()
                    ? ((IXmlLineInfo)theme).LineNumber
                    : null;

                yield return (file, theme, target, line);
            }
        }
    }

    /// <summary>
    /// The control a <c>ControlTheme</c> targets, as a bare type name.
    /// </summary>
    /// <remarks>
    /// ⚠ Both spellings occur and each is idiomatic: <c>TargetType="local:Thing"</c> and
    /// <c>TargetType="{x:Type local:Thing}"</c>. Handling one silently halves a rule's reach.
    /// </remarks>
    private static string? TargetTypeOf(XElement theme)
    {
        string? raw = theme.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, "TargetType", StringComparison.Ordinal))
            ?.Value;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (raw.StartsWith('{'))
        {
            // "{x:Type local:Thing}" — take the last whitespace-separated token, minus the brace.
            raw = raw.TrimEnd('}').Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? raw;
        }

        int colon = raw.LastIndexOf(':');
        string name = colon >= 0 ? raw[(colon + 1)..] : raw;
        return name.Length == 0 ? null : name;
    }
}
