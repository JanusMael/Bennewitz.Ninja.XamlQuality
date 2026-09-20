namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// A foreground/background pair a consumer promises to keep above a WCAG contrast floor. Either
/// key may be one of the consumer's own tokens or a theme key, and either may be a surface
/// placeholder (<c>$Page</c>) that a <see cref="SurfaceMap"/> resolves per variant to that
/// variant's own surface key — so a marker drawn on the host's page is scored against the page
/// colour NightSky actually paints, not an assumed near-black.
/// </summary>
public sealed record ContrastPair(string Foreground, string Background, double Floor)
{
    /// <summary>
    /// The key the background is composited over first when it is translucent — a diff-row tint
    /// sits on the pane background — so the pair is scored as the reader sees it. A placeholder
    /// is allowed. A translucent background with no <see cref="Over"/> cannot be scored.
    /// </summary>
    public string? Over { get; init; }

    /// <summary>What the pair is, for the report: "row text on inserted rows".</summary>
    public string? Purpose { get; init; }
}

/// <summary>How one pair fared under one variant.</summary>
public enum ContrastStatus
{
    /// <summary>The ratio meets the floor.</summary>
    Pass,

    /// <summary>The ratio is below the floor — the low-contrast finding.</summary>
    Fail,

    /// <summary>A key did not resolve to a colour, so nothing could be measured; <see cref="ContrastFinding.Reason"/> says which.</summary>
    Unscored,
}

/// <summary>
/// One pair scored under one variant: the keys after placeholder substitution, the colours as
/// scored (the background already composited over its <see cref="ContrastPair.Over"/>), the
/// ratio, and the verdict.
/// </summary>
public sealed record ContrastFinding(
    string DisplayName,
    ContrastPair Pair,
    string ForegroundKey,
    string BackgroundKey,
    AuditColor? Foreground,
    AuditColor? Background,
    double? Ratio,
    ContrastStatus Status,
    string? Reason);

/// <summary>
/// The surface keys a theme names, per variant: placeholder name → theme key. Semi's Light and
/// Dark paint the page with <c>SemiColorBackground0</c>; its high-contrast variants paint it
/// with <c>SemiColorWindow</c>, so a placeholder resolves through the variant's own entry first
/// and the shared one second.
/// </summary>
public sealed class SurfaceMap
{
    private readonly IReadOnlyDictionary<string, string> _shared;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _perVariant;

    /// <param name="shared">Placeholder → key for every variant not overridden.</param>
    /// <param name="perVariant">Variant display name → (placeholder → key) overrides.</param>
    public SurfaceMap(IReadOnlyDictionary<string, string> shared,
                      IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? perVariant = null)
    {
        _shared = shared;
        _perVariant = perVariant ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
    }

    /// <summary>The theme key behind <paramref name="placeholder"/> under <paramref name="variant"/>, or <c>null</c> when unnamed.</summary>
    public string? Resolve(string variant, string placeholder)
    {
        if (_perVariant.TryGetValue(variant, out IReadOnlyDictionary<string, string>? overrides)
            && overrides.TryGetValue(placeholder, out string? overridden))
        {
            return overridden;
        }

        return _shared.TryGetValue(placeholder, out string? key) ? key : null;
    }
}

/// <summary>
/// Scores contrast pairs against a theme's variants. A key resolves through the consumer's own
/// token dictionary first (its Light/Dark entries, or a per-variant override, following the same
/// fallback the theme uses) and the theme second, so a pair can mix a consumer token with a host
/// surface. Every pair yields a finding per variant — pass, fail, or unscored with the reason —
/// so a silently skipped pair cannot masquerade as a passing one.
/// </summary>
public static class ContrastAudit
{
    /// <summary>The character that marks a surface placeholder in a pair (<c>$Page</c>).</summary>
    public const char PlaceholderPrefix = '$';

    /// <summary>
    /// Scores every pair under every named variant, in the order given.
    /// </summary>
    /// <param name="pairs">The consumer's pairs.</param>
    /// <param name="theme">The host theme.</param>
    /// <param name="variants">The variant display names to score — the report's targets for this theme.</param>
    /// <param name="tokens">The consumer's own dictionary, built with the theme's inheritance so its Light/Dark entries serve the theme's inheritors.</param>
    /// <param name="surfaces">The theme's surface placeholders.</param>
    public static IReadOnlyList<ContrastFinding> Score(
        IReadOnlyList<ContrastPair> pairs,
        ThemeInventory theme,
        IReadOnlyList<string> variants,
        ThemeInventory? tokens = null,
        SurfaceMap? surfaces = null)
    {
        List<ContrastFinding> findings = [];
        foreach (string variant in variants)
        {
            foreach (ContrastPair pair in pairs)
            {
                findings.Add(ScoreOne(pair, variant, theme, tokens, surfaces));
            }
        }

        return findings;
    }

    private static ContrastFinding ScoreOne(ContrastPair pair, string variant, ThemeInventory theme, ThemeInventory? tokens, SurfaceMap? surfaces)
    {
        Resolved foreground = Resolve(pair.Foreground, variant, theme, tokens, surfaces);
        Resolved background = Resolve(pair.Background, variant, theme, tokens, surfaces);

        if (foreground.Reason is not null || background.Reason is not null)
        {
            return Unscored(pair, variant, foreground, background, foreground.Reason ?? background.Reason!);
        }

        AuditColor fg = foreground.Color!.Value;
        AuditColor bg = background.Color!.Value;

        if (bg.A != 255)
        {
            if (pair.Over is null)
            {
                return Unscored(pair, variant, foreground, background,
                    $"background {background.Key} is translucent ({bg}) and the pair names nothing to composite it over");
            }

            Resolved over = Resolve(pair.Over, variant, theme, tokens, surfaces);
            if (over.Reason is not null)
            {
                return Unscored(pair, variant, foreground, background, over.Reason);
            }

            if (over.Color!.Value.A != 255)
            {
                return Unscored(pair, variant, foreground, background,
                    $"'over' key {over.Key} is itself translucent ({over.Color})");
            }

            bg = bg.CompositeOver(over.Color.Value);
        }

        double ratio = Contrast.Ratio(fg, bg);
        ContrastStatus status = ratio + 1e-9 >= pair.Floor ? ContrastStatus.Pass : ContrastStatus.Fail;
        return new ContrastFinding(variant, pair, foreground.Key, background.Key, fg, bg, ratio, status, null);
    }

    private static ContrastFinding Unscored(ContrastPair pair, string variant, Resolved foreground, Resolved background, string reason)
    {
        return new ContrastFinding(variant, pair, foreground.Key, background.Key, foreground.Color, background.Color, null, ContrastStatus.Unscored, reason);
    }

    private sealed record Resolved(string Key, AuditColor? Color, string? Reason);

    private static Resolved Resolve(string name, string variant, ThemeInventory theme, ThemeInventory? tokens, SurfaceMap? surfaces)
    {
        string key = name;
        if (name.Length > 1 && name[0] == PlaceholderPrefix)
        {
            string placeholder = name[1..];
            string? surface = surfaces?.Resolve(variant, placeholder);
            if (surface is null)
            {
                return new Resolved(name, null, $"the theme names no {name} surface for {variant}");
            }

            key = surface;
        }

        AuditColor? color = tokens?.ForVariant(variant).Resolve(key) ?? theme.ForVariant(variant).Resolve(key);
        return color is null
            ? new Resolved(key, null, $"{key} is undefined or not a colour under {variant}")
            : new Resolved(key, color, null);
    }
}
