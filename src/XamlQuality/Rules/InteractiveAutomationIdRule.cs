using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every interactive control declares an explicit automation id.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The id is how a test or an agent finds a control.</b> A UI Automation client searches by
/// <c>AutomationId</c>, and <c>docs/ai-drivable-ui.md</c>'s rule 1 asks for an explicit one on
/// every control a test or a person needs to reach. The name <see cref="InteractiveAutomationNameRule"/>
/// requires is what a person reads; this is what code searches by, and the two are different jobs.
/// </para>
/// <para>
/// ⛔ <b><c>x:Name</c> does not count, although a framework derives an id from it.</b> Measured on
/// Avalonia 12.1.3, a control with only <c>x:Name="Go"</c> reports the id <c>"Go"</c>. Relying on it
/// makes a field's name the search key of every test, so renaming the field breaks them all. What
/// counts, and what does not, is in <see cref="AutomationIdentifier"/>.
/// </para>
/// <para>
/// ⚠ <b><see cref="InteractiveAutomationNameRule"/>'s framework set, and <c>Expander</c> too.</b> That
/// rule leaves <c>Expander</c> to <see cref="ExpanderAutomationNameRule"/> so that one missing name is
/// not reported twice. No other rule checks an id, so leaving it out here would leave it unchecked.
/// A consumer's own controls, and any element a test reads rather than drives, are passed to
/// <see cref="InteractiveAutomationIdRule(IEnumerable{string})"/>, by local name as elsewhere.
/// </para>
/// <para>
/// ⚠ <b>Present, not unique.</b> Markup can show that an id is declared, and a bound id counts as
/// declared. It cannot show that ids are unique, and a control inside an item template declares one
/// id for every row it produces.
/// </para>
/// </remarks>
public sealed class InteractiveAutomationIdRule : IXamlRule
{
    private readonly HashSet<string> _elements;

    /// <summary>Covers <see cref="InteractiveAutomationNameRule.FrameworkInteractiveElements"/> and <c>Expander</c>.</summary>
    public InteractiveAutomationIdRule()
        : this([])
    {
    }

    /// <summary>
    /// Covers <see cref="InteractiveAutomationNameRule.FrameworkInteractiveElements"/>, <c>Expander</c>,
    /// and <paramref name="additionalElements"/>.
    /// </summary>
    /// <param name="additionalElements">
    /// Element local names of the consumer's own controls, and of anything else a test needs to
    /// find: a custom editor surface, a status text a test reads. A control the consumer wrote is
    /// the one no framework list will ever name.
    /// </param>
    public InteractiveAutomationIdRule(IEnumerable<string> additionalElements)
    {
        ArgumentNullException.ThrowIfNull(additionalElements);
        _elements = new HashSet<string>(InteractiveAutomationNameRule.FrameworkInteractiveElements, StringComparer.Ordinal)
        {
            "Expander",
        };
        _elements.UnionWith(additionalElements);
    }

    /// <inheritdoc />
    public string Id => "BNXQ1007";

    /// <inheritdoc />
    public string Summary => "Every interactive control declares an explicit AutomationId.";

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
                if (!_elements.Contains(element.Name.LocalName))
                {
                    continue;
                }

                inspected++;

                string? message = AutomationIdentifier.Of(element) switch
                {
                    AutomationIdDeclaration.Declared => null,
                    AutomationIdDeclaration.Blank =>
                        $"This {element.Name.LocalName} declares an empty AutomationId. A search by id "
                        + "finds nothing, and on Avalonia the empty value also replaces the id the "
                        + "framework would derive from x:Name. Give it a value that says what the "
                        + "control is for.",
                    _ =>
                        $"This {element.Name.LocalName} has no explicit AutomationId, so a test or an "
                        + "agent can find it only by an id a framework derives from x:Name, which "
                        + "renaming the field changes, or not at all. Give it an "
                        + "AutomationProperties.AutomationId that says what the control is for.",
                };

                if (message is null)
                {
                    continue;
                }

                int? line = (element as IXmlLineInfo).HasLineInfo()
                    ? ((IXmlLineInfo)element).LineNumber
                    : null;

                findings.Add(new XamlFinding(Id, file.Path, file.RelativePath, line, message));
            }
        }

        return new XamlRuleResult(findings, inspected);
    }
}
