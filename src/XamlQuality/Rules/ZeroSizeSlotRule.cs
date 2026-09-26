using System.Xml.Linq;
using static Bennewitz.Ninja.XamlQuality.GridLayout;

namespace Bennewitz.Ninja.XamlQuality.Rules;

/// <summary>
/// Every control in a zero-size <c>Grid</c> slot is hidden by <c>IsVisible</c>, not by the slot alone.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>A slot of no size hides a control from people, not from the automation tree.</b> Measured on
/// Avalonia 12.1.3, a <c>Button</c> in a row of <c>Height="0"</c> is arranged 0 tall, stays effectively
/// visible, and its peer stays in the tree, reporting an empty rectangle and <c>IsOffscreen</c> false.
/// A <c>TextBox</c> in the same row is arranged at the 32 its Fluent theme sets as a minimum, over its
/// neighbours. Either way a test or an agent finds a control nobody can see or use. With
/// <c>IsVisible="False"</c> the control leaves the tree. <c>docs/ai-drivable-ui.md</c>'s rule 6 asks
/// for exactly that: hide what is not on screen, do not merely shrink it.
/// </para>
/// <para>
/// ⚠ <b>A slot's size is what the framework arranges, bounds included.</b> In the same measurement a
/// row's <c>MinHeight</c> wins over its <c>Height</c> and <c>MaxHeight</c>, a <c>MaxHeight</c> of 0
/// empties any row, <c>Auto</c> and <c>*</c> included, a star of weight 0 gets nothing, and a span has
/// the spacing between the slots it crosses. A control is placed where the framework places it, an
/// index past the last definition in the last one, through <see cref="GridLayout"/>, which
/// <see cref="GridSlotOverflowRule"/> shares.
/// </para>
/// <para>
/// ⚠ <b>A control that asks for a size is <see cref="GridSlotOverflowRule"/>'s.</b> One with its own
/// <c>Height</c> or <c>MinHeight</c> in an empty row overflows at that size, which that rule reports.
/// This one covers a control that asks for no size in that direction, so the two never report one
/// control twice.
/// </para>
/// <para>
/// ⚠ <b>Any <c>IsVisible</c> but a literal <c>True</c> answers it</b>, in either spelling, and so does
/// WPF's <c>Visibility</c> other than <c>Visible</c>: a <c>False</c> hides the control, and a binding is
/// how a collapsing pane hides it. A slot whose size markup cannot evaluate, such as a bound column
/// width, is named in <see cref="XamlRuleResult.Skipped"/>, unless the control is hidden anyway or has
/// room whatever the value turns out to be.
/// </para>
/// </remarks>
public sealed class ZeroSizeSlotRule : IXamlRule
{
    /// <inheritdoc />
    public string Id => "BNXQ1008";

    /// <inheritdoc />
    public string Summary => "Every control in a zero-size Grid slot is hidden by IsVisible, not by the slot alone.";

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
                if (!string.Equals(grid.Name.LocalName, "Grid", StringComparison.Ordinal)) { continue; }

                Axis rows = Axis.Of(grid, Rows);
                Axis columns = Axis.Of(grid, Columns);
                if (rows.Definitions is null && columns.Definitions is null) { continue; }

