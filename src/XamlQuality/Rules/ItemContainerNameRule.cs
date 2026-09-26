using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every item container generated from <c>ItemsSource</c> is named by what it shows, not by its
/// item's type.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>A row an items control generates is named by a fallback unless something names it.</b> A
/// <c>ListBoxItem</c>, <c>ComboBoxItem</c> or <c>TabItem</c> with no <c>AutomationProperties.Name</c>
/// of its own takes the text of a <c>TextBlock</c> at the root of what it presents, else that root's
/// own automation name, else <c>ToString()</c> on its item. A view model that writes no
/// <c>ToString()</c> gives every row its type's full name, and a record gives its type and every
/// property it has. Measured on Avalonia 12.1.3 under Fluent: with a panel or a border at the root of
/// the item template, rows were named <c>Probe.Items.Plain</c>, the type's full name, and a record's
/// <c>Rec { Label = Alpha }</c>; a <c>TextBlock</c> root, a named root, a <c>ToString()</c> override,
/// a container style and an <c>ItemContainerTheme</c> each named them <c>Alpha</c>.
/// </para>
/// <para>
/// ⚠ <b>A <c>TreeViewItem</c> has no fallback at all.</b> Its peer reads
/// <c>AutomationProperties.Name</c> and nothing else, so whatever its template shows, its name is
/// empty until a container style or its <c>ItemContainerTheme</c> gives it one. Measured: all 12
/// item and template shapes tried gave an empty name, and both ways of naming it named the nested
/// levels too.
/// </para>
/// <para>
/// ⭐ <b>What names a container, in the order the rule asks.</b> A <c>Style</c> in the host's own
/// <c>Styles</c>, an ancestor's or the application's, whose selector is the container type alone;
/// the <c>ItemContainerTheme</c> the host or its theme sets, else the container's implicit theme,
/// through its <c>BasedOn</c> chain; for a list or tab row, <c>DisplayMemberBinding</c>; then the
/// item template's root: a <c>TextBlock</c> with text, or any other root that declares
/// <c>AutomationProperties.Name</c> or <c>LabeledBy</c>. A <c>TextBlock</c>'s own automation name does
/// not count, because its peer reads only its text: measured, a named <c>TextBlock</c> with no text
/// left the row to <c>ToString()</c>. When the root is a panel or a decorator that declares neither,
/// or a <c>TextBlock</c> with no text, the name falls to <c>ToString()</c> on the type the template's
/// <c>x:DataType</c> names, which is reported when it is <c>object</c>'s or <c>ValueType</c>'s, or
/// the one the compiler writes for a record.
/// </para>
/// <para>
/// ⛔ <b>What markup cannot decide is named, not guessed.</b> A selector with a state, a class, a
/// name or a context; a naming style in another file; a style that sets the host's template or the
/// container's theme; an item template with no <c>x:DataType</c>, or none at all; an item type that
/// is abstract, an interface, a control, or has a subclass that writes its own <c>ToString()</c>; a
/// root that names itself through a peer of its own; and a container whose peer writes its own name
/// are all named in <see cref="XamlRuleResult.Skipped"/>. So is every <c>ItemsSource</c> in a scan
/// given no assemblies, because which container an items control generates, and how that container
/// is named, are read from compiled types.
/// </para>
/// <para>
/// ⚠ <b>Only rows whose naming was measured are checked.</b> A container is a subject when the peer
/// its <c>OnCreateAutomationPeer</c> creates is Avalonia's <c>ListItemAutomationPeer</c> or
/// <c>TreeViewItemAutomationPeer</c>, read from the container's compiled code, so another
/// framework's rows are left alone. Before Avalonia 12.1.2, by its source, a list row read only a
/// <c>TextBlock</c> root's <c>Text</c>, so a name on any other root did not reach it. The rule reads
/// 12.1.2's order: on an older Avalonia it can miss such a row, and never reports a named one.
/// </para>
/// </remarks>
public sealed class ItemContainerNameRule : IXamlRule
{
    /// <inheritdoc />
    public string Id => "BNXQ1009";

