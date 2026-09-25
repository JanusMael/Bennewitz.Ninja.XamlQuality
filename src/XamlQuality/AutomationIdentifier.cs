using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>What automation id an element declares, in any spelling markup allows.</summary>
/// <remarks>
/// <para>
/// ⭐ <b>The id's counterpart to <see cref="AutomationName"/>, with the same two spellings and the
/// same bar.</b> The attached property is written as an attribute
/// (<c>AutomationProperties.AutomationId="…"</c>) or as a property element
/// (<c>&lt;AutomationProperties.AutomationId&gt;…&lt;/&gt;</c>), and a check that reads one passes
/// whatever is written in the other.
/// </para>
/// <para>
/// ⚠ <b>MAUI spells it as a plain property.</b> There the id is <c>AutomationId</c> on the element
/// itself, so the bare attribute counts, and so does its property element,
/// <c>&lt;Button.AutomationId&gt;</c>. Avalonia declares the id only as the attached property, as its
/// source at 12.1.3 shows, so accepting both costs nothing there.
/// </para>
/// <para>
/// ⛔ <b><c>x:Name</c> is not an explicit id.</b> Measured on Avalonia 12.1.3 through its runtime XAML
/// loader, a control with <c>x:Name="Go"</c> and nothing else reports the automation id <c>"Go"</c>:
/// the framework derives one. <c>docs/ai-drivable-ui.md</c>'s rule 1 says not to rely on that, because
/// renaming a field then renames what every test and agent searches for.
/// </para>
/// <para>
/// ⛔ <b>A blank id is not an id, and it is worse than none.</b> In the same measurement,
/// <c>AutomationProperties.AutomationId=""</c> beside <c>x:Name="Go"</c> makes the control report
/// <c>""</c>, so the blank value hides even the id the framework would have derived. An empty or
/// self-closing property element sets nothing at all: the attached value stays unset.
/// </para>
/// </remarks>
internal static class AutomationIdentifier
{
    /// <summary>The attached property, as a reader writes it.</summary>
    internal const string Attribute = "AutomationProperties.AutomationId";

    /// <summary>MAUI's plain property, which is also the attached property's member name.</summary>
    private const string Plain = "AutomationId";

    /// <summary>What <paramref name="element"/> declares as its automation id.</summary>
    internal static AutomationIdDeclaration Of(XElement element)
    {
        bool blank = false;

        foreach (XAttribute attribute in element.Attributes())
        {
            if (!IsIdAttribute(attribute.Name.LocalName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(attribute.Value))
            {
                return AutomationIdDeclaration.Declared;
            }

            blank = true;
        }

        foreach (XElement child in element.Elements())
        {
            if (IsIdPropertyElement(child.Name.LocalName, element.Name.LocalName) && HasContent(child))
            {
                return AutomationIdDeclaration.Declared;
            }
        }

        return blank ? AutomationIdDeclaration.Blank : AutomationIdDeclaration.None;
    }

    private static bool IsIdAttribute(string localName) =>
        string.Equals(localName, Plain, StringComparison.Ordinal)
        || localName.EndsWith(Attribute, StringComparison.Ordinal);

    /// <summary>
    /// The attached property's element, or MAUI's element for its own property, such as
    /// <c>&lt;Button.AutomationId&gt;</c> inside a <c>Button</c>.
    /// </summary>
    private static bool IsIdPropertyElement(string localName, string ownerLocalName) =>
        localName.EndsWith(Attribute, StringComparison.Ordinal)
        || string.Equals(localName, ownerLocalName + "." + Plain, StringComparison.Ordinal);

    /// <summary>Whether a property element sets anything: text, or an element such as a binding.</summary>
    private static bool HasContent(XElement propertyElement) =>
        propertyElement.HasElements || !string.IsNullOrWhiteSpace(propertyElement.Value);
}

/// <summary>What an element declares as its automation id.</summary>
internal enum AutomationIdDeclaration
{
    /// <summary>No explicit id in any spelling, or only a property element that sets nothing.</summary>
    None,

    /// <summary>An id attribute whose value is empty or whitespace.</summary>
    Blank,

    /// <summary>A non-blank id, literal or bound, in any spelling.</summary>
    Declared,
}
