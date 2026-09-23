namespace Bennewitz.Ninja.XamlQuality;

/// <summary>Something a rule saw but could not check, and why.</summary>
/// <remarks>
/// <para>
/// ⭐ <b>The number that names names.</b> <see cref="XamlRuleResult.Inspected"/> counts what a rule
/// checked, so it can show that coverage fell but never which control fell out of it. A control the
/// rule cannot read contributes nothing to that count and looks exactly like one with nothing to
/// check. A skip says which, and why.
/// </para>
/// <para>
/// ⚠ <b>Not a violation.</b> Some skips are correct, such as a control that genuinely has no
/// template parts. The consumer decides whether any should fail a build, which is why skips are
/// reported beside the findings rather than among them.
/// </para>
/// </remarks>
/// <param name="Subject">What was skipped, as a reader would name it, such as a control's type name.</param>
/// <param name="Reason">Why it could not be checked, and what would make it checkable.</param>
/// <param name="RelativePath">The markup file the skip was noticed in, or <c>null</c> when it has none.</param>
/// <param name="Line">1-based line in that file, or <c>null</c>.</param>
public sealed record XamlSkip(string Subject, string Reason, string? RelativePath = null, int? Line = null)
{
    /// <summary>A single line suitable for a report.</summary>
    public override string ToString() =>
        RelativePath is null ? $"{Subject}: {Reason}"
        : Line is { } line ? $"{RelativePath}({line}): {Subject}: {Reason}"
        : $"{RelativePath}: {Subject}: {Reason}";
}
