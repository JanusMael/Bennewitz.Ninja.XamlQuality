using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every key binding on an items control sits where keyboard focus can reach it.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>A dead key binding reports nothing.</b> A key press is offered to the focused element and
/// then to each of its visual ancestors, so a binding fires only while focus is on its control or
/// somewhere inside it. An items control usually cannot take focus itself; its item containers can,
/// and a binding on a <c>ListBox</c> fires once a row has focus. Make the rows unfocusable too, and
/// the chord goes nowhere: no exception, no log line, rows that still select and highlight. Measured
/// on Avalonia 12.1.3 with one binding in place: a row focused, 1 run; the same list with its rows'
/// <c>Focusable</c> set to <c>False</c>, 0 runs.
/// </para>
/// <para>
/// ⚠ <b>The inverse of the obvious check.</b> Nobody writes <c>Focusable="False"</c> on a list,
/// because a list is unfocusable by default, so the rule asks whether ANYTHING in the binding's path
/// can take focus: the control itself, its item containers, and what its item template puts inside
/// them. Only when all three cannot is the binding reported.
/// </para>
/// <para>
/// ⭐ <b>Where the containers' focus comes from, in the framework's order.</b> A <c>Focusable</c>
/// the element declares; a <c>Style</c> that sets it, which stops the rule because styles are not
/// evaluated; the container's <c>ControlTheme</c> and every theme it is <c>BasedOn</c>; the type's
/// own default, read from the framework's property metadata through the assemblies the scan was
/// given. The theme is the control's <c>ItemContainerTheme</c>, set on it or by its own theme,
/// or else the implicit theme, keyed <c>{x:Type ListBoxItem}</c>, that reaches it from its own
/// resources, an ancestor's, or the application's.
/// </para>
/// <para>
/// ⛔ <b>What a scan cannot see, it does not guess at.</b> A template the scan does not hold is taken
/// to hold nothing focusable for the items control and for its containers, which holds for every
/// stock items control measured in Avalonia's Fluent, Simple and Semi themes. It does not hold for a
/// container that presents items of its own, whose template carries a header that takes focus (a
/// <c>TreeViewItem</c>'s does, in all three), nor for anything else with a template of its own, as
/// an <c>Expander</c>'s header shows. Those, a missing item template, a bound value, a style, and an
/// implicit theme whose reach depends on where a view is placed are named in
/// <see cref="XamlRuleResult.Skipped"/> rather than decided.
/// </para>
/// <para>
/// ⚠ <b>Only items controls are checked.</b> A binding on any other control that cannot take focus
/// is as dead when nothing inside it can (measured: a <c>Border</c> or <c>UserControl</c> holding only
/// text, 0 runs), but deciding that needs every template beneath it, which markup does not carry. A
/// binding on a window always has somewhere to go, since a key press with nothing focused is raised
/// on the window itself.
/// </para>
/// <para>
/// ⛔ <b>A scan given no assemblies checks nothing, and says so.</b> Whether an element is an items
/// control, which container it generates, and what can take focus by default are all read from
/// compiled types, so every key binding in the scan is named as skipped.
/// </para>
/// </remarks>
public sealed class KeyBindingFocusRule : IXamlRule
{
    /// <inheritdoc />
    public string Id => "XQ1005";

    /// <inheritdoc />
    public string Summary => "Every key binding on an items control sits where keyboard focus can reach it.";

