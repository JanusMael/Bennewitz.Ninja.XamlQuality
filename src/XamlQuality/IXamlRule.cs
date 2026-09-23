namespace Bennewitz.Ninja.XamlQuality;

/// <summary>One XAML quality rule.</summary>
/// <remarks>
/// <para>
/// ⚠ <b>A rule reports; it does not assert.</b> Returning findings rather than throwing keeps the
/// library free of any test framework, and lets the consumer choose severity: the same finding can
/// fail a build, warn in CI, or print in a report.
/// </para>
/// <para>
/// ⛔ <b>A rule that can never fire is worse than no rule.</b> It reports zero, reads as coverage,
/// and is indistinguishable from a clean codebase — which is why <see cref="XamlRuleResult"/>
/// carries what was inspected as well as what was found.
/// </para>
/// </remarks>
public interface IXamlRule
{
    /// <summary>
    /// Stable identifier, used in findings and in any suppression a consumer builds.
    /// ⚠ Treat it as public API: changing it silently un-suppresses whatever referenced it.
    /// </summary>
    string Id { get; }

    /// <summary>One line: what the rule REQUIRES, phrased as the requirement rather than the violation.</summary>
    string Summary { get; }

    /// <summary>Examine the scanned markup and report every violation found.</summary>
    XamlRuleResult Analyze(XamlScanContext context);
}

/// <summary>What a rule found, and how much it looked at.</summary>
/// <param name="Findings">Every violation, in file order.</param>
/// <param name="Inspected">
/// How many candidate elements or files the rule actually examined.
/// <para>
/// ⭐ <b>The number that tells "nothing is wrong" apart from "nothing was checked".</b> A rule
/// whose selector stops matching — a renamed element, a changed namespace, a folder that moved —
/// returns zero findings and looks like success. A consumer asserting only on
/// <see cref="Findings"/> is trusting a number that cannot rise, so it can never fail.
/// </para>
/// </param>
public sealed record XamlRuleResult(IReadOnlyList<XamlFinding> Findings, int Inspected)
{
    /// <summary>A result for a rule that examined things and found nothing wrong.</summary>
    public static XamlRuleResult Clean(int inspected) => new([], inspected);

    /// <summary>What the rule saw but could not check, and why. Empty when it checked everything it saw.</summary>
    /// <remarks>
    /// ⚠ <b><see cref="Inspected"/> says how much was checked; this says what was not.</b> A
    /// consumer asserting on both can tell a clean result from one that quietly skipped the control
    /// that mattered. A skip is not a violation; see <see cref="XamlSkip"/>.
    /// </remarks>
    public IReadOnlyList<XamlSkip> Skipped { get; init; } = [];
}
