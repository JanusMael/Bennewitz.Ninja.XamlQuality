using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>Whether an element declares an automation name, in any spelling markup allows.</summary>
/// <remarks>
/// <para>
/// ⭐ <b>Shared because two rules asking the same question is how the answers drift.</b> This
/// started as a private helper on <c>ExpanderAutomationNameRule</c>. The moment a second rule
/// needed it, copying it would have recreated — inside this library — exactly the duplication the
/// library exists to end: the guard it came from was itself a copy of a copy, and each copy had
/// learned a different subset of the cases below.
/// </para>
/// <para>
/// ⚠ <b>Two spellings, and checking one is how a rule quietly passes.</b> An attached property is
/// written either as an attribute (<c>AutomationProperties.Name="…"</c>) or as a property ELEMENT
/// (<c>&lt;AutomationProperties.Name&gt;…&lt;/&gt;</c>). The attribute form is far more common,
/// which is exactly why the element form is the one that goes unhandled.
/// </para>
/// <para>
/// ⛔ <b>An empty name is not a name.</b> <c>AutomationProperties.Name=""</c> satisfies any check
/// that only asks whether the attribute is present, and a screen reader announces exactly what it
/// would have announced with no attribute at all. A gate that accepts it reports coverage it does
/// not have — measured on a real codebase whose accessibility guard passed with one in place.
/// </para>
/// </remarks>
internal static class AutomationName
{
    /// <summary>The attached property, as a reader writes it.</summary>
    internal const string Attribute = "AutomationProperties.Name";

    /// <summary>Whether <paramref name="element"/> declares a non-empty automation name.</summary>
    internal static bool IsDeclaredOn(XElement element)
    {
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.Name.LocalName.EndsWith("Name", StringComparison.Ordinal)
                && attribute.Name.LocalName.Contains("AutomationProperties", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return true;
            }
        }

        // XName.LocalName for a property element is "Expander.AutomationProperties" style in some
        // dialects and "AutomationProperties.Name" in others, so match on the suffix rather than
        // on an exact string.
        foreach (XElement child in element.Elements())
        {
            if (child.Name.LocalName.EndsWith(Attribute, StringComparison.Ordinal) && HasContent(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a property element names anything: text, or an element such as a binding.</summary>
    /// <remarks>
    /// ⛔ The element spelling has to clear the same bar as the attribute. An empty, self-closing or
    /// whitespace-only <c>&lt;AutomationProperties.Name&gt;</c> is as empty as <c>Name=""</c>, and
    /// accepting it by name alone is the presence check the remarks above rule out.
    /// </remarks>
    private static bool HasContent(XElement propertyElement) =>
        propertyElement.HasElements || !string.IsNullOrWhiteSpace(propertyElement.Value);
}
