using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every interactive control declares <c>AutomationProperties.Name</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The broad companion to <see cref="ExpanderAutomationNameRule"/>.</b> That rule asks one
/// exact question about one element, because an unnamed <c>Expander</c> breaks a theme's header
/// binding specifically. This one asks the general question of everything a keyboard can reach: a
/// screen reader announces an unnamed control by its type, so a form of them reads as
/// <c>"button, button, button"</c>.
/// </para>
/// <para>
/// ⚠ <b><c>Expander</c> is deliberately NOT in the default set.</b> Both rules would fire on the
/// same element and a consumer would see one defect reported twice. Run both: this covers the
/// breadth, <c>XQ1001</c> covers the case with the extra failure mode.
/// </para>
/// <para>
/// ⛔ <b>Local names only, and that is load-bearing twice over.</b> The markup namespace differs
/// between Avalonia, WPF and MAUI, so matching a qualified name makes the rule silently inert on
/// two of the three. And a consumer's own controls are in the consumer's namespace, which is why
/// <see cref="InteractiveAutomationNameRule(IEnumerable{string})"/> takes names rather than types.
/// </para>
/// </remarks>
public sealed class InteractiveAutomationNameRule : IXamlRule
{
    /// <summary>
    /// Framework elements that present a focusable control.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Curated, not derived.</b> This is the set two codebases independently converged on
    /// before this library existed; it is not an exhaustive enumeration of every focusable type in
    /// any one framework. Adding to it is a behaviour change for every consumer — a name added
    /// here turns previously-green markup red — so it is public API, and a consumer who wants more
    /// passes them in rather than waiting for this list to grow.
    /// </remarks>
    public static IReadOnlyCollection<string> FrameworkInteractiveElements { get; } =
    [
        "Button",
        "ToggleButton",
        "RepeatButton",
        "TextBox",
        "ComboBox",
        "CheckBox",
        "ToggleSwitch",
        "RadioButton",
        "Slider",
        "NumericUpDown",
        "DataGrid",
        "ListBox",
        "AutoCompleteBox",
        "DatePicker",
        "TimePicker",
        "CalendarDatePicker",
        "MenuItem",
    ];

    private readonly HashSet<string> _elements;

    /// <summary>Covers <see cref="FrameworkInteractiveElements"/>.</summary>
    public InteractiveAutomationNameRule()
        : this([])
    {
    }

    /// <summary>
    /// Covers <see cref="FrameworkInteractiveElements"/> plus <paramref name="additionalElements"/>.
    /// </summary>
    /// <param name="additionalElements">
    /// Element local names of the consumer's own controls that take focus or are clicked — a text
    /// editor surface, a custom gutter, a minimap. ⭐ <b>A control the consumer wrote is exactly
    /// the one no framework list will ever name</b>, and leaving it out means the rule reports
    /// clean over the markup most likely to be wrong.
    /// </param>
    public InteractiveAutomationNameRule(IEnumerable<string> additionalElements)
    {
        ArgumentNullException.ThrowIfNull(additionalElements);
        _elements = new HashSet<string>(FrameworkInteractiveElements, StringComparer.Ordinal);
        _elements.UnionWith(additionalElements);
    }

    /// <inheritdoc />
    public string Id => "XQ1002";

    /// <inheritdoc />
    public string Summary => "Every interactive control declares AutomationProperties.Name.";

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

                if (AutomationName.IsDeclaredOn(element))
                {
                    continue;
                }

                int? line = (element as IXmlLineInfo).HasLineInfo()
                    ? ((IXmlLineInfo)element).LineNumber
                    : null;

                findings.Add(new XamlFinding(
                    Id,
                    file.Path,
                    file.RelativePath,
                    line,
                    $"This {element.Name.LocalName} has no AutomationProperties.Name, so a screen "
                    + "reader announces it as its control type rather than its purpose. Give it a "
                    + "name — the text a sighted reader takes it to mean."));
            }
        }

        return new XamlRuleResult(findings, inspected);
    }
}
