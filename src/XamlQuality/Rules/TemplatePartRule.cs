using System.Reflection;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every template part a control looks up by name is declared in that control's own theme.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This one fails silently and completely.</b> A control asks its template for
/// <c>PART_Minimap</c>; the theme spells it <c>PART_MiniMap</c>, or a refactor renamed one half.
/// The lookup returns null, the control skips the feature, and the build is clean, the app starts,
/// and nothing is logged. The minimap is simply not there. No other check in this library — and no
/// compiler — can see it, because the two halves live in different languages.
/// </para>
/// <para>
/// ⚠ <b>Only one direction is a defect.</b> A part the control looks up and the theme omits is
/// unambiguously broken. The reverse — a name in the theme that no control asks for — is NOT:
/// naming an element so a style can select it (<c>#PART_Badge</c>) is ordinary and correct. A rule
/// that reported both would generate false positives on well-written themes, which is how a rule
/// stops being run. Measured on a real theme set: 25 of 51 part names existed only for selectors.
/// </para>
/// <para>
/// ⛔ <b>Parts are matched inside their own <c>ControlTheme</c>, not across the file.</b> One
/// resource dictionary commonly holds several, and a file-wide match would let one control's theme
/// satisfy another's lookups — passing precisely when two templates have drifted apart, which is
/// the case worth catching.
/// </para>
/// <para>
/// ⚠ <b>What the rule sees but cannot check is named in <see cref="XamlRuleResult.Skipped"/>.</b>
/// A control with a theme in the scan and no part found in its code contributes nothing to
/// <see cref="XamlRuleResult.Inspected"/>, and neither does one whose parts are known but whose
/// theme is not in the scan, since it may ship from another package. A lower count showed that
/// something fell out; the skip says which control, and why.
/// </para>
/// <para>
/// ⛔ <b>A scan given no assemblies checks nothing, and says so.</b> Parts are read from compiled
/// code, so without <see cref="XamlScanContext.WithAssemblies"/> there is nothing to read. That
/// used to return zero inspected and nothing else, which a consumer who copied a
/// <see cref="XamlScanContext.Load"/> call without it would read as success. Every themed control
/// in the scan is now named as skipped.
/// </para>
/// <para>
/// ⛔ <b>Build output whose dependencies are not beside it is read, never thrown at.</b> A control
/// whose base type lives in an assembly the process cannot load does not load itself, and the rule
/// threw out of <see cref="Analyze"/> at the first method body that named such a type. Now a control
/// the scanned assemblies define but could not load is named in <see cref="XamlRuleResult.Skipped"/>
/// with what stopped it, where it would otherwise pass for a framework control the scan was not
/// given, and a control whose code could be read only in part is checked on that part and named for
/// the rest.
/// </para>
/// </remarks>
public sealed class TemplatePartRule : IXamlRule
{
    /// <summary>The prefix that marks a named template part, by convention across XAML frameworks.</summary>
    public const string PartPrefix = "PART_";

    /// <inheritdoc />
    public string Id => "BNXQ1003";

    /// <inheritdoc />
    public string Summary => "Every template part a control looks up is declared in its own theme.";

    /// <inheritdoc />
    public XamlRuleResult Analyze(XamlScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Assemblies.Count == 0)
        {
            // Nothing to read parts from. Zero findings AND zero inspected, and every themed control
            // is named as skipped: a consumer who forgot WithAssemblies is told so, not reassured.
            // A scan with no themes had nothing to check, and says nothing.
            return XamlRuleResult.Clean(0) with
            {
                Skipped =
                [
                    .. ControlThemes.In(context).Select(theme => new XamlSkip(
                        theme.Target,
                        "The scan was given no assemblies, so this control's parts could not be read and "
                        + "nothing was checked. Call WithAssemblies with the assembly that defines it.",
                        theme.File.RelativePath,
                        theme.Line)),
                ],
            };
        }

        Reading reading = ReadControls(context.Assemblies);

        List<XamlFinding> findings = [];
        List<XamlSkip> skipped = [];
        HashSet<string> themed = new(StringComparer.Ordinal);
        int inspected = 0;

