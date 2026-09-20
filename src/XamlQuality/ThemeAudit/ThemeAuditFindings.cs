namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// A key a consumer references that a theme variant does not define — the invisible-control case.
/// <see cref="WorstKind"/> is <see cref="ReferenceKind.Static"/> when any reference to the key is
/// static (it throws at load) and <see cref="ReferenceKind.Dynamic"/> otherwise (it resolves to
/// nothing and paints an invisible control).
/// </summary>
public sealed record UndefinedKeyFinding(string Variant, string DisplayName, string Key, ReferenceKind WorstKind);

/// <summary>Compares what a consumer references against what a theme defines, per variant.</summary>
public static class ThemeAuditFindings
{
    /// <summary>
    /// The keys <paramref name="references"/> use that no variant of <paramref name="inventory"/>
    /// defines and that <paramref name="alsoDefined"/> (the consumer's own resources, and any base
    /// keys the host supplies) does not cover. One finding per (variant, key), ordered by variant
    /// then key.
    /// </summary>
    public static IReadOnlyList<UndefinedKeyFinding> UndefinedKeys(
        IReadOnlyList<ResourceReference> references,
        ThemeInventory inventory,
        IReadOnlySet<string>? alsoDefined = null)
    {
        return Find(references, inventory.Variants.Select(v => (v.DisplayName, v)), alsoDefined)
            .OrderBy(f => f.DisplayName, StringComparer.Ordinal)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The same comparison for the named <paramref name="variants"/> only — each resolved the way
    /// an application requesting it would be (<see cref="ThemeInventory.ForVariant"/>) and reported
    /// under the requested name, so Fluent's <c>Light</c> is audited as the <c>Default</c>
    /// dictionary it resolves to. Ordered by the variants' given order, then key.
    /// </summary>
    public static IReadOnlyList<UndefinedKeyFinding> UndefinedKeys(
        IReadOnlyList<ResourceReference> references,
        ThemeInventory inventory,
        IReadOnlyList<string> variants,
        IReadOnlySet<string>? alsoDefined = null)
    {
        return Find(references, variants.Select(name => (name, inventory.ForVariant(name))), alsoDefined);
    }

    private static List<UndefinedKeyFinding> Find(
        IReadOnlyList<ResourceReference> references,
        IEnumerable<(string Name, VariantInventory Variant)> variants,
        IReadOnlySet<string>? alsoDefined)
    {
        // Worst reference kind per key: Static (throws) outranks Dynamic (invisible).
        Dictionary<string, ReferenceKind> worstKind = new(StringComparer.Ordinal);
        foreach (ResourceReference reference in references)
        {
            if (!worstKind.ContainsKey(reference.Key))
            {
                worstKind[reference.Key] = reference.Kind;
            }
            else if (reference.Kind == ReferenceKind.Static)
            {
                worstKind[reference.Key] = ReferenceKind.Static;
            }
        }

        List<UndefinedKeyFinding> findings = [];
        foreach ((string name, VariantInventory variant) in variants)
        {
            foreach ((string key, ReferenceKind kind) in worstKind.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!variant.Defines(key) && (alsoDefined is null || !alsoDefined.Contains(key)))
                {
                    findings.Add(new UndefinedKeyFinding(variant.Key, name, key, kind));
                }
            }
        }

        return findings;
    }
}
