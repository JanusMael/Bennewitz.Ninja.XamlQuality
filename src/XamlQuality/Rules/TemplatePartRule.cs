using System.Reflection;
using System.Xml;
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
/// </remarks>
public sealed class TemplatePartRule : IXamlRule
{
    /// <summary>The prefix that marks a named template part, by convention across XAML frameworks.</summary>
    public const string PartPrefix = "PART_";

    /// <inheritdoc />
    public string Id => "XQ1003";

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
                    .. ThemesIn(context).Select(theme => new XamlSkip(
                        theme.Target,
                        "The scan was given no assemblies, so this control's parts could not be read and "
                        + "nothing was checked. Call WithAssemblies with the assembly that defines it.",
                        theme.File.RelativePath,
                        theme.Line)),
                ],
            };
        }

        (Dictionary<string, SortedSet<string>> declared, HashSet<string> known) = ReadControls(context.Assemblies);

        List<XamlFinding> findings = [];
        List<XamlSkip> skipped = [];
        HashSet<string> themed = new(StringComparer.Ordinal);
        int inspected = 0;

        foreach ((XamlFile file, XElement theme, string target, int? line) in ThemesIn(context))
        {
            themed.Add(target);

            if (!declared.TryGetValue(target, out SortedSet<string>? wanted))
            {
                // A theme for a type the scanned assemblies do not hold is not the scan's to
                // explain: it is usually a framework control's.
                if (known.Contains(target))
                {
                    skipped.Add(new XamlSkip(
                        target,
                        "It has a ControlTheme here, but no template part was found in its code: no "
                        + "PART_ constant, and no PART_ literal passed to a Find or Get lookup. Either it "
                        + "has no parts, or it names them in a way this rule cannot read: built at "
                        + "runtime, or looked up through a helper named otherwise.",
                        file.RelativePath,
                        line));
                }

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
        }

        foreach ((string control, SortedSet<string> parts) in declared
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
    /// ⚠ <b>Lambdas, local functions and iterators compile into nested types</b>, so their literals
    /// are credited to the outermost type that declares them, the one a <c>ControlTheme</c> targets.
    /// </para>
    /// <para>
    /// ⚠ <b>Three limits.</b> A name built at runtime (<c>"PART_" + name</c>) is invisible, and so is
    /// a lookup through a helper whose name does not start with <c>Find</c> or <c>Get</c>; a control
    /// left with no parts that way is named in <see cref="XamlRuleResult.Skipped"/>. And every lookup
    /// is taken to be on the type's OWN template, so one that reaches into a child control's template
    /// is checked against this type's theme, and reported there.
    /// </para>
    /// </remarks>
    private static (Dictionary<string, SortedSet<string>> Declared, HashSet<string> Known) ReadControls(
        IReadOnlyList<Assembly> assemblies)
    {
        const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                        | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        Dictionary<string, SortedSet<string>> declared = new(StringComparer.Ordinal);
        HashSet<string> known = new(StringComparer.Ordinal);

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in TypesOf(assembly))
            {
                Type owner = type;
                while (owner.DeclaringType is { } outer)
                {
                    owner = outer;
                }

                // Compiler-generated at the top level: <Module>, <PrivateImplementationDetails>.
                if (owner.Name.StartsWith('<'))
                {
                    continue;
                }

                known.Add(owner.Name);

                IEnumerable<string?> constants = type.GetFields(Everything)
                    .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                    .Select(field => field.GetRawConstantValue() as string);

                IEnumerable<string> literals = type.GetMethods(Everything)
                    .Cast<MethodBase>()
                    .Concat(type.GetConstructors(Everything))
                    .SelectMany(method => CompiledStrings.LoadedBy(method, IsPartName))
                    .Where(loaded => IsLookup(loaded.NextCall))
                    .Select(loaded => loaded.Value);

                foreach (string name in constants.Concat(literals).OfType<string>().Where(IsPartName))
                {
                    if (!declared.TryGetValue(owner.Name, out SortedSet<string>? parts))
                    {
                        parts = new SortedSet<string>(StringComparer.Ordinal);
                        declared[owner.Name] = parts;
                    }

                    parts.Add(name);
                }
            }
        }

        return (declared, known);
    }

    /// <summary>Every <c>ControlTheme</c> in the scan that names a target type, with where it is.</summary>
    private static IEnumerable<(XamlFile File, XElement Theme, string Target, int? Line)> ThemesIn(
        XamlScanContext context)
    {
        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement theme in file.Document!.Descendants()
                         .Where(e => string.Equals(e.Name.LocalName, "ControlTheme", StringComparison.Ordinal)))
            {
                if (TargetTypeOf(theme) is not { } target)
                {
                    continue;
                }

                int? line = (theme as IXmlLineInfo).HasLineInfo()
                    ? ((IXmlLineInfo)theme).LineNumber
                    : null;

                yield return (file, theme, target, line);
            }
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

    /// <summary>Every type in <paramref name="assembly"/> that loads, public or not.</summary>
    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A type whose dependencies do not load is skipped rather than failing the whole scan.
            return ex.Types.OfType<Type>();
        }
    }

    /// <summary>
    /// The control a <c>ControlTheme</c> targets, as a bare type name.
    /// </summary>
    /// <remarks>
    /// ⚠ Both spellings occur and each is idiomatic: <c>TargetType="local:Thing"</c> and
    /// <c>TargetType="{x:Type local:Thing}"</c>. Handling one silently halves the rule's reach.
    /// </remarks>
    private static string? TargetTypeOf(XElement theme)
    {
        string? raw = theme.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, "TargetType", StringComparison.Ordinal))
            ?.Value;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (raw.StartsWith('{'))
        {
            // "{x:Type local:Thing}" — take the last whitespace-separated token, minus the brace.
            raw = raw.TrimEnd('}').Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? raw;
        }

        int colon = raw.LastIndexOf(':');
        string name = colon >= 0 ? raw[(colon + 1)..] : raw;
        return name.Length == 0 ? null : name;
    }

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
}
