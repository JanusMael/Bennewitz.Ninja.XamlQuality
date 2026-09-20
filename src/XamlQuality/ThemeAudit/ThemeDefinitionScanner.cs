using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>What a defined resource resolves to, as far as the audit needs.</summary>
public abstract record ResourceValue
{
    /// <summary>
    /// A literal colour: <c>&lt;Color&gt;#..&lt;/Color&gt;</c> or a brush's literal <c>Color</c>.
    /// <paramref name="Opacity"/> is the brush's own <c>Opacity</c> (1 when absent), folded into
    /// the alpha when the colour is scored.
    /// </summary>
    public sealed record ColorLiteral(AuditColor Color, double Opacity = 1.0) : ResourceValue;

    /// <summary>
    /// An alias to another key: <c>Color="{DynamicResource X}"</c> or <c>ResourceKey="X"</c>.
    /// <paramref name="Opacity"/> is the aliasing brush's own <c>Opacity</c> (1 when absent),
    /// multiplied along the chain when the colour is scored.
    /// </summary>
    public sealed record Alias(string TargetKey, double Opacity = 1.0) : ResourceValue;

    /// <summary>A defined key that is not a resolvable colour (a thickness, a gradient, a control theme).
    /// It still counts as defined for the undefined-key finding; it is just not colour-scored.</summary>
    public sealed record Opaque : ResourceValue;
}

/// <summary>
/// One defined resource: the file and line, the theme variant it belongs to (the
/// <c>ThemeDictionaries</c> child's <c>x:Key</c>, or <c>null</c> for a base resource that applies
/// to every variant), the key, and its value.
/// </summary>
public sealed record DefinedResource(string File, string? Variant, string Key, ResourceValue Value, int Line);

/// <summary>
/// Reads the resource keys a theme <em>defines</em>, per variant and with their colour values, so
/// the inventory can be compared against what consumers reference and scored for contrast. The XML
/// is parsed, not line-matched. A key inside a <c>ResourceDictionary.ThemeDictionaries</c> child is
/// attributed to that child's <c>x:Key</c> variant; a key outside one is a base resource
/// (<c>Variant == null</c>).
/// </summary>
public static class ThemeDefinitionScanner
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Scans every AXAML/XAML file under a directory, leaving out <paramref name="excludedDirectories"/>.</summary>
    /// <exception cref="InvalidDataException">A file is not well-formed XML.</exception>
    public static IReadOnlyList<DefinedResource> Scan(string directory, IReadOnlyList<string>? excludedDirectories = null)
    {
        List<DefinedResource> defined = [];
        foreach (string file in XamlFiles.Enumerate(directory, excludedDirectories))
        {
            defined.AddRange(ScanFile(file));
        }

        return defined;
    }

    /// <summary>Scans one AXAML/XAML file — the unit the inventory composes per contributing file.</summary>
    /// <exception cref="InvalidDataException">The file is not well-formed XML.</exception>
    public static IReadOnlyList<DefinedResource> ScanFile(string file)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(file, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{file}: {ex.Message}", ex);
        }

        List<DefinedResource> defined = [];
        foreach (XElement element in document.Descendants())
        {
            XAttribute? key = element.Attribute(XamlNamespace + "Key");
            if (key is null || IsThemeDictionarySlotChild(element))
            {
                continue;
            }

            int line = ((IXmlLineInfo)element).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : 0;
            defined.Add(new DefinedResource(file, VariantOf(element), key.Value, ValueOf(element), line));
        }

        return defined;
    }

    /// <summary>
    /// A direct child of a <c>ThemeDictionaries</c> slot is a variant container or reference — an
    /// inline <c>ResourceDictionary</c>, a <c>ResourceInclude</c>, or a code-behind dictionary
    /// element — not a defined resource, so its <c>x:Key</c> names a variant, not a resource.
    /// </summary>
    internal static bool IsThemeDictionarySlotChild(XElement element)
    {
        return element.Parent is { } parent
               && parent.Name.LocalName.EndsWith("ThemeDictionaries", StringComparison.Ordinal);
    }

    private static string? VariantOf(XElement element)
    {
        foreach (XElement ancestor in element.Ancestors())
        {
            if (ancestor.Name.LocalName == "ResourceDictionary"
                && ancestor.Attribute(XamlNamespace + "Key") is { } variantKey
                && ancestor.Parent is { } parent
                && parent.Name.LocalName.EndsWith("ThemeDictionaries", StringComparison.Ordinal))
            {
                return variantKey.Value;
            }
        }

        return null;
    }

    /// <summary>The value a keyed resource element resolves to (literal colour, alias, or opaque).</summary>
    internal static ResourceValue ValueOf(XElement element)
    {
        string localName = element.Name.LocalName;

        // <StaticResource x:Key="A" ResourceKey="B" /> — an alias.
        if (localName is "StaticResource" or "DynamicResource"
            && element.Attribute("ResourceKey")?.Value is { } aliasTarget && aliasTarget.Length > 0)
        {
            return new ResourceValue.Alias(aliasTarget);
        }

        // <Color x:Key="A">#RRGGBB</Color>
        if (localName == "Color")
        {
            string text = element.Value.Trim();
            return AuditColor.TryParse(text, out AuditColor literal)
                ? new ResourceValue.ColorLiteral(literal)
                : new ResourceValue.Opaque();
        }

        // <SolidColorBrush x:Key="A" Color="#.." /> or Color="{DynamicResource X}", either with an
        // optional Opacity that scales the colour as seen.
        if (localName.EndsWith("SolidColorBrush", StringComparison.Ordinal))
        {
            double opacity = OpacityOf(element);
            string? colorAttribute = element.Attribute("Color")?.Value;
            if (colorAttribute is not null)
            {
                if (AuditColor.TryParse(colorAttribute, out AuditColor brushColor))
                {
                    return new ResourceValue.ColorLiteral(brushColor, opacity);
                }

                (ReferenceKind, string Key)? reference = ResourceReferenceScanner
                    .ExtractMarkupReferences(colorAttribute)
                    .Select(r => ((ReferenceKind, string)?)r)
                    .FirstOrDefault();
                if (reference is { } r)
                {
                    return new ResourceValue.Alias(r.Key, opacity);
                }
            }
        }

        return new ResourceValue.Opaque();
    }

    private static double OpacityOf(XElement element)
    {
        string? text = element.Attribute("Opacity")?.Value;
        if (text is not null
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity))
        {
            return Math.Clamp(opacity, 0, 1);
        }

        return 1.0;
    }
}
