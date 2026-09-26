using System.Xml.Linq;
using static Bennewitz.Ninja.XamlQuality.GridLayout;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// No control declares a size larger than the fixed <c>Grid</c> slot it sits in.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The defect is in the automation tree, not on screen.</b> A child whose own
/// <c>MinHeight</c> exceeds its row's fixed height is arranged at the size it asked for, and keeps
/// that full size in the automation tree — so a UI harness or a screen reader sees a pane the user
/// cannot. Whether it is also DRAWN depends on the container and is deliberately not what this rule
/// asks about: <c>ContentControl</c> defaults <c>ClipToBounds="True"</c> and <c>Border</c> does not,
/// which is unknowable from markup once a consumer's own control is involved.
/// </para>
/// <para>
/// ⛔ <b>Only FIXED slot sizes are decidable.</b> A <c>*</c> row resolves against its siblings and
/// the available space, and <c>Auto</c> against content — neither is knowable from markup, so a rule
/// that guessed there would fire on correct layouts. A rule with false positives on correct markup
/// is worse than no rule, because people stop reading its output.
/// </para>
/// <para>
/// ⭐ <b>Inspected counts what was measured, and Skipped names what could not be.</b> A control
/// counts in <see cref="XamlRuleResult.Inspected"/> once its declared size is measured against
/// fixed slots, in either direction. One in an <c>Auto</c> or <c>*</c> slot, or declaring no size,
/// has nothing to measure and counts as neither, as a child of a grid with no definitions does. One
/// whose placement or size rests on a value markup cannot evaluate, such as a binding, a resource
/// or text that is not a size, is named in <see cref="XamlRuleResult.Skipped"/> with that value,
/// unless no fixed slot could be involved whatever the value turns out to be. Counted as inspected,
/// it would read as a clean result for a check that never ran.
/// </para>
/// <para>
/// ⚠ <b>A control is measured where the framework places it.</b> <c>Grid</c> clamps an index past
/// its last definition to the last one, and a span to the definitions that remain, and a span's room
/// is every slot it crosses plus the spacing between them. So an out-of-range <c>Grid.Column</c> is
/// measured against the last column rather than skipped, and a control that fits the columns it spans
/// is not measured against the first of them alone. A span that crosses an <c>Auto</c> or <c>*</c>
/// slot is not measured, like an <c>Auto</c> slot itself.
/// </para>
/// <para>
/// ⚠ <b>Two spellings, and checking one is how a rule quietly passes.</b> Definitions are written
/// either as the attribute shorthand (<c>RowDefinitions="Auto,0,*"</c>) or as property ELEMENTS
/// (<c>&lt;Grid.RowDefinitions&gt;&lt;RowDefinition Height="0"/&gt;…</c>), and both are read, through
/// <see cref="GridLayout"/>, which <c>BNXQ1008</c> shares.
/// </para>
/// <para>
/// ⓘ The fix is <c>IsVisible</c>, never <c>ClipToBounds</c>: it removes the element from layout AND
/// from the automation tree together, which clipping does not. Full entry, with the measured
/// <c>ClipToBounds</c> defaults and the centring behaviour a scan cannot see, in
/// <c>docs/avalonia-gotchas.md</c>.
/// </para>
/// </remarks>
public sealed class GridSlotOverflowRule : IXamlRule
{
    /// <inheritdoc />
    public string Id => "BNXQ1004";

    /// <inheritdoc />
    public string Summary => "Every control fits the fixed Grid slot it is placed in.";

