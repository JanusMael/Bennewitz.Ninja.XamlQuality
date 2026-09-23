using System.Globalization;
using System.Xml;
using System.Xml.Linq;

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
/// ⚠ <b>Two spellings, and checking one is how a rule quietly passes.</b> Definitions are written
/// either as the attribute shorthand (<c>RowDefinitions="Auto,0,*"</c>) or as property ELEMENTS
/// (<c>&lt;Grid.RowDefinitions&gt;&lt;RowDefinition Height="0"/&gt;…</c>). The shorthand is more
/// common in hand-written markup, which is exactly why the element form is the one that goes
/// unhandled.
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
    public string Id => "XQ1004";

    /// <inheritdoc />
    public string Summary => "Every control fits the fixed Grid slot it is placed in.";

    /// <inheritdoc />
    public XamlRuleResult Analyze(XamlScanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<XamlFinding> findings = [];
        int inspected = 0;

        foreach (XamlFile file in context.ParsedFiles)
        {
            foreach (XElement grid in file.Document!.Descendants())
            {
                // Local name only: the markup namespace differs between Avalonia, WPF and MAUI, and
                // matching a fully-qualified name would make the rule silently inert on two of three.
                if (!string.Equals(grid.Name.LocalName, "Grid", StringComparison.Ordinal)) { continue; }

                IReadOnlyList<double?> rows = Definitions(grid, "RowDefinitions", "RowDefinition", "Height");
                IReadOnlyList<double?> columns = Definitions(grid, "ColumnDefinitions", "ColumnDefinition", "Width");
                if (rows.Count == 0 && columns.Count == 0) { continue; }

                // Grid.Row and Grid.Column bind to DIRECT children only; an element nested inside
                // another panel is positioned by that panel, not by this Grid.
                foreach (XElement child in grid.Elements())
                {
                    // <Grid.RowDefinitions> and friends are property elements, not children.
                    if (child.Name.LocalName.Contains('.', StringComparison.Ordinal)) { continue; }

                    inspected++;

                    Report(child, rows, "Row", "MinHeight", "Height", "row", "tall", findings, file);
                    Report(child, columns, "Column", "MinWidth", "Width", "column", "wide", findings, file);
                }
            }
        }

        return new XamlRuleResult(findings, inspected);
    }

    private void Report(
        XElement child,
        IReadOnlyList<double?> slots,
        string indexProperty,
        string minProperty,
        string sizeProperty,
        string slotWord,
        string dimensionWord,
        List<XamlFinding> findings,
        XamlFile file)
    {
        int index = AttachedIndex(child, indexProperty);
        if (index < 0 || index >= slots.Count) { return; }

        double? slot = slots[index];
        if (slot is null) { return; }               // Auto or star: not decidable from markup.

        double? declared = Number(child, minProperty) ?? Number(child, sizeProperty);
        if (declared is null || declared <= slot) { return; }

        int? line = (child as IXmlLineInfo).HasLineInfo() ? ((IXmlLineInfo)child).LineNumber : null;

        findings.Add(new XamlFinding(
            Id,
            file.Path,
            file.RelativePath,
            line,
            $"This {child.Name.LocalName} asks to be {Format(declared.Value)} {dimensionWord} in a "
            + $"{slotWord} fixed at {Format(slot.Value)}. It is arranged at the size it asked for and "
            + "keeps that size in the automation tree, so a screen reader and a UI test see it even "
            + "when nothing is drawn. Bind IsVisible alongside whatever sizes the "
            + $"{slotWord} — that removes it from layout and from the tree together, which "
            + "ClipToBounds does not."));
    }

    /// <summary>
    /// Slot sizes in order, <c>null</c> where the size is not decidable from markup.
    /// </summary>
    /// <remarks>
    /// ⚠ Reads BOTH spellings. The attribute shorthand wins when both are present, matching the
    /// framework — but markup carrying both is already confusing enough that the rule does not try
    /// to be clever about it.
    /// </remarks>
    private static IReadOnlyList<double?> Definitions(
        XElement grid, string collectionProperty, string itemName, string sizeProperty)
    {
        XAttribute? shorthand = grid.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, collectionProperty, StringComparison.Ordinal));

        if (shorthand is not null)
        {
            return [.. shorthand.Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Fixed)];
        }

        XElement? collection = grid.Elements()
            .FirstOrDefault(e => e.Name.LocalName.EndsWith(collectionProperty, StringComparison.Ordinal));

        if (collection is null) { return []; }

        return [.. collection.Elements()
            .Where(e => string.Equals(e.Name.LocalName, itemName, StringComparison.Ordinal))
            .Select(e => Fixed(e.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, sizeProperty, StringComparison.Ordinal))
                ?.Value ?? "*"))];
    }

    /// <summary>A fixed size, or <c>null</c> for <c>Auto</c>, <c>*</c> and anything unparseable.</summary>
    private static double? Fixed(string raw) =>
        raw.Contains('*', StringComparison.Ordinal)
            ? null
            : double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : null;

    private static double? Number(XElement element, string property)
    {
        XAttribute? attribute = element.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, property, StringComparison.Ordinal));

        return attribute is null ? null : Fixed(attribute.Value);
    }

    /// <summary>The attached <c>Grid.Row</c> / <c>Grid.Column</c> index; 0 when unset, as the framework does.</summary>
    private static int AttachedIndex(XElement element, string property)
    {
        XAttribute? attribute = element.Attributes().FirstOrDefault(a =>
            a.Name.LocalName.EndsWith(property, StringComparison.Ordinal)
            && a.Name.LocalName.Contains("Grid", StringComparison.Ordinal));

        if (attribute is null) { return 0; }

        return int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            ? index
            : -1;
    }

    private static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