                // Grid.Row and Grid.Column bind to DIRECT children only.
                foreach (XElement child in grid.Elements())
                {
                    // <Grid.RowDefinitions> and friends are property elements, not children.
                    if (child.Name.LocalName.Contains('.', StringComparison.Ordinal)) { continue; }

                    Extent row = ExtentOf(child, rows);
                    Extent column = ExtentOf(child, columns);

                    // One control counts once, whichever of its directions were decided.
                    if (row.IsDecided || column.IsDecided) { inspected++; }

                    if (IsHidden(child)) { continue; }

                    if (row.Outcome == Outcome.Empty || column.Outcome == Outcome.Empty)
                    {
                        Extent empty = row.Outcome == Outcome.Empty ? row : column;
                        findings.Add(new XamlFinding(Id, file.Path, file.RelativePath, LineOf(child), empty.Text));
                        continue;
                    }

                    foreach (Extent extent in (Extent[])[row, column])
                    {
                        if (extent.Outcome == Outcome.Undecided)
                        {
                            skipped.Add(new XamlSkip(child.Name.LocalName, extent.Text, file.RelativePath, LineOf(child)));
                        }
                    }
                }
            }
        }

        return new XamlRuleResult(findings, inspected) { Skipped = skipped };
    }

    /// <summary>Whether the slots <paramref name="child"/> is placed in along one direction have any size.</summary>
    /// <remarks>
    /// ⚠ What gives the control room is checked before what is unknown about it: a value markup cannot
    /// evaluate is named only when the answer could depend on it.
    /// </remarks>
    private static Extent ExtentOf(XElement child, Axis axis)
    {
        Dimension names = axis.Names;
        if (axis.Definitions is not { } definitions) { return Extent.NotASubject; }   // One implicit * slot.

        (Length declared, _) = DeclaredSize(child, names);
        if (declared.Kind != Kind.Flexible) { return Extent.NotASubject; }             // GridSlotOverflowRule's.

        List<string> unknown = [];
        if (definitions.Unevaluated is { } whole) { unknown.Add(whole.Shown); }

        Placement index = Attached(child, names.Index, fallback: 0, minimum: 0);
        Placement span = Attached(child, names.Span, fallback: 1, minimum: 1);
        if (index.Value is null) { unknown.Add(index.Shown); }
        if (span.Value is null) { unknown.Add(span.Shown); }

        int crossed = 1;

        if (definitions.Unevaluated is null)
        {
            SlotSize[] sizes = [.. definitions.Slots.Select((slot, i) => SizeOfSlot(slot, definitions.Bounds[i]))];

            if (index.Value is int at)
            {
                // Where the framework places it: past the last definition means IN the last one, and
                // a span running off the edge covers only the definitions that remain.
                int first = Math.Min(at, sizes.Length - 1);

                if (span.Value is int across)
                {
                    crossed = Math.Min(across, sizes.Length - first);
                    SlotSize[] spanned = [.. sizes.Skip(first).Take(crossed)];
                    if (spanned.Any(size => size.Room == Room.Some)) { return Extent.HasRoom; }

                    if (crossed > 1)
                    {
                        if (axis.Spacing.Kind == Kind.Fixed && axis.Spacing.Value > 0) { return Extent.HasRoom; }
                        if (axis.Spacing.Kind == Kind.Unknown) { unknown.Add(axis.Spacing.Shown); }
                    }

                    if (spanned.Any(size => size.Room == Room.Flexible)) { return Extent.NotASubject; }
                    unknown.AddRange(spanned.Where(size => size.Room == Room.Unknown).Select(size => size.Shown));
                }
                else if (sizes[first].Room == Room.Some)
                {
                    return Extent.HasRoom;   // Whatever it spans, it has the room of the slot it starts in.
                }
                else if (sizes[first].Room == Room.Flexible)
                {
                    return Extent.NotASubject;
                }
                else if (sizes[first].Room == Room.Unknown)
                {
                    unknown.Add(sizes[first].Shown);
                }
            }
            else if (sizes.All(size => size.Room == Room.Some))
            {
                return Extent.HasRoom;       // Wherever it lands, it has room.
            }
            else if (sizes.All(size => size.Room == Room.Flexible))
            {
                return Extent.NotASubject;   // Wherever it lands, no slot's size is stated.
            }
        }

        if (unknown.Count > 0)
        {
            return new Extent(
                Outcome.Undecided,
                $"Whether the Grid {names.SlotWord}s it is placed in have any {names.SizeWord} was not "
                + $"checked, because markup cannot evaluate {Join(unknown)}.");
        }

        string where = crossed == 1
            ? $"a Grid {names.SlotWord} of no {names.SizeWord}"
            : $"{crossed} Grid {names.SlotWord}s of no {names.SizeWord} together";

        return new Extent(
            Outcome.Empty,
            $"This {child.Name.LocalName} is placed in {where}, and nothing else hides it. It is "
            + $"arranged 0 {names.DimensionWord}, or at a minimum its theme sets, drawn over its "
            + "neighbours, and either way it stays in the automation tree, where a test or an agent "
            + "finds a control nobody can see or use. Set or bind IsVisible alongside whatever sizes "
            + $"the {names.SlotWord}: that removes it from layout and from the tree together.");
    }

    /// <summary>
    /// A slot's size as the framework arranges it: its <c>MinHeight</c> first, then a <c>MaxHeight</c>
    /// of 0, then its size, a star of weight 0 counting as none.
    /// </summary>
    private static SlotSize SizeOfSlot(Length size, SlotBounds bounds)
    {
        if (bounds.Minimum is { Kind: Kind.Fixed, Value: > 0 }) { return new SlotSize(Room.Some, string.Empty); }

        SlotSize result =
            bounds.Maximum is { Kind: Kind.Fixed, Value: 0 } || bounds.ZeroStar ? new SlotSize(Room.None, string.Empty)
            : size.Kind == Kind.Fixed ? new SlotSize(size.Value > 0 ? Room.Some : Room.None, string.Empty)
            : size.Kind == Kind.Unknown ? new SlotSize(Room.Unknown, size.Shown)
            : new SlotSize(Room.Flexible, string.Empty);

        // A cap markup cannot evaluate could empty a slot that has room.
        if (result.Room == Room.Some && bounds.Maximum.Kind == Kind.Unknown)
        {
            result = new SlotSize(Room.Unknown, bounds.Maximum.Shown);
        }

        // A minimum markup cannot evaluate could give room to a slot that has none.
        if (result.Room == Room.None && bounds.Minimum.Kind == Kind.Unknown)
        {
            result = new SlotSize(Room.Unknown, bounds.Minimum.Shown);
        }

        return result;
    }

    /// <summary>
    /// Whether the control says how it is hidden: an <c>IsVisible</c> other than a literal <c>True</c>,
    /// or WPF's <c>Visibility</c> other than <c>Visible</c>, as an attribute or a property element.
    /// </summary>
    private static bool IsHidden(XElement child) =>
        SaysHidden(child, "IsVisible", "True") || SaysHidden(child, "Visibility", "Visible");

    private static bool SaysHidden(XElement child, string property, string shown)
    {
        foreach (XAttribute attribute in child.Attributes())
        {
            if (string.Equals(attribute.Name.LocalName, property, StringComparison.Ordinal))
            {
                return !string.Equals(attribute.Value.Trim(), shown, StringComparison.OrdinalIgnoreCase);
            }
        }

        foreach (XElement element in child.Elements())
        {
            if (string.Equals(element.Name.LocalName, child.Name.LocalName + "." + property, StringComparison.Ordinal))
            {
                // An element such as a binding decides it; an empty element sets nothing.
                return element.HasElements
                    || (!string.IsNullOrWhiteSpace(element.Value)
                        && !string.Equals(element.Value.Trim(), shown, StringComparison.OrdinalIgnoreCase));
            }
        }

        return false;
    }

    /// <summary>What a slot's size gives a control.</summary>
    private enum Room
    {
        /// <summary>A size above 0.</summary>
        Some,

        /// <summary>A size of 0.</summary>
        None,

        /// <summary><c>Auto</c> or a star with weight: no size markup states.</summary>
        Flexible,

        /// <summary>A value markup cannot evaluate decides it.</summary>
        Unknown,
    }

    private enum Outcome
    {
        NotASubject,
        HasRoom,
        Empty,
        Undecided,
    }

    /// <summary>A slot's room, and the value that decides it when markup cannot evaluate that.</summary>
    private readonly record struct SlotSize(Room Room, string Shown);

    private readonly record struct Extent(Outcome Outcome, string Text)
    {
        public static Extent NotASubject => new(Outcome.NotASubject, string.Empty);

        public static Extent HasRoom => new(Outcome.HasRoom, string.Empty);

        public bool IsDecided => Outcome is Outcome.HasRoom or Outcome.Empty;
    }
}