        foreach ((XamlFile file, XElement theme, string target, int? line) in ControlThemes.In(context))
        {
            themed.Add(target);

            if (!reading.Declared.TryGetValue(target, out SortedSet<string>? wanted))
            {
                if (reading.Known.Contains(target))
                {
                    skipped.Add(new XamlSkip(
                        target,
                        reading.PartlyRead.TryGetValue(target, out string? unread)
                            ? "It has a ControlTheme here, and no template part was found in the part of its "
                              + $"code that could be read, but the rest could not be: {unread}. So nothing was "
                              + $"checked. {LoadedTypes.Remedy}"
                            : "It has a ControlTheme here, but no template part was found in its code: no "
                              + "PART_ constant, and no PART_ literal passed to a Find or Get lookup. Either it "
                              + "has no parts, or it names them in a way this rule cannot read: built at "
                              + "runtime, or looked up through a helper named otherwise.",
                        file.RelativePath,
                        line));
                }
                else if (reading.NotLoaded.TryGetValue(target, out (string Source, string Cause) notLoaded))
                {
                    // Not a framework control: the scan was given the assembly that defines it.
                    skipped.Add(new XamlSkip(
                        target,
                        $"It has a ControlTheme here, and {notLoaded.Source}, which the scan was given, defines "
                        + $"it, but it could not be loaded: {notLoaded.Cause}. Its parts were not read and "
                        + $"nothing was checked. {LoadedTypes.Remedy}",
                        file.RelativePath,
                        line));
                }

                // Any other theme is for a type the scanned assemblies do not define, which is not the
                // scan's to explain: it is usually a framework control's.
                continue;
            }

            HashSet<string> present = PartsNamedUnder(theme);

            foreach (string part in wanted)
            {
                inspected++;
                if (present.Contains(part))
                {
                    continue;
                }

                findings.Add(new XamlFinding(
                    Id,
                    file.Path,
                    file.RelativePath,
                    line,
                    $"{target} looks up the template part '{part}', and this ControlTheme does "
                    + $"not declare it. The lookup will return null at runtime and the feature "
                    + $"behind it will be silently absent. Add Name=\"{part}\" to the element "
                    + "that plays that part, or drop the lookup."));
            }

            if (reading.PartlyRead.TryGetValue(target, out string? partly))
            {
                skipped.Add(new XamlSkip(
                    target,
                    $"Part of its code could not be read: {partly}. A part it looks up there was not "
                    + $"checked, though the parts found in the rest were. {LoadedTypes.Remedy}",
                    file.RelativePath,
                    line));
            }
        }

        foreach ((string control, SortedSet<string> parts) in reading.Declared
                     .Where(entry => !themed.Contains(entry.Key))
                     .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            skipped.Add(new XamlSkip(
                control,
                $"It declares {string.Join(", ", parts)}, but no ControlTheme for it is in the scanned "
                + "markup, so those parts were not checked. Its theme may ship from another package; "
                + "if it is yours, scan the folder that holds it."));
        }

