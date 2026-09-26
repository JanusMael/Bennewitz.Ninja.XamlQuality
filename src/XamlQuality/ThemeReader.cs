using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>A property's value as markup states it: text, or an element, and where it was stated.</summary>
internal sealed record Setting(string? Text, XElement? Element, XamlFile File, XElement At)
{
    public string Where => ThemeReader.LineOf(At) is { } line ? $"{File.RelativePath}:{line}" : File.RelativePath;

    public string Shown => Text ?? $"<{Element?.Name.LocalName}>";
}

/// <summary>A lookup that found a setting, found nothing, or cannot say.</summary>
internal sealed record Found(Setting? Setting, string? Unknown)
{
    public static readonly Found Nothing = new(null, null);

    public bool IsSet => Setting is not null;

    public bool IsUnknown => Unknown is not null;

    public static Found Of(Setting setting) => new(setting, null);

    public static Found Undecidable(string why) => new(null, why);
}

/// <summary>
/// What a control's themes and templates say, followed the way the framework follows them, as far as
/// markup shows it.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Shared because two rules resolving one theme is how their answers drift.</b> <c>BNXQ1005</c>
/// asks what a theme says about focus, and <c>BNXQ1009</c> what it says about a container's name.
/// Both follow a control's <c>Theme</c> or its implicit one, an <c>ItemContainerTheme</c>, the
/// <c>BasedOn</c> chain under each, and what an item template holds. This began as
/// <c>BNXQ1005</c>'s own reading and moved here unchanged; each caller names the properties that
/// matter to it where a theme's reach is in doubt.
/// </para>
/// <para>
/// ⚠ <b>What markup cannot decide is said, not guessed.</b> A value that is a binding, a resource the
/// scan does not hold, a theme set in a nested style that applies only in some state, and an implicit
/// theme whose reach depends on where a view is placed all come back as a reason a caller can name
/// in <see cref="XamlRuleResult.Skipped"/>.
/// </para>
/// </remarks>
internal sealed class ThemeReader(ResourceScope scope, ElementTypes types)
{
    private readonly ResourceScope _scope = scope;
    private readonly ElementTypes _types = types;

    /// <summary>The theme the control itself uses: the one it names, else its implicit one.</summary>
    internal (Definition? Theme, string? Unknown) ThemeOfHost(XElement host, XamlFile file, Type type, params string[] relevant)
    {
        Found named = Local(host, file, "Theme");
        if (named.IsSet)
        {
            (Definition? theme, string? unknown, bool cleared) = ThemeOf(named.Setting!, "its Theme");
            if (!cleared)
            {
                return (theme, unknown);
            }
        }

        return ImplicitTheme(_types.StyleKeyOf(type).Name, host, $"this {host.Name.LocalName}", relevant);
    }

    /// <summary>
    /// The theme the generated containers use: the <c>ItemContainerTheme</c> the control sets, or its
    /// theme sets, else the container type's implicit theme.
    /// </summary>
    internal (Definition? Theme, string? Unknown) ItemContainerThemeOf(
        XElement host, XamlFile file, Definition? hostTheme, Type container, params string[] relevant)
    {
        Found named = Local(host, file, "ItemContainerTheme");
        if (!named.IsSet && hostTheme is not null)
        {
            named = InTheme(hostTheme, "ItemContainerTheme");
        }

        if (named.IsUnknown)
        {
            return (null, named.Unknown);
        }

        if (named.IsSet)
        {
            (Definition? theme, string? unknown, bool cleared) = ThemeOf(named.Setting!, "its ItemContainerTheme");
            if (!cleared)
            {
                return (theme, unknown);
            }
        }

        return ImplicitTheme(_types.StyleKeyOf(container).Name, host, $"the {container.Name} containers of this {host.Name.LocalName}", relevant);
    }

    /// <summary>
    /// The implicit theme for <paramref name="typeName"/> that reaches <paramref name="from"/>, or an
    /// explanation when one that matters may or may not.
    /// </summary>
    internal (Definition? Theme, string? Unknown) ImplicitTheme(string typeName, XElement from, string subject, params string[] relevant)
    {
        Lookup lookup = _scope.Find(ResourceScope.ImplicitKey(typeName), from);
        switch (lookup.Reach)
        {
            case Reach.Found:
                return (lookup.Definition, null);
            case Reach.MayReach:
                foreach (Definition candidate in lookup.Others)
                {
                    foreach (string property in relevant)
                    {
                        if (InTheme(candidate, property) is { IsSet: true } or { IsUnknown: true })
                        {
                            return (null,
                                $"An implicit ControlTheme for {typeName} at {candidate.Where} sets {property}, and it "
                                + $"is in no scope this markup shows reaching {subject}: whether it applies depends on "
                                + "where the view is placed at runtime.");
                        }
                    }
                }

                return (null, null);
            default:
                return (null, null);
        }
    }

