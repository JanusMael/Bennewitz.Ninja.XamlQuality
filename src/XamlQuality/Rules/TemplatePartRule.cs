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
/// A control whose theme is not in the scanned markup is skipped rather than reported: its theme
/// may ship from another package. That shows up as a lower <see cref="XamlRuleResult.Inspected"/>,
/// which is the honest signal.
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

        Dictionary<string, SortedSet<string>> declared = PartsDeclaredInCode(context.Assemblies);
        if (declared.Count == 0)
        {
            // Nothing to check against. Zero findings AND zero inspected, so a consumer who forgot
            // WithAssemblies sees the difference between "agrees" and "never asked".
            return XamlRuleResult.Clean(0);
        }

        List<XamlFinding> findings = [];
        int inspected = 0;

        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement theme in file.Document!.Descendants()
                         .Where(e => string.Equals(e.Name.LocalName, "ControlTheme", StringComparison.Ordinal)))
            {
                if (TargetTypeOf(theme) is not { } target || !declared.TryGetValue(target, out SortedSet<string>? wanted))
                {
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

                    int? line = (theme as IXmlLineInfo).HasLineInfo()
                        ? ((IXmlLineInfo)theme).LineNumber
                        : null;

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
        }

        return new XamlRuleResult(findings, inspected);
    }

    /// <summary>
    /// Part names each type declares, read from its public string constants.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Constants, not string literals in method bodies.</b> Reflection cannot see a literal
    /// passed to a lookup call, so a control that inlines <c>"PART_Foo"</c> is invisible here and
    /// this rule reports nothing for it. Declaring parts as constants is the convention that makes
    /// the contract checkable at all — and it is the convention every framework's own controls
    /// follow.
    /// </remarks>
    private static Dictionary<string, SortedSet<string>> PartsDeclaredInCode(IReadOnlyList<Assembly> assemblies)
    {
        Dictionary<string, SortedSet<string>> declared = new(StringComparer.Ordinal);

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in assembly.GetExportedTypes())
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsLiteral || field.FieldType != typeof(string))
                    {
                        continue;
                    }

                    if (field.GetRawConstantValue() is not string value
                        || !value.StartsWith(PartPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!declared.TryGetValue(type.Name, out SortedSet<string>? parts))
                    {
                        parts = new SortedSet<string>(StringComparer.Ordinal);
                        declared[type.Name] = parts;
                    }

                    parts.Add(value);
                }
            }
        }

        return declared;
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