        return new XamlRuleResult(findings, inspected) { Skipped = skipped };
    }

    /// <summary>
    /// The part names each type declares in its compiled code, and the name of every type the scan
    /// can see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Two sources, because controls use both.</b> A part name is a <c>PART_</c> string
    /// constant, public or not, or a <c>PART_</c> string literal the type's code passes to a lookup.
    /// The literal is the case constants alone missed: <c>NameScope.Find("PART_Foo")</c> in an
    /// <c>OnApplyTemplate</c> override declares a part as surely as a constant does, and a rule that
    /// read only public constants reported such a control clean while checking none of it. Non-public
    /// types are read too, for two reasons: internal controls exist, and every lambda's closure is a
    /// private nested type, so reading public types alone loses each literal written in a lambda.
    /// </para>
    /// <para>
    /// ⛔ <b>Only a literal passed to a LOOKUP counts</b>: a call whose name starts with <c>Find</c> or
    /// <c>Get</c>, which covers <c>NameScope.Find</c>, <c>FindControl</c>, <c>FindName</c>,
    /// <c>Get</c> and <c>GetTemplateChild</c>. Compiled XAML loads every element name too, passing it
    /// to <c>set_Name</c> and <c>Register</c> to name the element, or to <c>Name</c> to build a
    /// selector. Counting those made every compiled theme look like a control with parts. Measured on
    /// a real codebase before this filter: 34 true lookups, every one followed by <c>Find</c>, beside
    /// 162 literals in compiled XAML, every one followed by one of those three.
    /// </para>
    /// <para>
    /// ⭐ <b>A lookup belongs to the control whose template it is made on, whichever type's code
    /// makes it.</b> A control can hand its template to a helper: in one real codebase two controls
    /// each pass the arguments of their <c>OnApplyTemplate</c> to one controller, which makes every
    /// lookup. Credited to the type whose code makes them, those lookups belonged to a type with no
    /// theme, and reached a consumer only as the prose of a skip. So a lookup is credited to each
    /// control that hands the method making it its template, or the template's name scope: from the
    /// <c>OnApplyTemplate</c> the control resolves to, through any chain of calls in the scanned
    /// assemblies that passes either on. A base class's lookups reach a subclass the same way, since the
    /// base's <c>OnApplyTemplate</c> runs on the subclass's template. A method no control hands its
    /// template to keeps its lookups as its own type's, as a handler on another control's template does.
    /// </para>
    /// <para>
    /// ⚠ <b>Lambdas, local functions and iterators compile into nested types</b>, so their literals
    /// are credited to the outermost type that declares them, the one a <c>ControlTheme</c> targets.
    /// </para>
    /// <para>
    /// ⚠ <b>Three limits.</b> A name built at runtime (<c>"PART_" + name</c>) is invisible, and so is
    /// a lookup through a helper whose name does not start with <c>Find</c> or <c>Get</c>; a control
    /// left with no parts that way is named in <see cref="XamlRuleResult.Skipped"/>. And a lookup no
    /// control hands its template to is taken to be on its own type's template, so one that reaches
    /// into a child control's template is checked against this type's theme, and reported there.
    /// </para>
    /// </remarks>
    private static Reading ReadControls(IReadOnlyList<Assembly> assemblies)
    {
        Reading reading = new(
            new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, (string Source, string Cause)>(StringComparer.Ordinal));

        HashSet<Assembly> scanned = [.. assemblies];
        Dictionary<(Module Module, int Token), (string Owner, List<string> Parts)> lookups = [];
        List<Type> topLevel = [];

        foreach (Assembly assembly in assemblies)
        {
            LoadedTypes types = LoadedTypes.Of(assembly);
            foreach ((string name, string cause) in types.NotLoaded)
            {
                reading.NotLoaded.TryAdd(name, (types.Source, cause));
            }

            foreach (Type type in types.Loaded)
            {
                // A nested type whose enclosing type did not load belongs to a control that did not
                // either, and that control is already among the types that did not load.
                // Compiler-generated at the top level: <Module>, <PrivateImplementationDetails>.
                if (OwnerOf(type) is not { } owner || owner.Name.StartsWith('<'))
                {
                    continue;
                }

                reading.Known.Add(owner.Name);
                if (owner == type)
                {
                    topLevel.Add(type);
                }

                (List<string> constants, List<(MethodBase Method, List<string> Parts)> made) = PartNamesIn(type, out string? unread);
                foreach (string constant in constants)
                {
                    Declare(reading, owner.Name, constant);
                }

                foreach ((MethodBase method, List<string> parts) in made)
                {
                    lookups[KeyOf(method)] = (owner.Name, parts);
                }

                if (unread is not null)
                {
                    reading.PartlyRead.TryAdd(owner.Name, unread);
                }
            }
        }

        // Which controls hand each method their template.
        Dictionary<(Module Module, int Token), SortedSet<string>> handedBy = [];
        foreach (Type control in topLevel)
        {
            foreach ((Module Module, int Token) reached in HandedTemplate(control, scanned))
            {
                if (!handedBy.TryGetValue(reached, out SortedSet<string>? hosts))
                {
                    hosts = new SortedSet<string>(StringComparer.Ordinal);
                    handedBy[reached] = hosts;
                }

                hosts.Add(control.Name);
            }
        }

        foreach (((Module Module, int Token) method, (string owner, List<string> parts)) in lookups)
        {
            IEnumerable<string> creditedTo = handedBy.TryGetValue(method, out SortedSet<string>? hosts) ? hosts : [owner];
            foreach (string host in creditedTo)
            {
                foreach (string part in parts)
                {
                    Declare(reading, host, part);
                }
            }
        }

        return reading;
    }

    /// <summary>
    /// The part names <paramref name="type"/> declares in its own members: its <c>PART_</c>
    /// constants, and the lookups each of its methods makes, with what stopped the rest being read in
    /// <paramref name="unread"/>, or <c>null</c> when nothing did.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Each field and each method is read on its own</b>, so one that needs an assembly the
    /// process cannot load costs only itself. Reading a method's body loads the types of its locals,
    /// and one naming a framework type the scan's process does not have threw out of
    /// <see cref="Analyze"/>: <c>FileNotFoundException</c>, measured on a library's Release output.
    /// </remarks>
    private static (List<string> Constants, List<(MethodBase Method, List<string> Parts)> Lookups) PartNamesIn(Type type, out string? unread)
    {
        const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                        | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        List<string> constants = [];
        List<(MethodBase Method, List<string> Parts)> lookups = [];
        List<Exception> failures = [];

        foreach (FieldInfo field in Read(() => type.GetFields(Everything), failures) ?? [])
        {
            if (Read(() => field.IsLiteral && field.FieldType == typeof(string) ? field.GetRawConstantValue() as string : null, failures)
                    is { } constant
                && IsPartName(constant))
            {
                constants.Add(constant);
            }
        }

        MethodBase[] methods = Read(() => (MethodBase[])[.. type.GetMethods(Everything), .. type.GetConstructors(Everything)], failures) ?? [];
        foreach (MethodBase method in methods)
        {
            List<string> parts = Read(
                () => CompiledStrings.LoadedBy(method, IsPartName)
                    .Where(loaded => IsLookup(loaded.NextCall))
                    .Select(loaded => loaded.Value)
                    .ToList(),
                failures) ?? [];
            if (parts.Count > 0)
            {
                lookups.Add((method, parts));
            }
        }

        unread = failures.Count == 0 ? null : LoadedTypes.CauseOf(failures);
        return (constants, lookups);
    }

    /// <summary>
    /// Every method a control's template reaches: the <c>OnApplyTemplate</c> the control resolves to,
    /// when the scanned assemblies declare it, and each method in them that the template, or the
    /// template's name scope, is handed to, however many calls deep.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Handing the template on is a call to a method with a parameter that can take what
    /// <c>OnApplyTemplate</c> was given</b>, or a value its type declares as a property, such as a
    /// name scope. A parameter of type <see cref="object"/> takes anything, so it is not counted. Read
    /// by name and signature, so no framework type is referenced: a framework whose
    /// <c>OnApplyTemplate</c> takes nothing hands nothing on, and its controls read as they always did.
    /// </remarks>
    private static IReadOnlyCollection<(Module Module, int Token)> HandedTemplate(Type control, HashSet<Assembly> scanned)
    {
        MethodInfo? start;
        Type[] handed;
        try
        {
            start = ResolvedOnApplyTemplate(control);
            if (start is null || !scanned.Contains(start.Module.Assembly))
            {
                return [];
            }

            handed = HandedTypes(start);
        }
        catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
        {
            return [];
        }

        HashSet<(Module Module, int Token)> reached = [KeyOf(start)];
        Queue<MethodBase> pending = new([start]);
        while (pending.TryDequeue(out MethodBase? method))
        {
            foreach (MethodBase callee in CompiledStrings.CalledBy(method))
            {
                bool handedOn;
                try
                {
                    handedOn = scanned.Contains(callee.Module.Assembly) && Takes(callee, handed);
                }
                catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
                {
                    handedOn = false;
                }

                if (handedOn && reached.Add(KeyOf(callee)))
                {
                    pending.Enqueue(callee);
                }
            }
        }

        return reached;
    }

    /// <summary>
    /// The <c>OnApplyTemplate</c> a control's template is applied through: the most-derived one in its
    /// chain that takes one argument, or <c>null</c> when none does.
    /// </summary>
    private static MethodInfo? ResolvedOnApplyTemplate(Type control)
    {
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (Type? link = control; link is not null; link = link.BaseType)
        {
            if (link.GetMethods(Declared).FirstOrDefault(method => method.Name == "OnApplyTemplate" && method.GetParameters().Length == 1)
                is { } declared)
            {
                return declared;
            }
        }

        return null;
    }

    /// <summary>
    /// What <paramref name="onApplyTemplate"/> is given, and the types of the properties that type
    /// declares, such as its name scope; never <see cref="object"/>, <see cref="string"/> or a primitive.
    /// </summary>
    private static Type[] HandedTypes(MethodInfo onApplyTemplate)
    {
        Type argument = onApplyTemplate.GetParameters()[0].ParameterType;
        return
        [
            argument,
            .. argument.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(property => property.PropertyType)
                .Where(type => type != typeof(object) && type != typeof(string) && !type.IsPrimitive && !type.IsEnum),
        ];
    }

    /// <summary>Whether a parameter of <paramref name="callee"/> can take one of <paramref name="handed"/>.</summary>
    private static bool Takes(MethodBase callee, Type[] handed) =>
        callee.GetParameters().Any(parameter =>
            parameter.ParameterType != typeof(object) && handed.Any(type => parameter.ParameterType.IsAssignableFrom(type)));

    /// <summary>Records that <paramref name="control"/> declares <paramref name="part"/>.</summary>
    private static void Declare(Reading reading, string control, string part)
    {
        if (!reading.Declared.TryGetValue(control, out SortedSet<string>? parts))
        {
            parts = new SortedSet<string>(StringComparer.Ordinal);
            reading.Declared[control] = parts;
        }

        parts.Add(part);
    }

    /// <summary>A method's identity across the reads that find it: its module and metadata token.</summary>
    private static (Module Module, int Token) KeyOf(MethodBase method) => (method.Module, method.MetadataToken);

    /// <summary>
    /// The outermost type declaring <paramref name="type"/>, which is the one a <c>ControlTheme</c>
    /// targets, or <c>null</c> when an enclosing type does not load.
    /// </summary>
    private static Type? OwnerOf(Type type)
    {
        try
        {
            Type owner = type;
            while (owner.DeclaringType is { } outer)
            {
                owner = outer;
            }

            return owner;
        }
        catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>A reflective read, or <c>null</c> with the exception kept when what it needs does not load.</summary>
    private static T? Read<T>(Func<T?> read, List<Exception> failures)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
        {
            failures.Add(ex);
            return null;
        }
    }

    /// <summary>A part name: the prefix and something after it.</summary>
    /// <remarks>
    /// ⚠ The bare prefix is not a part. <c>"PART_" + name</c> loads exactly <c>"PART_"</c>, and
    /// reading it as a name would report a part called <c>PART_</c> that no theme could declare.
    /// </remarks>
    private static bool IsPartName(string value) =>
        value.Length > PartPrefix.Length && value.StartsWith(PartPrefix, StringComparison.Ordinal);

    /// <summary>Whether a call looks a name up, as opposed to registering it or building a selector.</summary>
    private static bool IsLookup(string? call) =>
        call is not null
        && (call.StartsWith("Find", StringComparison.Ordinal) || call.StartsWith("Get", StringComparison.Ordinal));

    /// <summary>Part names declared on elements inside this theme, in either naming spelling.</summary>
    private static HashSet<string> PartsNamedUnder(XElement theme)
    {
        HashSet<string> present = new(StringComparer.Ordinal);

        foreach (XElement element in theme.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                // "Name" and x:"Name" both land here as the local name "Name".
                if (string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal)
                    && attribute.Value.StartsWith(PartPrefix, StringComparison.Ordinal))
                {
                    present.Add(attribute.Value);
                }
            }
        }

        return present;
    }

    /// <summary>
    /// What the compiled code says: each control's part names, the name of every type that loaded,
    /// each control whose code could not all be read with why, and each type the assemblies define
    /// that did not load, with the assembly that defines it and why.
    /// </summary>
    private sealed record Reading(
        Dictionary<string, SortedSet<string>> Declared,
        HashSet<string> Known,
        Dictionary<string, string> PartlyRead,
        Dictionary<string, (string Source, string Cause)> NotLoaded);
}