    /// <summary>A theme a property names: inline, by resource key, or <c>{x:Null}</c>.</summary>
    internal (Definition? Theme, string? Unknown, bool Cleared) ThemeOf(Setting setting, string what)
    {
        if (setting.Element is { } element)
        {
            if (element.Name.LocalName == "ControlTheme")
            {
                return (new Definition(setting.File, element), null, false);
            }

            if (element.Name.LocalName is "StaticResource" or "DynamicResource"
                && element.Attribute("ResourceKey")?.Value is { } resourceKey
                && ResourceScope.NormalizeKey(resourceKey) is { } elementKey)
            {
                return Resolved(_scope.Find(elementKey, element), elementKey, setting, what);
            }

            return (null, $"{Capitalised(what)} at {setting.Where} is a <{element.Name.LocalName}>, which this rule cannot follow.", false);
        }

        string text = setting.Text!.Trim();
        if (IsNull(text))
        {
            return (null, null, true);
        }

        return ResourceScope.ReferencedKey(text) is { } key
            ? Resolved(_scope.Find(key, setting.At), key, setting, what)
            : (null, $"{Capitalised(what)} at {setting.Where} is {text}, which markup cannot evaluate.", false);
    }

    private static (Definition? Theme, string? Unknown, bool Cleared) Resolved(Lookup lookup, string key, Setting setting, string what) =>
        lookup.Reach switch
        {
            Reach.Found when lookup.Definition!.Element.Name.LocalName == "ControlTheme" => (lookup.Definition, null, false),
            Reach.Found => (null, $"{Capitalised(what)} at {setting.Where} names {key}, which is a {lookup.Definition!.Element.Name.LocalName} at {lookup.Definition.Where}, not a ControlTheme.", false),
            Reach.NotInScan when ResourceScope.IsImplicit(key) => (null, null, false),
            Reach.NotInScan => (null, $"{Capitalised(what)} at {setting.Where} names {key}, which is not defined in the scanned markup, so what it sets is unknown.", false),
            _ => (null, $"{Capitalised(what)} at {setting.Where} names {key}, which is defined {lookup.Others.Count} times, none in a scope this markup shows reaching it.", false),
        };

    /// <summary>
    /// What a theme sets <paramref name="property"/> to: its own last setter, else what the theme it
    /// is <c>BasedOn</c> sets, and so on down.
    /// </summary>
    internal Found InTheme(Definition theme, string property) => InTheme(theme, property, []);

    private Found InTheme(Definition theme, string property, HashSet<XElement> seen)
    {
        if (!seen.Add(theme.Element))
        {
            return Found.Nothing;
        }

        // A nested style that sets it changes it in some state only, which markup cannot decide.
        if (theme.Element.Elements()
                .Where(child => child.Name.LocalName == "Style")
                .SelectMany(style => style.DescendantsAndSelf().Where(nested => nested.Name.LocalName == "Style"))
                .Where(style => style.Attribute("Selector")?.Value.Contains("/template/", StringComparison.Ordinal) != true)
                .SelectMany(style => style.Elements())
                .FirstOrDefault(setter => SetsProperty(setter, property)) is { } conditional)
        {
            return Found.Undecidable(
                $"The ControlTheme at {theme.Where} sets {property} in a nested style at line {LineOf(conditional)}, "
                + "which applies only in some state.");
        }

        if (theme.Element.Elements().LastOrDefault(child => SetsProperty(child, property)) is { } setter)
        {
            return Found.Of(ValueOf(setter, theme.File));
        }

        if (theme.Element.Attribute("BasedOn")?.Value is not { } basedOn)
        {
            return Found.Nothing;
        }

        if (ResourceScope.ReferencedKey(basedOn) is not { } key)
        {
            return Found.Undecidable($"The ControlTheme at {theme.Where} is BasedOn {basedOn}, which this rule cannot follow.");
        }

        Lookup lookup = _scope.Find(key, theme.Element, exclude: theme.Element);
        switch (lookup.Reach)
        {
            case Reach.Found:
                return InTheme(lookup.Definition!, property, seen);
            case Reach.NotInScan when ResourceScope.IsImplicit(key):
                // The framework's own theme for the type, which sets nothing this rule reads.
                return Found.Nothing;
            case Reach.NotInScan:
                return Found.Undecidable(
                    $"The ControlTheme at {theme.Where} is BasedOn {key}, which is not defined in the scanned markup.");
            case Reach.MayReach:
                foreach (Definition candidate in lookup.Others)
                {
                    if (InTheme(candidate, property, [.. seen]) is { IsSet: true } or { IsUnknown: true })
                    {
                        return Found.Undecidable(
                            $"The ControlTheme at {theme.Where} is BasedOn {key}, and the implicit theme at "
                            + $"{candidate.Where} sets {property}, but it is in no scope this markup shows reaching it.");
                    }
                }

                return Found.Nothing;
            default:
                return Found.Undecidable(
                    $"The ControlTheme at {theme.Where} is BasedOn {key}, which is defined {lookup.Others.Count} times.");
        }
    }