    /// <inheritdoc />
    public XamlRuleResult Analyze(XamlScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<(XamlFile File, XElement Host)> hosts = [.. HostsIn(context)];

        if (hosts.Count == 0)
        {
            // Nothing to check, so nothing to resolve: no type is loaded and no constructor runs.
            return XamlRuleResult.Clean(0);
        }

        if (context.Assemblies.Count == 0)
        {
            return XamlRuleResult.Clean(0) with
            {
                Skipped =
                [
                    .. hosts.Select(entry => SkipOf(entry.File, entry.Host,
                        "The scan was given no assemblies, so whether this control presents items, and what beneath it "
                        + "can take focus, could not be read and nothing was checked. Call WithAssemblies with the "
                        + "assembly whose markup this is; the framework is reached through its references.")),
                ],
            };
        }

        Analysis analysis = new(context);
        List<XamlFinding> findings = [];
        List<XamlSkip> skipped = [];
        int inspected = 0;

        foreach ((XamlFile file, XElement host) in hosts)
        {
            Verdict verdict;
            try
            {
                verdict = analysis.Decide(file, host);
            }
            catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
            {
                // A consumer's build output missing a dependency is a skip, never a failed scan.
                verdict = Verdict.Undecided(
                    $"Reading the compiled types behind this {host.Name.LocalName} failed with {ex.GetType().Name}: "
                    + $"{ex.Message} Pass assemblies whose dependencies load beside them.");
            }
            switch (verdict.Outcome)
            {
                case Outcome.NotASubject:
                    break;
                case Outcome.Live:
                    inspected++;
                    break;
                case Outcome.Dead:
                    inspected++;
                    findings.Add(new XamlFinding(Id, file.Path, file.RelativePath, LineOf(host), verdict.Text));
                    break;
                default:
                    skipped.Add(SkipOf(file, host, verdict.Text));
                    break;
            }
        }

        return new XamlRuleResult(findings, inspected) { Skipped = skipped };
    }