    /// <inheritdoc />
    public XamlRuleResult Analyze(XamlScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<XamlFinding> findings = [];
        List<XamlSkip> skipped = [];
        int inspected = 0;

        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement grid in file.Document!.Descendants())
            {
                // Local name only: the markup namespace differs between Avalonia, WPF and MAUI, and
                // matching a fully-qualified name would make the rule silently inert on two of three.
                if (!string.Equals(grid.Name.LocalName, "Grid", StringComparison.Ordinal)) { continue; }

                Axis rows = Axis.Of(grid, Rows);
                Axis columns = Axis.Of(grid, Columns);
                if (rows.Definitions is null && columns.Definitions is null) { continue; }

                // Grid.Row and Grid.Column bind to DIRECT children only; an element nested inside
                // another panel is positioned by that panel, not by this Grid.
                foreach (XElement child in grid.Elements())
                {
                    // <Grid.RowDefinitions> and friends are property elements, not children.
                    if (child.Name.LocalName.Contains('.', StringComparison.Ordinal)) { continue; }

                    Verdict row = Measure(child, rows);
                    Verdict column = Measure(child, columns);

                    // One control counts once, whichever of its directions were measured.
                    if (row.IsMeasured || column.IsMeasured) { inspected++; }

                    foreach (Verdict verdict in (Verdict[])[row, column])
                    {
                        if (verdict.Outcome == Outcome.Overflows)
                        {
                            findings.Add(new XamlFinding(Id, file.Path, file.RelativePath, LineOf(child), verdict.Text));
                        }
                        else if (verdict.Outcome == Outcome.Undecided)
                        {
                            skipped.Add(new XamlSkip(child.Name.LocalName, verdict.Text, file.RelativePath, LineOf(child)));
                        }
                    }
                }
            }
        }

        return new XamlRuleResult(findings, inspected) { Skipped = skipped };
    }

    /// <summary>
    /// Whether <paramref name="child"/> fits the slots it sits in along one direction of its grid.
    /// </summary>
    /// <remarks>
    /// ⚠ What rules the control out is checked before what is unknown about it: a value markup
    /// cannot evaluate is named only when a fixed slot could depend on it. A bound index among
    /// nothing but <c>Auto</c> and <c>*</c> slots lands in one of them whatever it turns out to be.
    /// </remarks>
    private static Verdict Measure(XElement child, Axis axis)
    {
        Dimension names = axis.Names;
        if (axis.Definitions is not { } definitions) { return Verdict.NotASubject; }   // One implicit * slot.

        (Length declared, string[] unevaluated) = DeclaredSize(child, names);
        if (declared.Kind == Kind.Flexible) { return Verdict.NotASubject; }             // Asks for no size.

        List<string> unknown = [];
        if (definitions.Unevaluated is { } whole) { unknown.Add(whole.Shown); }

        Placement index = Attached(child, names.Index, fallback: 0, minimum: 0);
        Placement span = Attached(child, names.Span, fallback: 1, minimum: 1);
        if (index.Value is null) { unknown.Add(index.Shown); }
        if (span.Value is null) { unknown.Add(span.Shown); }

        double available = 0;
        int crossed = 1;

        if (definitions.Unevaluated is null)
        {
            IReadOnlyList<Length> slots = definitions.Slots;

            if (index.Value is int at)
            {
                // Where the framework places it: past the last definition means IN the last one, and
                // a span running off the edge covers only the definitions that remain.
                int first = Math.Min(at, slots.Count - 1);
                if (slots[first].Kind == Kind.Flexible) { return Verdict.NotASubject; }   // Every span crosses it.

                if (span.Value is int across)
                {
                    crossed = Math.Min(across, slots.Count - first);
                    Length[] spanned = [.. slots.Skip(first).Take(crossed)];
                    if (spanned.Any(slot => slot.Kind == Kind.Flexible)) { return Verdict.NotASubject; }

                    unknown.AddRange(spanned.Where(slot => slot.Kind == Kind.Unknown).Select(slot => slot.Shown));
                    available = spanned.Sum(slot => slot.Value);

                    if (crossed > 1)
                    {
                        if (axis.Spacing.Kind == Kind.Unknown) { unknown.Add(axis.Spacing.Shown); }
                        available += axis.Spacing.Value * (crossed - 1);
                    }
                }
            }
            else if (slots.All(slot => slot.Kind == Kind.Flexible))
            {
                return Verdict.NotASubject;   // Wherever it lands, no slot is fixed.
            }
        }

        if (declared.Kind == Kind.Unknown) { unknown.AddRange(unevaluated); }

        if (unknown.Count > 0)
        {
            return new Verdict(
                Outcome.Undecided,
                $"Whether its {names.SizeWord} fits the Grid {names.SlotWord}s it is placed in was not checked, "
                + $"because markup cannot evaluate {Join(unknown)}.");
        }

        if (declared.Value <= available) { return Verdict.Fits; }

        string room = crossed == 1
            ? $"a {names.SlotWord} fixed at {Format(available)}"
            : $"{crossed} {names.SlotWord}s fixed at {Format(available)} together";

        return new Verdict(
            Outcome.Overflows,
            $"This {child.Name.LocalName} asks to be {Format(declared.Value)} {names.DimensionWord} in "
            + $"{room}. It is arranged at the size it asked for and "
            + "keeps that size in the automation tree, so a screen reader and a UI test see it even "
            + "when nothing is drawn. Bind IsVisible alongside whatever sizes the "
            + $"{names.SlotWord} — that removes it from layout and from the tree together, which "
            + "ClipToBounds does not.");
    }

    private enum Outcome
    {
        NotASubject,
        Fits,
        Overflows,
        Undecided,
    }

    private readonly record struct Verdict(Outcome Outcome, string Text)
    {
        public static Verdict NotASubject => new(Outcome.NotASubject, string.Empty);

        public static Verdict Fits => new(Outcome.Fits, string.Empty);

        public bool IsMeasured => Outcome is Outcome.Fits or Outcome.Overflows;
    }
}