    /// <inheritdoc />
    public string Summary => "Every item container generated from ItemsSource is named by what it shows, not by its item's type.";

    /// <inheritdoc />
    public XamlRuleResult Analyze(XamlScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<(XamlFile File, XElement Host)> hosts = [.. HostsIn(context)];

        if (hosts.Count == 0)
        {
            // Nothing to check, so nothing to resolve: no type is loaded.
            return XamlRuleResult.Clean(0);
        }

        if (context.Assemblies.Count == 0)
        {
            return XamlRuleResult.Clean(0) with
            {
                Skipped =
                [
                    .. hosts.Select(entry => SkipOf(entry.File, entry.Host,
                        "The scan was given no assemblies, so whether this control generates item containers, and "
                        + "how they are named, could not be read and nothing was checked. Call WithAssemblies with "
                        + "the assembly whose markup this is; the framework is reached through its references.")),
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
                case Outcome.Undecided:
                    skipped.Add(SkipOf(file, host, verdict.Text));
                    break;
                default:
                    inspected++;
                    if (verdict.Outcome == Outcome.Unnamed)
                    {
                        findings.Add(new XamlFinding(Id, file.Path, file.RelativePath, ThemeReader.LineOf(host), verdict.Text));
                    }

                    break;
            }
        }

        return new XamlRuleResult(findings, inspected) { Skipped = skipped };
    }

    /// <summary>
    /// Every element that sets <c>ItemsSource</c>, as an attribute or as a property element, but a
    /// template: a <c>TreeDataTemplate</c> sets its children's source, and generates no rows itself.
    /// </summary>
    private static IEnumerable<(XamlFile File, XElement Host)> HostsIn(XamlScanContext context)
    {
        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement element in file.Document!.Descendants())
            {
                if (!element.Name.LocalName.EndsWith("Template", StringComparison.Ordinal)
                    && ThemeReader.Local(element, file, "ItemsSource").IsSet)
                {
                    yield return (file, element);
                }
            }
        }
    }

    private static XamlSkip SkipOf(XamlFile file, XElement host, string reason) =>
        new(host.Name.LocalName, reason, file.RelativePath, ThemeReader.LineOf(host));

    private enum Outcome
    {
        NotASubject,
        Named,
        Unnamed,
        Undecided,
    }

    /// <summary>What was decided about one control's item containers, and the text that explains it.</summary>
    private readonly record struct Verdict(Outcome Outcome, string Text)
    {
        public static readonly Verdict NotASubject = new(Outcome.NotASubject, string.Empty);
        public static readonly Verdict Named = new(Outcome.Named, string.Empty);

        public static Verdict Unnamed(string why) => new(Outcome.Unnamed, why);

        public static Verdict Undecided(string why) => new(Outcome.Undecided, why);
    }

    /// <summary>How a container's automation peer names it, where that was measured.</summary>
    private enum Kind
    {
        /// <summary>Not a row whose naming was measured.</summary>
        None,

        /// <summary>A list or tab row: its own name, else what it presents, else its item's <c>ToString()</c>.</summary>
        Content,

        /// <summary>A tree row: its own name, else nothing.</summary>
        Tree,
    }

    /// <summary>Who wrote the <c>ToString()</c> a type resolves to.</summary>
    private enum Writer
    {
        /// <summary><c>object</c> or <c>ValueType</c>: the type's full name.</summary>
        Nobody,

        /// <summary>The compiler, for a record: its type and every property.</summary>
        Compiler,

        /// <summary>The type or a base, in code.</summary>
        Code,
    }

    /// <summary>How far a style's selector picks the containers.</summary>
    private enum Pick
    {
        /// <summary>It does not name the container type.</summary>
        No,

        /// <summary>One of its alternatives is the container type alone, so it applies to every row.</summary>
        Every,

        /// <summary>It names the container type with a state, a class, a name or a context, so it may apply to some.</summary>
        Some,
    }

    /// <summary>One scan's worth of resolved types, resources and styles, shared by every control it decides.</summary>
    private sealed class Analysis
    {
        private const string Peers = "Avalonia.Automation.Peers.";

        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>
        /// Roots whose automation peer is named by <c>AutomationProperties</c> alone: Avalonia's panels
        /// and decorators, none of which creates a peer of its own in 12.1.3.
        /// </summary>
        private static readonly HashSet<string> Layout = new(StringComparer.Ordinal)
        {
            "Border", "Canvas", "Decorator", "DockPanel", "Grid", "Panel", "RelativePanel", "StackPanel",
            "UniformGrid", "Viewbox", "VirtualizingStackPanel", "WrapPanel",
        };

        private readonly XamlScanContext _context;
        private readonly ElementTypes _types;
        private readonly ResourceScope _scope;
        private readonly ThemeReader _themes;
        private readonly Dictionary<Type, (Kind Kind, string? Why)> _kinds = [];
        private List<Type>? _supplied;

        public Analysis(XamlScanContext context)
        {
            _context = context;
            _types = new ElementTypes(context.Assemblies);
            _scope = new ResourceScope(context);
            _themes = new ThemeReader(_scope, _types);
        }

        public Verdict Decide(XamlFile file, XElement host)
        {
            string hostName = host.Name.LocalName;
            ElementType resolved = _types.Resolve(hostName);

            switch (resolved.Kind)
            {
                case ElementKind.Missing:
                    return Verdict.Undecided(_types.WhyUnresolved(hostName) is { } why
                        ? $"{why}, so whether it generates item containers could not be read. {LoadedTypes.Remedy}"
                        : $"{hostName} is not a type in the scanned assemblies or the assemblies they reference, so "
                          + "whether it generates item containers could not be read. Pass the assembly that defines it to WithAssemblies.");
                case ElementKind.Ambiguous:
                    return Verdict.Undecided(
                        $"More than one type named {hostName} is a control in the scanned assemblies, so which one "
                        + "this is could not be told.");
                case ElementKind.Other:
                    return Verdict.NotASubject;
            }

            Type type = resolved.Type!;
            if (!ElementTypes.IsItemsControl(type))
            {
                return Verdict.NotASubject;
            }

            Type? container = _types.ContainerOf(type);
            if (container is null)
            {
                return Verdict.Undecided(
                    $"Which container this {hostName} generates for an item could not be read from its compiled "
                    + "code, so how its items are named is unknown.");
            }

            (Kind kind, string? peerWhy) = KindOf(container);
            if (peerWhy is not null)
            {
                return Verdict.Undecided(peerWhy);
            }

            if (kind == Kind.None)
            {
                return Verdict.NotASubject;
            }

            IReadOnlyCollection<string> chain = ElementTypes.NamesOf(container);
            string styleKey = _types.StyleKeyOf(container).Name;
            string rows = $"its {container.Name} containers";

            // A style whose selector is the container type alone names every row, whatever else applies.
            IReadOnlyList<(XamlFile File, XElement Style)> reaching = _scope.StylesReaching(host);
            if (reaching.Any(entry => Picks(entry.Style, styleKey, chain) == Pick.Every && NamingSetter(entry.Style, entry.File) is not null))
            {
                return Verdict.Named;
            }

            // A style that changes which theme or template applies is not evaluated.
            if (Styled(ElementTypes.NamesOf(type), $"the {hostName}", "ItemContainerTheme", "ItemTemplate", "DisplayMemberBinding", "Theme") is { } hostStyled)
            {
                return Verdict.Undecided(hostStyled);
            }

            if (Styled(chain, $"the {container.Name} containers", "Theme") is { } containerStyled)
            {
                return Verdict.Undecided(containerStyled);
            }

            (Definition? hostTheme, string? hostThemeUnknown) =
                _themes.ThemeOfHost(host, file, type, "ItemContainerTheme", "ItemTemplate", "DisplayMemberBinding");
            if (hostThemeUnknown is not null)
            {
                return Verdict.Undecided(hostThemeUnknown);
            }

            (Definition? containerTheme, string? containerThemeUnknown) =
                _themes.ItemContainerThemeOf(host, file, hostTheme, container, "Name", "LabeledBy");
            if (containerThemeUnknown is not null)
            {
                return Verdict.Undecided(containerThemeUnknown);
            }

            if (containerTheme is not null)
            {
                foreach (string property in (string[])["Name", "LabeledBy"])
                {
                    Found set = _themes.InTheme(containerTheme, property);
                    if (set.IsUnknown)
                    {
                        return Verdict.Undecided(set.Unknown!);
                    }

                    if (set.IsSet && IsAutomationProperty(set.Setting!.At) && Names(set.Setting!))
                    {
                        return Verdict.Named;
                    }
                }
            }

            // A style that may name them in some state or place, or from where the view is placed.
            foreach ((XamlFile styleFile, XElement style) in reaching)
            {
                if (Picks(style, styleKey, chain) == Pick.Some && NamingSetter(style, styleFile) is { } some)
                {
                    return Verdict.Undecided(
                        $"A Style at {Where(styleFile, some)} sets {some.Attribute("Property")!.Value} on a selector "
                        + $"that picks {rows} only in some state or some places, and selectors are not evaluated, so "
                        + "whether it names every row is not decided from markup.");
                }
            }

            HashSet<XElement> reached = [.. reaching.Select(entry => entry.Style)];
            foreach ((XamlFile styleFile, XElement style) in _scope.Styles)
            {
                if (styleFile != file
                    && !reached.Contains(style)
                    && Picks(style, styleKey, chain) != Pick.No
                    && NamingSetter(style, styleFile) is { } elsewhere)
                {
                    return Verdict.Undecided(
                        $"A Style at {Where(styleFile, elsewhere)} sets {elsewhere.Attribute("Property")!.Value} on "
                        + $"{rows}, and it is in no scope this markup shows reaching this {hostName}: whether it "
                        + "applies depends on where the view is placed at runtime.");
                }
            }

            if (kind == Kind.Tree)
            {
                return Verdict.Unnamed(
                    $"The {container.Name} containers this {hostName} generates from ItemsSource have no name. "
                    + $"{ThemeReader.Capitalised(A(container.Name))}'s automation peer reads AutomationProperties.Name and nothing else: not "
                    + "its header, its template or its item. Set AutomationProperties.Name, bound to what each item "
                    + $"shows, in its ItemContainerTheme or in a Style for {styleKey}.");
            }

            return ByContent(file, host, hostName, hostTheme, container);
        }

        /// <summary>A list or tab row that nothing names itself: named by what it presents, else its item's <c>ToString()</c>.</summary>
        private Verdict ByContent(XamlFile file, XElement host, string hostName, Definition? hostTheme, Type container)
        {
            string rows = $"its {container.Name} containers";

            Found display = ThemeReader.Local(host, file, "DisplayMemberBinding");
            if (!display.IsSet && hostTheme is not null)
            {
                display = _themes.InTheme(hostTheme, "DisplayMemberBinding");
            }

            if (display.IsUnknown)
            {
                return Verdict.Undecided(display.Unknown!);
            }

            if (display.IsSet && Names(display.Setting!))
            {
                // Each row presents a TextBlock bound to the member.
                return Verdict.Named;
            }

            Found itemTemplate = ThemeReader.Local(host, file, "ItemTemplate");
            if (!itemTemplate.IsSet && hostTheme is not null)
            {
                itemTemplate = _themes.InTheme(hostTheme, "ItemTemplate");
            }

            if (itemTemplate.IsUnknown)
            {
                return Verdict.Undecided(itemTemplate.Unknown!);
            }

            List<(XamlFile File, XElement Root)> content = [];
            if (itemTemplate.IsSet && _themes.TemplateContent(itemTemplate, content) is { } unknownTemplate)
            {
                return Verdict.Undecided(unknownTemplate);
            }

            if (content.Count == 0)
            {
                return Verdict.Undecided(
                    $"No ItemTemplate is declared, so {rows} present what a data template chosen by each item's "
                    + "type shows, or else its ToString(), and neither is decided from markup. Declare an "
                    + "ItemTemplate with an x:DataType for it to be checked.");
            }

            (XamlFile templateFile, XElement template) = content[0];
            if (!template.Name.LocalName.EndsWith("DataTemplate", StringComparison.Ordinal))
            {
                return Verdict.Undecided(
                    $"The ItemTemplate at {Where(templateFile, template)} is {A(template.Name.LocalName)}, which "
                    + "this rule cannot follow.");
            }

            XElement? root = template.Elements().FirstOrDefault(child => !child.Name.LocalName.Contains('.', StringComparison.Ordinal));
            string because;
            if (root is null)
            {
                because = $"the item template at {Where(templateFile, template)} is empty";
            }
            else if (root.Name.LocalName is "TextBlock" or "SelectableTextBlock")
            {
                // A TextBlock's peer reads its text alone, so a name declared on it does not count.
                if (HasText(root))
                {
                    return Verdict.Named;
                }

                because = $"the root of the item template at {Where(templateFile, root)} is {A(root.Name.LocalName)} with no text";
            }
            else if (AutomationName.IsDeclaredOn(root) || DeclaresLabeledBy(root))
            {
                return Verdict.Named;
            }
            else if (Layout.Contains(root.Name.LocalName))
            {
                because = $"the root of the item template at {Where(templateFile, root)}, {A(root.Name.LocalName)}, declares no name";
            }
            else
            {
                return Verdict.Undecided(
                    $"The root of the item template at {Where(templateFile, root)} is {A(root.Name.LocalName)}, "
                    + $"whose own automation peer may name {rows} from what it holds, and that is not decided "
                    + "from markup.");
            }

            return ByToString(templateFile, template, hostName, container, because);
        }

        /// <summary>A row named by its item's <c>ToString()</c>, on the type the item template declares.</summary>
        private Verdict ByToString(XamlFile file, XElement template, string hostName, Type container, string because)
        {
            string falls = $"Nothing names the {container.Name} containers this {hostName} generates from ItemsSource "
                           + $"but each item's ToString(), because {because}";

            XAttribute? declared = template.Attribute(Xaml + "DataType") ?? template.Attribute("DataType");
            if (declared is null)
            {
                return Verdict.Undecided(
                    $"{falls}, and the item template declares no x:DataType, so the item type is not in the markup. "
                    + "Declare its x:DataType for it to be checked.");
            }

            if (ItemTypeOf(declared.Value, template) is not { } itemType)
            {
                return Verdict.Undecided(
                    $"{falls}, and its x:DataType, {declared.Value}, is not a type in the scanned assemblies or the "
                    + "assemblies they reference.");
            }

            string? unknowable = itemType == typeof(object) ? "object, which every item is"
                : itemType.IsInterface ? $"{itemType.Name}, an interface"
                : itemType.IsAbstract ? $"{itemType.Name}, which is abstract"
                : null;
            if (unknowable is not null)
            {
                return Verdict.Undecided(
                    $"{falls}, and its x:DataType is {unknowable}, so which ToString() the items have depends on "
                    + "their concrete types, which markup does not show.");
            }

            if (ElementTypes.IsInputElement(itemType))
            {
                return Verdict.Undecided(
                    $"The items are controls, {itemType.Name}, which a container presents as they are, named by "
                    + "their own automation peers, and that is not decided from markup.");
            }

            Writer writer = WriterOf(itemType);
            if (writer == Writer.Code)
            {
                return Verdict.Named;
            }

            if (!itemType.IsSealed && SubclassWriting(itemType) is { } subclass)
            {
                return Verdict.Undecided(
                    $"{falls}. {subclass.Name} derives from {itemType.Name} and writes its own ToString(), and "
                    + "which types the items are is not in the markup.");
            }

            string remedy = "Bind AutomationProperties.Name to what each item shows, on the template's root or in "
                            + $"the ItemContainerTheme, or give {itemType.Name} a ToString() of its own.";
            return Verdict.Unnamed(writer == Writer.Compiler
                ? $"{falls}. {itemType.Name} is a record, and the ToString() the compiler writes for it lists its "
                  + $"type and every property, so each row is named \"{itemType.Name} {{ Property = value, … }}\". {remedy}"
                : $"{falls}. {itemType.Name} writes no ToString(), so every row is named \"{itemType.FullName}\", "
                  + $"its type's full name. {remedy}");
        }

        /// <summary>
        /// Whether <paramref name="container"/>'s automation peer is one whose naming was measured, read
        /// from the <c>newobj</c> in the <c>OnCreateAutomationPeer</c> it resolves to.
        /// </summary>
        private (Kind Kind, string? Why) KindOf(Type container)
        {
            if (!_kinds.TryGetValue(container, out (Kind Kind, string? Why) known))
            {
                known = ReadKind(container);
                _kinds[container] = known;
            }

            return known;
        }

        private static (Kind Kind, string? Why) ReadKind(Type container)
        {
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            if (container.GetMethod("OnCreateAutomationPeer", Instance, Type.EmptyTypes) is not { } create)
            {
                return (Kind.None, null);
            }

            Type[] peers = [.. CompiledStrings.ConstructedBy(create).Where(IsPeer).Distinct()];
            if (peers.Length != 1)
            {
                return (Kind.None, null);
            }

            Type peer = peers[0];
            (Kind kind, string reads) = Derives(peer, "ListItemAutomationPeer") ? (Kind.Content, "ContentControlAutomationPeer")
                : Derives(peer, "TreeViewItemAutomationPeer") ? (Kind.Tree, "ControlAutomationPeer")
                : (Kind.None, string.Empty);

            if (kind == Kind.None)
            {
                return (Kind.None, null);
            }

            return peer.GetMethod("GetNameCore", Instance, Type.EmptyTypes)?.DeclaringType?.FullName == Peers + reads
                ? (kind, null)
                : (kind, $"Its {container.Name} containers are named by their automation peer, {peer.Name}, which "
                         + "writes its own GetNameCore, so how they are named is not decided from markup.");
        }

        private static bool IsPeer(Type type) => Derives(type, "AutomationPeer");

        private static bool Derives(Type type, string peer)
        {
            for (Type? link = type; link is not null; link = link.BaseType)
            {
                if (link.FullName == Peers + peer)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The type an <c>x:DataType</c> or <c>DataType</c> names, through the <c>xmlns</c> its prefix is
        /// declared with: <c>using:</c>, <c>clr-namespace:</c>, or the XAML language's own types.
        /// </summary>
        private Type? ItemTypeOf(string written, XElement template)
        {
            string name = written.Trim();
            if (name.StartsWith('{') && name.EndsWith('}'))
            {
                // {x:Type m:Item} and {x:Type TypeName=m:Item}.
                string[] parts = name[1..^1].Trim().Split((char[])[' ', '\t', '\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !parts[0].EndsWith("Type", StringComparison.Ordinal))
                {
                    return null;
                }

                name = parts[1].Trim();
                if (name.StartsWith("TypeName=", StringComparison.Ordinal))
                {
                    name = name["TypeName=".Length..].Trim();
                }
            }

            int colon = name.IndexOf(':', StringComparison.Ordinal);
            string prefix = colon >= 0 ? name[..colon] : string.Empty;
            string local = colon >= 0 ? name[(colon + 1)..] : name;
            if (local.Length == 0 || !local.All(character => char.IsLetterOrDigit(character) || character == '_'))
            {
                return null;
            }

            XNamespace? xmlns = prefix.Length == 0 ? template.GetDefaultNamespace() : template.GetNamespaceOfPrefix(prefix);
            if (xmlns is null)
            {
                return null;
            }

            if (xmlns == Xaml)
            {
                // x:String, x:Int32 and the rest of the language's own types.
                return _types.TypeNamed("System", local);
            }

            string uri = xmlns.NamespaceName;
            string? clr = uri.StartsWith("using:", StringComparison.Ordinal) ? uri["using:".Length..]
                : uri.StartsWith("clr-namespace:", StringComparison.Ordinal) ? uri["clr-namespace:".Length..].Split(';')[0]
                : null;

            return clr is null ? null : _types.TypeNamed(clr.Trim(), local);
        }

        /// <summary>Who wrote the public <c>ToString()</c> <paramref name="type"/> resolves to.</summary>
        private static Writer WriterOf(Type type)
        {
            MethodInfo? method = type.GetMethod("ToString", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
            if (method is null || method.DeclaringType == typeof(object) || method.DeclaringType == typeof(ValueType))
            {
                return Writer.Nobody;
            }

            return method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ? Writer.Compiler : Writer.Code;
        }

        /// <summary>A type in the scanned assemblies that derives from <paramref name="type"/> and writes a <c>ToString()</c>.</summary>
        private Type? SubclassWriting(Type type)
        {
            _supplied ??= [.. _context.Assemblies.SelectMany(assembly => LoadedTypes.Of(assembly).Loaded)];

            foreach (Type candidate in _supplied)
            {
                try
                {
                    if (candidate != type && ElementTypes.IsAssignable(type, candidate) && WriterOf(candidate) == Writer.Code)
                    {
                        return candidate;
                    }
                }
                catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
                {
                    // A type that cannot be read cannot be one the items are shown to be.
                }
            }

            return null;
        }

        /// <summary>
        /// How far a style picks containers whose style key is <paramref name="styleKey"/> and whose type
        /// chain is <paramref name="chain"/>. A type selector matches the style key exactly, as Avalonia's
        /// does, and <c>:is()</c> matches any type in the chain; a nested style's <c>^</c> is its parent's.
        /// </summary>
        private static Pick Picks(XElement style, string styleKey, IReadOnlyCollection<string> chain)
        {
            if (style.Attribute("Selector")?.Value is not { } selector)
            {
                return Pick.No;
            }

            Pick pick = Pick.No;
            foreach (string alternative in AtDepthZero(selector, ','))
            {
                List<string> compounds = [.. Compounds(alternative)];
                if (compounds.Count == 0)
                {
                    continue;
                }

                string last = compounds[^1];
                bool picks;
                bool alone;
                if (last.StartsWith('^'))
                {
                    picks = style.Parent is { Name.LocalName: "Style" } parent && Picks(parent, styleKey, chain) != Pick.No;
                    alone = false;
                }
                else
                {
                    (string? type, bool isIs, string qualifiers) = Split(last);
                    picks = type is not null && (isIs ? chain.Contains(type, StringComparer.Ordinal) : type == styleKey);
                    alone = qualifiers.Length == 0 && compounds.Count == 1;
                }

                if (picks)
                {
                    if (alone)
                    {
                        return Pick.Every;
                    }

                    pick = Pick.Some;
                }
            }

            return pick;
        }

        /// <summary>A compound selector's type, whether it is <c>:is()</c>, and what qualifies it.</summary>
        private static (string? Type, bool IsIs, string Qualifiers) Split(string compound)
        {
            if (compound.StartsWith(":is(", StringComparison.Ordinal))
            {
                int close = compound.IndexOf(')', StringComparison.Ordinal);
                return close < 0
                    ? (null, true, compound)
                    : (Unprefixed(compound[4..close].Trim()), true, compound[(close + 1)..]);
            }

            int end = 0;
            while (end < compound.Length && (char.IsLetterOrDigit(compound[end]) || compound[end] is '_' or '|'))
            {
                end++;
            }

            return end == 0 ? (null, false, compound) : (Unprefixed(compound[..end]), false, compound[end..]);
        }

        /// <summary>A selector type without its <c>ns|</c> prefix.</summary>
        private static string Unprefixed(string type) => type[(type.LastIndexOf('|') + 1)..];

        /// <summary>A selector's compound selectors: split at combinators outside parentheses.</summary>
        private static IEnumerable<string> Compounds(string alternative)
        {
            StringBuilder current = new();
            int depth = 0;
            foreach (char character in alternative)
            {
                depth += character switch { '(' => 1, ')' => -1, _ => 0 };
                if (depth == 0 && (char.IsWhiteSpace(character) || character == '>'))
                {
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Clear();
                    }

                    continue;
                }

                current.Append(character);
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }

        /// <summary><paramref name="text"/> split at <paramref name="separator"/> outside parentheses.</summary>
        private static IEnumerable<string> AtDepthZero(string text, char separator)
        {
            int depth = 0;
            int start = 0;
            for (int index = 0; index < text.Length; index++)
            {
                depth += text[index] switch { '(' => 1, ')' => -1, _ => 0 };
                if (depth == 0 && text[index] == separator)
                {
                    yield return text[start..index];
                    start = index + 1;
                }
            }

            yield return text[start..];
        }

        /// <summary>A setter directly in <paramref name="style"/> that names what it applies to, or <c>null</c>.</summary>
        private static XElement? NamingSetter(XElement style, XamlFile file) =>
            style.Elements().FirstOrDefault(setter =>
                setter.Name.LocalName == "Setter"
                && IsAutomationProperty(setter)
                && Names(ThemeReader.ValueOf(setter, file)));

        /// <summary>
        /// Whether a setter sets <c>AutomationProperties.Name</c> or <c>AutomationProperties.LabeledBy</c>,
        /// in any spelling: bare, in parentheses, or with a namespace prefix.
        /// </summary>
        private static bool IsAutomationProperty(XElement setter)
        {
            if (setter.Attribute("Property")?.Value is not { } written)
            {
                return false;
            }

            string property = written.Trim().TrimStart('(').TrimEnd(')').Trim();
            property = property[(property.LastIndexOf(':') + 1)..];
            return property is "AutomationProperties.Name" or "AutomationProperties.LabeledBy";
        }

        /// <summary>Whether a value names something: an element such as a binding, or text that is not blank or null.</summary>
        private static bool Names(Setting setting) =>
            setting.Element is not null
            || (setting.Text is { } text && !string.IsNullOrWhiteSpace(text) && !ThemeReader.IsNull(text.Trim()));

        /// <summary>Whether a <c>TextBlock</c> has text: a <c>Text</c> that is not blank, or inline content.</summary>
        private static bool HasText(XElement textBlock)
        {
            if (textBlock.Attribute("Text") is { } text)
            {
                return !string.IsNullOrWhiteSpace(text.Value) && !ThemeReader.IsNull(text.Value.Trim());
            }

            foreach (XNode node in textBlock.Nodes())
            {
                switch (node)
                {
                    case XText run when !string.IsNullOrWhiteSpace(run.Value):
                        return true;
                    case XElement inline when !inline.Name.LocalName.Contains('.', StringComparison.Ordinal):
                        return true;
                    case XElement property when (property.Name.LocalName.EndsWith(".Text", StringComparison.Ordinal)
                                                 || property.Name.LocalName.EndsWith(".Inlines", StringComparison.Ordinal))
                                                && (property.HasElements || !string.IsNullOrWhiteSpace(property.Value)):
                        return true;
                }
            }

            return false;
        }

        /// <summary>Whether an element declares <c>AutomationProperties.LabeledBy</c>, in either spelling, with something in it.</summary>
        private static bool DeclaresLabeledBy(XElement element) =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith("AutomationProperties.LabeledBy", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(attribute.Value))
            || element.Elements().Any(child =>
                child.Name.LocalName.EndsWith("AutomationProperties.LabeledBy", StringComparison.Ordinal)
                && (child.HasElements || !string.IsNullOrWhiteSpace(child.Value)));

        /// <summary>
        /// The reason to stop when a top-level style sets one of <paramref name="properties"/> on one of
        /// <paramref name="names"/>, or <c>null</c>.
        /// </summary>
        private string? Styled(IReadOnlyCollection<string> names, string subject, params string[] properties) =>
            _scope.StylesSetting(names, properties) is [{ } style, ..]
                ? $"A Style at {style.Where} sets {style.Property} on a selector naming {subject}, and styles are not "
                  + "evaluated, so how its item containers are named is not decided from markup."
                : null;

        private static string Where(XamlFile file, XElement element) =>
            ThemeReader.LineOf(element) is { } line ? $"{file.RelativePath}:{line}" : file.RelativePath;

        /// <summary>A type name with its indefinite article: "a ListBoxItem", "an Image".</summary>
        private static string A(string name) =>
            (name.Length > 0 && "AEIOU".Contains(char.ToUpperInvariant(name[0]), StringComparison.Ordinal) ? "an " : "a ") + name;
    }
}
