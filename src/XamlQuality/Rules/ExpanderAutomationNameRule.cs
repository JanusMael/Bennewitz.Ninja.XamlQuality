using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every <c>&lt;Expander&gt;</c> declares <c>AutomationProperties.Name</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This is the precondition of a style, not a preference.</b> A theme that copies an
/// Expander's name down to its header part has nothing to copy when the host is unnamed — and it
/// fails SILENTLY in that case: binding an unset attached property yields nothing, the setter
/// never applies, and the header goes back to announcing its layout type. A screen reader then
/// says <c>"Grid, button"</c> for every section on the page, with nothing to tell one from
/// another. Nothing about the build or the rendered UI says so.
/// </para>
/// <para>
/// ⚠ <b>Why a blanket accessibility-coverage baseline does not cover this.</b> Those are usually
/// per-file RATCHETS: a file carrying a nonzero baseline can absorb a newly-unnamed Expander by
/// naming some other control and stay green. This asks the exact question with no slack — zero
/// unnamed Expanders, anywhere.
/// </para>
/// <para>
/// ⛔ <b>A markup scan cannot see a defect that lives in a control TEMPLATE</b>, where there is no
/// element to scan. What it can do is verify the input such a template-level fix depends on, which
/// is what this rule is for.
/// </para>
/// </remarks>
public sealed class ExpanderAutomationNameRule : IXamlRule
{
    /// <inheritdoc />
    public string Id => "XQ1001";

    /// <inheritdoc />
    public string Summary => "Every Expander declares AutomationProperties.Name.";

    /// <inheritdoc />
    public XamlRuleResult Analyze(XamlScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<XamlFinding> findings = [];
        int inspected = 0;

        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement element in file.Document!.Descendants())
            {
                // Local name only: the markup namespace differs between Avalonia, WPF and MAUI,
                // and matching a fully-qualified name would make the rule silently inert on two
                // of the three.
                if (!string.Equals(element.Name.LocalName, "Expander", StringComparison.Ordinal))
                {
                    continue;
                }

                inspected++;

                if (AutomationName.IsDeclaredOn(element)) { continue; }

                int? line = (element as IXmlLineInfo).HasLineInfo()
                    ? ((IXmlLineInfo)element).LineNumber
                    : null;

                findings.Add(new XamlFinding(
                    Id,
                    file.Path,
                    file.RelativePath,
                    line,
                    "This Expander has no AutomationProperties.Name, so its header announces its "
                    + "layout type to a screen reader instead of its heading. Give it a name — the "
                    + "same text the header already shows."));
            }
        }

        return new XamlRuleResult(findings, inspected);
    }

}