    /// <summary>Adds what a template setting holds to <paramref name="content"/>; the reason when it cannot.</summary>
    internal string? TemplateContent(Found found, List<(XamlFile File, XElement Root)> content)
    {
        if (found.IsUnknown)
        {
            return found.Unknown;
        }

        if (!found.IsSet)
        {
            return null;
        }

        Setting setting = found.Setting!;
        if (setting.Element is { } element)
        {
            if (element.Name.LocalName is "StaticResource" or "DynamicResource"
                && element.Attribute("ResourceKey")?.Value is { } resourceKey
                && ResourceScope.NormalizeKey(resourceKey) is { } elementKey)
            {
                return Template(_scope.Find(elementKey, element), elementKey, setting, content);
            }

            content.Add((setting.File, element));
            return null;
        }

        string text = setting.Text!.Trim();
        if (IsNull(text))
        {
            return null;
        }

        return ResourceScope.ReferencedKey(text) is { } key
            ? Template(_scope.Find(key, setting.At), key, setting, content)
            : $"The template at {setting.Where} is {text}, which markup cannot evaluate.";
    }

    private static string? Template(Lookup lookup, string key, Setting setting, List<(XamlFile File, XElement Root)> content)
    {
        if (lookup.Reach != Reach.Found)
        {
            return $"The template at {setting.Where} names {key}, which "
                   + (lookup.Reach == Reach.NotInScan ? "is not defined in the scanned markup." : "cannot be told apart from its other definitions.");
        }

        content.Add((lookup.Definition!.File, lookup.Definition.Element));
        return null;
    }

    /// <summary>The value an element sets locally, as an attribute or as a property element.</summary>
    internal static Found Local(XElement element, XamlFile file, string property)
    {
        if (element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == property) is { } attribute)
        {
            return Found.Of(new Setting(attribute.Value, null, file, element));
        }

        if (element.Elements().FirstOrDefault(child => child.Name.LocalName.EndsWith("." + property, StringComparison.Ordinal)) is { } propertyElement)
        {
            return Found.Of(propertyElement.Elements().FirstOrDefault() is { } value
                ? new Setting(null, value, file, propertyElement)
                : new Setting(propertyElement.Value, null, file, propertyElement));
        }

        return Found.Nothing;
    }

    /// <summary>A setter's value: its <c>Value</c> attribute, its <c>Setter.Value</c> element, or its content.</summary>
    /// <remarks>
    /// ⚠ <c>Value</c> is <c>Setter</c>'s content property, so <c>&lt;Setter Property="Template"&gt;&lt;ControlTemplate&gt;</c>
    /// sets it as surely as the other two spellings, and it is how Avalonia's own themes write their
    /// templates. Read as empty, it made a theme's template, container theme or focus undecidable.
    /// </remarks>
    internal static Setting ValueOf(XElement setter, XamlFile file)
    {
        if (setter.Attribute("Value") is { } attribute)
        {
            return new Setting(attribute.Value, null, file, setter);
        }

        if (setter.Elements().FirstOrDefault(child => child.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal)) is { } valueElement)
        {
            return valueElement.Elements().FirstOrDefault() is { } value
                ? new Setting(null, value, file, setter)
                : new Setting(valueElement.Value, null, file, setter);
        }

        return setter.Elements().FirstOrDefault(child => !child.Name.LocalName.Contains('.', StringComparison.Ordinal)) is { } content
            ? new Setting(null, content, file, setter)
            : new Setting(setter.Value, null, file, setter);
    }

    internal static bool SetsProperty(XElement element, string property) =>
        element.Name.LocalName == "Setter"
        && element.Attribute("Property")?.Value is { } written
        && ResourceScope.PropertyName(written) == property;

    internal static bool IsNull(string text) =>
        text.Replace(" ", string.Empty, StringComparison.Ordinal) is "{x:Null}" or "{Null}";

    internal static string Capitalised(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    internal static int? LineOf(XElement element) =>
        (element as IXmlLineInfo).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : null;
}