    /// <summary>Every element carrying a <c>KeyBindings</c> block with at least one binding in it.</summary>
    private static IEnumerable<(XamlFile File, XElement Host)> HostsIn(XamlScanContext context)
    {
        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement element in file.Document!.Descendants())
            {
                if (element.Elements().Any(child =>
                        child.Name.LocalName.EndsWith(".KeyBindings", StringComparison.Ordinal)
                        && child.Elements().Any(binding => binding.Name.LocalName == "KeyBinding")))
                {
                    yield return (file, element);
                }
            }
        }
    }

    private static XamlSkip SkipOf(XamlFile file, XElement host, string reason) =>
        new(host.Name.LocalName, reason, file.RelativePath, LineOf(host));

    private static int? LineOf(XElement element) =>
        (element as IXmlLineInfo).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : null;

    private enum Outcome
    {
        NotASubject,
        Live,
        Dead,
        Undecided,
    }

    /// <summary>What was decided about one control's key bindings, and the text that explains it.</summary>
    private readonly record struct Verdict(Outcome Outcome, string Text)
    {
        public static readonly Verdict NotASubject = new(Outcome.NotASubject, string.Empty);
        public static readonly Verdict Live = new(Outcome.Live, string.Empty);

        public static Verdict Undecided(string why) => new(Outcome.Undecided, why);
    }

    /// <summary>A property's value as markup states it: text, or an element, and where it was stated.</summary>
    private sealed record Setting(string? Text, XElement? Element, XamlFile File, XElement At)
    {
        public string Where => LineOf(At) is { } line ? $"{File.RelativePath}:{line}" : File.RelativePath;

        public string Shown => Text ?? $"<{Element?.Name.LocalName}>";
    }

    /// <summary>A lookup that found a setting, found nothing, or cannot say.</summary>
    private sealed record Found(Setting? Setting, string? Unknown)
    {
        public static readonly Found Nothing = new(null, null);

        public bool IsSet => Setting is not null;

        public bool IsUnknown => Unknown is not null;

        public static Found Of(Setting setting) => new(setting, null);

        public static Found Undecidable(string why) => new(null, why);
    }

    /// <summary>A control's or a container's focus, as far as markup decides it.</summary>
    /// <param name="CanFocus"><c>true</c> or <c>false</c>, or <c>null</c> when not decidable.</param>
    /// <param name="Because">Why, in words a finding or a skip can use.</param>
    /// <param name="Theme">The theme that applies to it, when one in the scan does.</param>
    private readonly record struct Focus(bool? CanFocus, string Because, Definition? Theme);

    /// <summary>One scan's worth of resolved types and resources, shared by every control it decides.</summary>
    private sealed class Analysis(XamlScanContext context)
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>Property elements whose content is not in the control's visual tree, or is not content.</summary>
        private static readonly string[] OutsideTheTree =
        [
            ".KeyBindings", ".Resources", ".Styles", ".DataTemplates", ".ContextMenu", ".ContextFlyout",
            ".Flyout", ".AttachedFlyout", ".Tip", ".ItemContainerTheme", ".Theme", ".Transitions",
        ];

        private readonly ElementTypes _types = new(context.Assemblies);
        private readonly ResourceScope _scope = new(context);

        public Verdict Decide(XamlFile file, XElement host)
        {
            string hostName = host.Name.LocalName;
            ElementType resolved = _types.Resolve(hostName);

            switch (resolved.Kind)
            {
                case ElementKind.Missing:
                    return Verdict.Undecided(_types.WhyUnresolved(hostName) is { } why
                        ? $"{why}, so whether it presents items could not be read. {LoadedTypes.Remedy}"
                        : $"{hostName} is not a type in the scanned assemblies or the assemblies they reference, so "
                          + "whether it presents items could not be read. Pass the assembly that defines it to WithAssemblies.");
                case ElementKind.Ambiguous:
                    return Verdict.Undecided(
                        $"More than one type named {hostName} takes part in focus in the scanned assemblies, so which "
                        + "one this is could not be told.");
                case ElementKind.Other:
                    return Verdict.NotASubject;
            }

            Type type = resolved.Type!;
            if (!ElementTypes.IsItemsControl(type))
            {
                return Verdict.NotASubject;
            }

            // A Focusable the control declares outranks every theme and style, so a true one settles it.
            Found local = Local(host, file, "Focusable");
            if (local.IsSet && BoolOf(local.Setting!) == true)
            {
                return Verdict.Live;
            }

            (Definition? hostTheme, string? hostThemeUnknown) = ThemeOfHost(host, file, type);
            if (hostThemeUnknown is not null)
            {
                return Verdict.Undecided(hostThemeUnknown);
            }

            Focus hostFocus = FocusOf(host, file, type, local, hostTheme, typeName: hostName);
            if (hostFocus.CanFocus is not false)
            {
                return hostFocus.CanFocus is null ? Verdict.Undecided(hostFocus.Because) : Verdict.Live;
            }

            if (Styled(NamesOf(type), $"the {hostName}", "ItemContainerTheme", "ItemTemplate", "Template", "Theme") is { } hostStyled)
            {
                return Verdict.Undecided(hostStyled);
            }

            Type? container = _types.ContainerOf(type);
            if (container is null)
            {
                return Verdict.Undecided(
                    $"The {hostName} cannot take focus ({hostFocus.Because}), and which container it generates for "
                    + "an item could not be read from its compiled code, so what can take focus beneath it is unknown.");
            }

            (Definition? containerTheme, string? containerThemeUnknown) = ItemContainerThemeOf(host, file, hostTheme, container);
            if (containerThemeUnknown is not null)
            {
                return Verdict.Undecided(containerThemeUnknown);
            }

            List<(XamlFile File, XElement Root)> content = [];
            string containerWhy;
            string dependsOnContent =
                $"Neither the {hostName} ({hostFocus.Because}) nor its {container.Name} containers can take focus, so "
                + "it depends on what they hold. ";
            List<XElement> items = [.. host.Elements().Where(child => !child.Name.LocalName.Contains('.', StringComparison.Ordinal))];

            if (items.Count > 0)
            {
                // Items declared in the markup: each is its own container or is wrapped in a generated one.
                Verdict? inline = DeclaredItems(file, hostName, items, container, containerTheme, content, out containerWhy);
                if (inline is { } decided)
                {
                    return decided;
                }
            }
            else
            {
                Focus generated = ContainerFocus(container, containerTheme);
                if (generated.CanFocus is not false)
                {
                    return generated.CanFocus is null ? Verdict.Undecided(generated.Because) : Verdict.Live;
                }

                if (Hierarchical(container, hostName) is { } header)
                {
                    return Verdict.Undecided(header);
                }

                containerWhy = $"its {container.Name} containers cannot ({generated.Because})";

                if (ItemContent(host, file, type, hostTheme, container, content) is { } unknownContent)
                {
                    return Verdict.Undecided(dependsOnContent + unknownContent);
                }
            }

            if (TemplateContent(containerTheme is null ? Found.Nothing : InTheme(containerTheme, "Template"), content) is { } unknownContainerTemplate)
            {
                return Verdict.Undecided(dependsOnContent + unknownContainerTemplate);
            }

            Found hostTemplate = Local(host, file, "Template");
            if (!hostTemplate.IsSet && hostTheme is not null)
            {
                hostTemplate = InTheme(hostTheme, "Template");
            }

            if (TemplateContent(hostTemplate, content) is { } unknownHostTemplate)
            {
                return Verdict.Undecided(dependsOnContent + unknownHostTemplate);
            }

            (bool? anything, string contentWhy) = CanAnythingFocus(content);
            if (anything is null)
            {
                return Verdict.Undecided(dependsOnContent + contentWhy);
            }

            if (anything.Value)
            {
                return Verdict.Live;
            }

            return new Verdict(Outcome.Dead,
                $"This {hostName} has key bindings that can never fire. A key binding fires only while focus is on "
                + $"its control or inside it, and nothing here can take focus: the {hostName} cannot "
                + $"({hostFocus.Because}), {containerWhy}, and nothing inside them can. Let the containers "
                + "take focus, or put the bindings on a control that can.");
        }

        /// <summary>
        /// Items written in the markup rather than bound: an item of the container's type is its own
        /// container, and anything else is wrapped in a generated one.
        /// </summary>
        private Verdict? DeclaredItems(
            XamlFile file,
            string hostName,
            List<XElement> items,
            Type container,
            Definition? containerTheme,
            List<(XamlFile File, XElement Root)> content,
            out string containerWhy)
        {
            containerWhy = "none of the items declared in it can";
            Focus? generated = null;

            foreach (XElement item in items)
            {
                ElementType resolved = item.Name.Namespace == Xaml
                    ? new ElementType(ElementKind.Other, null)
                    : _types.Resolve(item.Name.LocalName);

                if (resolved.Kind is ElementKind.Missing or ElementKind.Ambiguous)
                {
                    return Verdict.Undecided(
                        $"{item.Name.LocalName}, declared as an item at {Where(file, item)}, is not a type this rule "
                        + "could resolve, so whether it can take focus is unknown.");
                }

                if (resolved.Kind == ElementKind.InputElement && ElementTypes.IsAssignable(container, resolved.Type!))
                {
                    Type own = resolved.Type!;
                    if (Styled(NamesOf(own), $"the {own.Name} items", "Focusable", "Template", "Theme") is { } styled)
                    {
                        return Verdict.Undecided(styled);
                    }

                    (Definition? ownTheme, string? ownThemeUnknown) = ThemeOfItem(item, file, containerTheme, own);
                    if (ownThemeUnknown is not null)
                    {
                        return Verdict.Undecided(ownThemeUnknown);
                    }

                    Focus focus = FocusOf(item, file, own, Local(item, file, "Focusable"), ownTheme, typeName: own.Name);
                    if (focus.CanFocus is not false)
                    {
                        return focus.CanFocus is null ? Verdict.Undecided(focus.Because) : Verdict.Live;
                    }

                    if (Hierarchical(own, hostName) is { } header)
                    {
                        return Verdict.Undecided(header);
                    }

                    content.AddRange(item.Elements().Select(child => (file, child)));
                    if (TemplateContent(ownTheme is null ? Found.Nothing : InTheme(ownTheme, "Template"), content) is { } unknown)
                    {
                        return Verdict.Undecided(unknown);
                    }

                    continue;
                }

                generated ??= ContainerFocus(container, containerTheme);
                if (generated.Value.CanFocus is not false)
                {
                    return generated.Value.CanFocus is null ? Verdict.Undecided(generated.Value.Because) : Verdict.Live;
                }

                if (Hierarchical(container, hostName) is { } generatedHeader)
                {
                    return Verdict.Undecided(generatedHeader);
                }

                containerWhy = $"its {container.Name} containers cannot ({generated.Value.Because}), nor can the items declared in it";
                content.Add((file, item));
            }

            return null;
        }

        /// <summary>
        /// The reason to stop when a container presents items of its own, whose template carries a
        /// header that takes focus; <c>null</c> for any other container.
        /// </summary>
        private static string? Hierarchical(Type container, string hostName) =>
            ElementTypes.IsItemsControl(container) && ElementTypes.IsTemplated(container)
                ? $"Neither the {hostName} nor its {container.Name} containers can take focus, but {A(container.Name)} "
                  + "presents items of its own, and the header its template carries can: a TreeViewItem's does in "
                  + "Avalonia's Fluent, Simple and Semi themes. That template is not in the scan, so this is not "
                  + "decided from markup."
                : null;

        /// <summary>
        /// The templates that fill the generated containers, added to <paramref name="content"/>; the
        /// reason when they cannot be known.
        /// </summary>
        private string? ItemContent(
            XElement host,
            XamlFile file,
            Type type,
            Definition? hostTheme,
            Type container,
            List<(XamlFile File, XElement Root)> content)
        {
            Found itemTemplate = Local(host, file, "ItemTemplate");
            if (!itemTemplate.IsSet && hostTheme is not null)
            {
                itemTemplate = InTheme(hostTheme, "ItemTemplate");
            }

            if (itemTemplate.IsSet)
            {
                if (TemplateContent(itemTemplate, content) is { } unknown)
                {
                    return unknown;
                }
            }
            else if (itemTemplate.IsUnknown)
            {
                return itemTemplate.Unknown;
            }
            else if (!Local(host, file, "DisplayMemberBinding").IsSet)
            {
                XElement[] dataTemplates = [.. host.Elements()
                    .Where(child => child.Name.LocalName.EndsWith(".DataTemplates", StringComparison.Ordinal))
                    .SelectMany(slot => slot.Elements())];

                if (dataTemplates.Length == 0)
                {
                    return $"No ItemTemplate is declared, so what its {container.Name} containers show comes from data "
                           + "templates this rule does not resolve. Declare an ItemTemplate for it to be checked.";
                }

                content.AddRange(dataTemplates.Select(template => (file, template)));
            }

            if (ElementTypes.HasProperty(type, "ContentTemplate"))
            {
                // A control that also presents its selected item's content, as a TabControl does.
                Found contentTemplate = Local(host, file, "ContentTemplate");
                if (!contentTemplate.IsSet && hostTheme is not null)
                {
                    contentTemplate = InTheme(hostTheme, "ContentTemplate");
                }

                if (!contentTemplate.IsSet)
                {
                    return contentTemplate.Unknown
                           ?? "It presents its selected item's content as well, and no ContentTemplate is declared, so "
                           + "what that shows comes from data templates this rule does not resolve.";
                }

                if (TemplateContent(contentTemplate, content) is { } unknown)
                {
                    return unknown;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether an element can take focus: the value it declares, else a style's (undecided), else
        /// its theme's, else its type's default.
        /// </summary>
        private Focus FocusOf(XElement element, XamlFile file, Type type, Found local, Definition? theme, string typeName)
        {
            if (local.IsSet)
            {
                return BoolOf(local.Setting!) switch
                {
                    true => new Focus(true, "it declares Focusable=\"True\"", theme),
                    false => new Focus(false, "it declares Focusable=\"False\"", theme),
                    null => new Focus(null, $"Its Focusable is {local.Setting!.Shown} at {local.Setting.Where}, which markup cannot evaluate.", theme),
                };
            }

            if (Styled(NamesOf(type), $"the {typeName}", "Focusable") is { } styled)
            {
                return new Focus(null, styled, theme);
            }

            return ThemeOrDefault(type, theme);
        }

        /// <summary>Whether a generated container can take focus: a style (undecided), its theme, its type.</summary>
        private Focus ContainerFocus(Type container, Definition? theme)
        {
            if (Styled(NamesOf(container), $"the {container.Name} containers", "Focusable", "Template", "Theme") is { } styled)
            {
                return new Focus(null, styled, theme);
            }

            return ThemeOrDefault(container, theme);
        }

        private Focus ThemeOrDefault(Type type, Definition? theme)
        {
            if (theme is not null)
            {
                Found themed = InTheme(theme, "Focusable");
                if (themed.IsUnknown)
                {
                    return new Focus(null, themed.Unknown!, theme);
                }

                if (themed.IsSet)
                {
                    return BoolOf(themed.Setting!) switch
                    {
                        true => new Focus(true, $"the ControlTheme at {themed.Setting!.Where} sets Focusable to True", theme),
                        false => new Focus(false, $"the ControlTheme at {themed.Setting!.Where} sets Focusable to False", theme),
                        null => new Focus(null, $"The ControlTheme at {themed.Setting!.Where} sets Focusable to {themed.Setting.Shown}, which markup cannot evaluate.", theme),
                    };
                }
            }

            bool? byDefault = _types.FocusableByDefault(type, out string? why);
            return byDefault switch
            {
                true => new Focus(true, $"{A(type.Name)} can by default", theme),
                false => new Focus(false, $"{A(type.Name)} cannot by default", theme),
                null => new Focus(null, why ?? $"Whether {A(type.Name)} can take focus by default could not be read.", theme),
            };
        }

        /// <summary>The theme the control itself uses: the one it names, else its implicit one.</summary>
        private (Definition? Theme, string? Unknown) ThemeOfHost(XElement host, XamlFile file, Type type)
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

            return ImplicitTheme(_types.StyleKeyOf(type).Name, host, $"this {host.Name.LocalName}", "Focusable", "ItemContainerTheme", "ItemTemplate", "Template", "ContentTemplate");
        }

        /// <summary>The theme an item declared as its own container uses.</summary>
        private (Definition? Theme, string? Unknown) ThemeOfItem(XElement item, XamlFile file, Definition? containerTheme, Type type)
        {
            Found named = Local(item, file, "Theme");
            if (named.IsSet)
            {
                (Definition? theme, string? unknown, bool cleared) = ThemeOf(named.Setting!, "its Theme");
                if (!cleared)
                {
                    return (theme, unknown);
                }
            }

            return containerTheme is not null
                ? (containerTheme, null)
                : ImplicitTheme(_types.StyleKeyOf(type).Name, item, $"the {type.Name} at line {LineOf(item)}", "Focusable", "Template");
        }

        /// <summary>
        /// The theme the generated containers use: the <c>ItemContainerTheme</c> the control sets, or its
        /// theme sets, else the container type's implicit theme.
        /// </summary>
        private (Definition? Theme, string? Unknown) ItemContainerThemeOf(XElement host, XamlFile file, Definition? hostTheme, Type container)
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

            return ImplicitTheme(_types.StyleKeyOf(container).Name, host, $"the {container.Name} containers of this {host.Name.LocalName}", "Focusable", "Template");
        }

        /// <summary>
        /// The implicit theme for <paramref name="typeName"/> that reaches <paramref name="from"/>, or an
        /// explanation when one that matters may or may not.
        /// </summary>
        private (Definition? Theme, string? Unknown) ImplicitTheme(string typeName, XElement from, string subject, params string[] relevant)
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
        private (Definition? Theme, string? Unknown, bool Cleared) ThemeOf(Setting setting, string what)
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
        private Found InTheme(Definition theme, string property) => InTheme(theme, property, []);

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
        private string? TemplateContent(Found found, List<(XamlFile File, XElement Root)> content)
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

        /// <summary>
        /// Whether anything in <paramref name="content"/> can take focus: <c>true</c> at the first thing
        /// that can, <c>false</c> when nothing can, <c>null</c> with the reason when something may.
        /// </summary>
        private (bool? Anything, string Why) CanAnythingFocus(List<(XamlFile File, XElement Root)> content)
        {
            string? undecided = null;

            foreach ((XamlFile file, XElement root) in content)
            {
                foreach (XElement element in Walk(root))
                {
                    string name = element.Name.LocalName;
                    ElementType resolved = element.Name.Namespace == Xaml
                        ? new ElementType(ElementKind.Other, null)
                        : _types.Resolve(name);

                    switch (resolved.Kind)
                    {
                        case ElementKind.Other:
                            continue;
                        case ElementKind.Missing when name.EndsWith("Template", StringComparison.Ordinal):
                            // DataTemplate, ControlTemplate and the like wrap content; they are not controls.
                            continue;
                        case ElementKind.Missing or ElementKind.Ambiguous:
                            undecided ??= $"{name} at {Where(file, element)} is not a type this rule could resolve, so "
                                          + "whether it can take focus is unknown.";
                            continue;
                    }

                    Type type = resolved.Type!;
                    Found local = Local(element, file, "Focusable");
                    bool? own = local.IsSet ? BoolOf(local.Setting!) : _types.FocusableByDefault(type, out _);

                    if (own is true)
                    {
                        return (true, string.Empty);
                    }

                    if (own is null)
                    {
                        undecided ??= $"Whether the {name} at {Where(file, element)} can take focus could not be read.";
                    }
                    else if (ElementTypes.IsTemplated(type))
                    {
                        undecided ??= $"The {name} at {Where(file, element)} cannot take focus itself, but its template "
                                      + "is not in the scan and may hold something that can, as an Expander's header does.";
                    }
                }
            }

            return undecided is null ? (false, string.Empty) : (null, undecided);
        }

        /// <summary>An element and what lies inside it in the visual tree, in document order.</summary>
        private static IEnumerable<XElement> Walk(XElement root)
        {
            Stack<XElement> pending = new([root]);
            while (pending.TryPop(out XElement? element))
            {
                string name = element.Name.LocalName;
                if (name.Contains('.', StringComparison.Ordinal))
                {
                    if (OutsideTheTree.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
                    {
                        continue;
                    }
                }
                else
                {
                    yield return element;
                }

                foreach (XElement child in element.Elements().Reverse())
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// The reason to stop when a top-level style sets one of <paramref name="properties"/> on one of
        /// <paramref name="names"/>, or <c>null</c>.
        /// </summary>
        private string? Styled(IReadOnlyCollection<string> names, string subject, params string[] properties) =>
            _scope.StylesSetting(names, properties) is [{ } style, ..]
                ? $"A Style at {style.Where} sets {style.Property} on a selector naming {subject}, and styles are not "
                  + "evaluated, so what can take focus there is not decided from markup."
                : null;

        /// <summary>The value an element sets locally, as an attribute or as a property element.</summary>
        private static Found Local(XElement element, XamlFile file, string property)
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

        /// <summary>A setter's value, from its <c>Value</c> attribute or its <c>Setter.Value</c> element.</summary>
        private static Setting ValueOf(XElement setter, XamlFile file)
        {
            if (setter.Attribute("Value") is { } attribute)
            {
                return new Setting(attribute.Value, null, file, setter);
            }

            XElement? valueElement = setter.Elements().FirstOrDefault(child => child.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal));
            return valueElement?.Elements().FirstOrDefault() is { } value
                ? new Setting(null, value, file, setter)
                : new Setting(valueElement?.Value ?? string.Empty, null, file, setter);
        }

        private static bool SetsProperty(XElement element, string property) =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value is { } written
            && ResourceScope.PropertyName(written) == property;

        /// <summary>A literal <c>True</c> or <c>False</c>, from text or from an <c>x:Boolean</c> element; <c>null</c> otherwise.</summary>
        private static bool? BoolOf(Setting setting)
        {
            string? text = setting.Text ?? (setting.Element is { HasElements: false } element ? element.Value : null);
            return bool.TryParse(text?.Trim(), out bool value) ? value : null;
        }

        private static bool IsNull(string text) =>
            text.Replace(" ", string.Empty, StringComparison.Ordinal) is "{x:Null}" or "{Null}";

        private static IReadOnlyCollection<string> NamesOf(Type type) => ElementTypes.NamesOf(type);

        private static string Where(XamlFile file, XElement element) =>
            LineOf(element) is { } line ? $"{file.RelativePath}:{line}" : file.RelativePath;

        private static string Capitalised(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary>A type name with its indefinite article: "a ListBox", "an ItemsControl".</summary>
        private static string A(string name) =>
            (name.Length > 0 && "AEIOU".Contains(char.ToUpperInvariant(name[0]), StringComparison.Ordinal) ? "an " : "a ") + name;
    }
}
