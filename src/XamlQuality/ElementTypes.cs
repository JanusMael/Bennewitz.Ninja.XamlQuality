using System.Reflection;
using System.Runtime.CompilerServices;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>What an element's local name resolved to.</summary>
internal enum ElementKind
{
    /// <summary>No type of that name that loads, in the scanned assemblies or the assemblies they reference.</summary>
    Missing,

    /// <summary>More than one type of that name takes part in focus, so which one is meant is unknown.</summary>
    Ambiguous,

    /// <summary>A type that takes no part in focus: a template, a definition, a brush, a setter.</summary>
    Other,

    /// <summary>A type with a <c>FocusableProperty</c>, so the framework's focus model applies to it.</summary>
    InputElement,
}

/// <summary>An element's local name, resolved.</summary>
/// <param name="Kind">What the name resolved to.</param>
/// <param name="Type">The type, when <paramref name="Kind"/> is <see cref="ElementKind.Other"/> or <see cref="ElementKind.InputElement"/>.</param>
internal readonly record struct ElementType(ElementKind Kind, Type? Type);

/// <summary>
/// The compiled types a scan's markup names, and what the framework they belong to says about them,
/// read through reflection over the assemblies a consumer supplied.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Names, not namespaces.</b> An element resolves by its local name, the way every rule here
/// matches markup, so one reading serves Avalonia's markup and WPF's. The supplied assemblies are
/// searched first and the assemblies they reference second, so a consumer's own control wins over a
/// framework type of the same name, and handing over the application's assembly is enough to reach
/// the framework it is built on.
/// </para>
/// <para>
/// ⛔ <b>A per-type default is registered by the type's static constructor, which loading an
/// assembly does not run.</b> Read cold, every control reports the base default, which for
/// <c>Focusable</c> is <c>false</c> for every control there is: plausible, and wrong. So each class
/// constructor in a type's chain is run before its default is read. Measured on Avalonia 12.1.3: read
/// cold, <c>ListBoxItem</c>, <c>Button</c> and <c>TextBox</c> all report <c>false</c>; after their
/// constructors, <c>true</c>. The entry in <c>docs/avalonia-gotchas.md</c> has the rest.
/// </para>
/// <para>
/// ⚠ <b>Running a class constructor runs the consumer's code.</b> A control's static constructor
/// registers its properties and little else, and a test process that references the control has
/// already agreed to load it. A constructor that throws leaves the type unreadable, and the caller
/// says so rather than guessing.
/// </para>
/// </remarks>
internal sealed class ElementTypes
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private readonly Dictionary<string, List<Type>> _supplied = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Type>> _referenced = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, (bool? Focusable, string? Why)> _focusable = [];
    private readonly Dictionary<Type, Type?> _containers = [];
    private readonly Dictionary<Type, Type> _styleKeys = [];
    private readonly Dictionary<string, (string Source, string Cause)> _notLoaded = new(StringComparer.Ordinal);
    private readonly List<string> _unloadedReferences = [];

    internal ElementTypes(IReadOnlyList<Assembly> assemblies)
    {
        HashSet<string> indexed = new(StringComparer.Ordinal);

        foreach (Assembly assembly in assemblies)
        {
            if (indexed.Add(assembly.GetName().Name ?? string.Empty))
            {
                LoadedTypes types = LoadedTypes.Of(assembly);
                Index(types.Loaded, _supplied);
                foreach ((string name, string cause) in types.NotLoaded)
                {
                    _notLoaded.TryAdd(name, (types.Source, cause));
                }
            }
        }

        foreach (Assembly assembly in assemblies)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (!indexed.Add(reference.Name ?? string.Empty))
                {
                    continue;
                }

                if (Load(reference) is { } loaded)
                {
                    Index(LoadedTypes.Of(loaded).Loaded, _referenced);
                }
                else
                {
                    _unloadedReferences.Add(reference.Name ?? reference.FullName);
                }
            }
        }
    }

    /// <summary>
    /// Why <paramref name="localName"/> resolved to no type, when that is known, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>"Not a type in the scanned assemblies" is wrong when the scan was given it.</b> A
    /// control whose dependencies are not beside its assembly does not load, and a message that told
    /// the consumer to pass the assembly that defines it sent them to do what they had already done.
    /// So a type an assembly defines but could not load is named with what stopped it, and when an
    /// assembly the scanned ones reference could not be loaded, that is said instead.
    /// </remarks>
    internal string? WhyUnresolved(string localName)
    {
        if (_notLoaded.TryGetValue(localName, out (string Source, string Cause) notLoaded))
        {
            return $"{localName}, defined in {notLoaded.Source}, could not be loaded: {notLoaded.Cause}";
        }

        return _unloadedReferences.Count == 0
            ? null
            : $"{localName} is not a type in the scanned assemblies, and {LoadedTypes.List(_unloadedReferences)}, "
              + "which they reference, could not be loaded";
    }

    /// <summary>What <paramref name="localName"/> names: the supplied assemblies first, then what they reference.</summary>
    /// <remarks>
    /// ⚠ A type that takes part in focus outranks one that does not, whichever assembly holds it. A
    /// consumer's model class that happens to share a control's name must not hide the control an
    /// element in markup means, because a hidden <c>Button</c> is a focusable element the rule would miss.
    /// </remarks>
    internal ElementType Resolve(string localName)
    {
        ElementType supplied = Resolve(localName, _supplied);
        if (supplied.Kind is ElementKind.InputElement or ElementKind.Ambiguous)
        {
            return supplied;
        }

        ElementType referenced = Resolve(localName, _referenced);
        if (referenced.Kind is ElementKind.InputElement or ElementKind.Ambiguous || supplied.Kind == ElementKind.Missing)
        {
            return referenced;
        }

        return supplied;
    }

    /// <summary>
    /// The type called <paramref name="name"/> in the CLR namespace <paramref name="clrNamespace"/>: from
    /// the supplied assemblies first, then what they reference, then the core library; <c>null</c> when
    /// none has one, or more than one assembly at the same level does.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>By namespace as well as name</b>, because an <c>x:DataType</c> names its type through the
    /// <c>xmlns</c> that says which namespace it is in, and a consumer's <c>Person</c> is not another
    /// library's. <c>System</c>'s types are reached through facades that forward them rather than
    /// define them, so the core library is asked last.
    /// </remarks>
    internal Type? TypeNamed(string clrNamespace, string name)
    {
        foreach (Dictionary<string, List<Type>> index in (Dictionary<string, List<Type>>[])[_supplied, _referenced])
        {
            if (index.TryGetValue(name, out List<Type>? types))
            {
                Type[] matching = [.. types.Distinct().Where(type => Safely(() => type.Namespace == clrNamespace, false))];
                if (matching.Length > 0)
                {
                    return matching.Length == 1 ? matching[0] : null;
                }
            }
        }

        return Safely(() => typeof(object).Assembly.GetType(clrNamespace + "." + name), (Type?)null);
    }

    /// <summary>
    /// The type a control's implicit theme is keyed by: the one its most-derived <c>StyleKeyOverride</c>
    /// returns with a <c>typeof</c>, else the control's own type.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>A custom control usually borrows its base's theme this way</b>, as TailBlazer's
    /// <c>LinesListBox</c> does with <c>StyleKeyOverride =&gt; typeof(ListBox)</c>, and then the implicit
    /// theme that reaches it is its base's, not one keyed by its own name. The <c>typeof</c> is an
    /// <c>ldtoken</c> in the getter's IL, read without running the control.
    /// </remarks>
    internal Type StyleKeyOf(Type type)
    {
        if (_styleKeys.TryGetValue(type, out Type? known))
        {
            return known;
        }

        Type key = Safely(() =>
        {
            for (Type? link = type; link is not null; link = link.BaseType)
            {
                PropertyInfo? property = link.GetProperties(Declared).FirstOrDefault(candidate => candidate.Name == "StyleKeyOverride");
                if (property?.GetMethod is not { } getter)
                {
                    continue;
                }

                Type[] named = [.. CompiledStrings.TypeOfsIn(getter).Distinct()];
                return named.Length == 1 ? named[0] : type;
            }

            return type;
        }, type);

        _styleKeys[type] = key;
        return key;
    }

    /// <summary>Whether the framework's focus model applies to <paramref name="type"/>; <c>false</c> when unreadable.</summary>
    internal static bool IsInputElement(Type type) => Safely(() => FocusablePropertyOf(type) is not null, false);

    /// <summary>Whether <paramref name="type"/> presents a collection through generated item containers.</summary>
    /// <remarks>By name, as XAML frameworks spell it, so no framework assembly is referenced.</remarks>
    internal static bool IsItemsControl(Type type) => Safely(() => Chain(type).Any(link => link.Name == "ItemsControl"), false);

    /// <summary>
    /// Whether <paramref name="type"/> draws itself through a template it is given; <c>true</c> when
    /// unreadable, since a template is where focus the markup cannot show would hide.
    /// </summary>
    internal static bool IsTemplated(Type type) => HasProperty(type, "Template");

    /// <summary>
    /// Whether <paramref name="type"/> has a property called <paramref name="name"/> a consumer can set;
    /// <c>true</c> when unreadable, so that the caller asks for what the property would hold.
    /// </summary>
    internal static bool HasProperty(Type type, string name) =>
        Safely(() => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property => property.Name == name), true);

    /// <summary>Whether <paramref name="type"/> can stand where <paramref name="container"/> is expected; <c>false</c> when unreadable.</summary>
    internal static bool IsAssignable(Type container, Type type) => Safely(() => container.IsAssignableFrom(type), false);

    /// <summary>The names of <paramref name="type"/> and its bases, as far as they can be read, without <c>Object</c>.</summary>
    internal static IReadOnlyCollection<string> NamesOf(Type type)
    {
        List<string> names = [];
        Safely(() =>
        {
            foreach (Type link in Chain(type).TakeWhile(link => link != typeof(object)))
            {
                names.Add(link.Name);
            }

            return true;
        }, false);

        return names;
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is what reflection throws for a type whose dependencies
    /// cannot be loaded, the one failure a rule reading someone else's build output has to expect.
    /// </summary>
    internal static bool IsUnreadable(Exception exception) =>
        exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException
            or ReflectionTypeLoadException or MissingMemberException or NotSupportedException;

    /// <summary>A reflective read that an unloadable dependency turns into <paramref name="whenUnreadable"/>, never an exception.</summary>
    private static T Safely<T>(Func<T> read, T whenUnreadable)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return whenUnreadable;
        }
    }

    /// <summary>
    /// The value the framework gives <c>Focusable</c> on <paramref name="type"/> when nothing sets it,
    /// or <c>null</c> with <paramref name="why"/> when it cannot be read.
    /// </summary>
    internal bool? FocusableByDefault(Type type, out string? why)
    {
        if (!_focusable.TryGetValue(type, out (bool? Focusable, string? Why) known))
        {
            known = ReadFocusable(type);
            _focusable[type] = known;
        }

        why = known.Why;
        return known.Focusable;
    }

    /// <summary>
    /// The container type an items control generates for an item, read from the <c>newobj</c> in its
    /// most-derived container factory, or <c>null</c> when that cannot be read.
    /// </summary>
    /// <remarks>
    /// ⚠ The factory is <c>CreateContainerForItemOverride</c> in Avalonia and
    /// <c>GetContainerForItemOverride</c> in WPF. One that delegates rather than constructing, as
    /// Avalonia's <c>TreeViewItem</c> does, or that constructs more than one kind of control, has no
    /// single answer and gets none.
    /// </remarks>
    internal Type? ContainerOf(Type itemsControl)
    {
        if (_containers.TryGetValue(itemsControl, out Type? known))
        {
            return known;
        }

        Type? container = Safely(() =>
        {
            for (Type? type = itemsControl; type is not null; type = type.BaseType)
            {
                MethodInfo? factory = type.GetMethods(Declared)
                    .FirstOrDefault(method => method.Name is "CreateContainerForItemOverride" or "GetContainerForItemOverride");
                if (factory is null)
                {
                    continue;
                }

                Type[] built = [.. CompiledStrings.ConstructedBy(factory).Where(IsInputElement).Distinct()];
                return built.Length == 1 ? built[0] : null;
            }

            return null;
        }, (Type?)null);

        _containers[itemsControl] = container;
        return container;
    }

    /// <remarks>
    /// ⛔ A type that cannot be read is not taken for one that takes no part in focus. Reading it as
    /// <see cref="ElementKind.Other"/> would drop it from what can take focus, and a binding it keeps
    /// alive would be reported dead; it resolves as <see cref="ElementKind.Missing"/> instead.
    /// </remarks>
    private static ElementType Resolve(string localName, Dictionary<string, List<Type>> index)
    {
        if (!index.TryGetValue(localName, out List<Type>? types))
        {
            return new ElementType(ElementKind.Missing, null);
        }

        List<Type> inputs = [];
        bool unreadable = false;
        foreach (Type type in types.Distinct())
        {
            switch (Safely<bool?>(() => FocusablePropertyOf(type) is not null, null))
            {
                case true:
                    inputs.Add(type);
                    break;
                case null:
                    unreadable = true;
                    break;
            }
        }

        return (inputs.Count, unreadable) switch
        {
            (1, false) => new ElementType(ElementKind.InputElement, inputs[0]),
            (0, false) => new ElementType(ElementKind.Other, types[0]),
            (0, true) => new ElementType(ElementKind.Missing, null),
            _ => new ElementType(ElementKind.Ambiguous, null),
        };
    }

    private static (bool? Focusable, string? Why) ReadFocusable(Type type)
    {
        try
        {
            // Base first, so an override registered anywhere in the chain is in place before the read.
            foreach (Type link in Chain(type).Reverse())
            {
                RuntimeHelpers.RunClassConstructor(link.TypeHandle);
            }

            object? property = FocusablePropertyOf(type)?.GetValue(null);
            if (property is null)
            {
                return (null, $"{type.Name} has no FocusableProperty to read.");
            }

            object? metadata = property.GetType().GetMethod("GetMetadata", [typeof(Type)])?.Invoke(property, [type]);
            PropertyInfo? defaultValue = metadata?.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(candidate => candidate.Name == "DefaultValue")
                .OrderByDescending(candidate => Chain(candidate.DeclaringType).Count())
                .FirstOrDefault();

            return defaultValue?.GetValue(metadata) is bool value
                ? (value, null)
                : (null, $"The Focusable metadata for {type.Name} has no DefaultValue this rule can read.");
        }
        catch (Exception ex) when (ex is TypeInitializationException or TargetInvocationException
                                       or MemberAccessException or AmbiguousMatchException or TypeLoadException
                                       or FileNotFoundException or FileLoadException or NotSupportedException)
        {
            return (null, $"Reading Focusable for {type.Name} failed with {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static FieldInfo? FocusablePropertyOf(Type type) =>
        type.GetField("FocusableProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

    private static IEnumerable<Type> Chain(Type? type)
    {
        for (Type? link = type; link is not null; link = link.BaseType)
        {
            yield return link;
        }
    }

    private static void Index(IEnumerable<Type> loaded, Dictionary<string, List<Type>> index)
    {
        foreach (Type type in loaded)
        {
            // Markup names top-level types; <Module> and friends are the compiler's. A type whose
            // dependencies do not load cannot even say whether it is nested, and is left out.
            if (Safely(() => type.IsNested || type.Name.StartsWith('<') ? null : type.Name, (string?)null) is not { } name)
            {
                continue;
            }

            if (!index.TryGetValue(name, out List<Type>? types))
            {
                types = [];
                index[name] = types;
            }

            types.Add(type);
        }
    }

    private static Assembly? Load(AssemblyName reference)
    {
        try
        {
            return Assembly.Load(reference);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }
}
