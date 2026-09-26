using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>
/// How markup lays out a <c>Grid</c>: its slots in each direction, and where a child is placed in them.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Shared because two rules placing one control is how their answers drift.</b> <c>BNXQ1004</c>
/// asks whether a control fits the slots it is placed in, and <c>BNXQ1008</c> whether those slots have
/// any size at all. Both read a grid's definitions and a child's placement here. This began as
/// <c>BNXQ1004</c>'s own parsing and moved here unchanged.
/// </para>
/// <para>
/// ⚠ <b>Two spellings, and checking one is how a rule quietly passes.</b> Definitions are written
/// either as the attribute shorthand (<c>RowDefinitions="Auto,0,*"</c>) or as property ELEMENTS
/// (<c>&lt;Grid.RowDefinitions&gt;&lt;RowDefinition Height="0"/&gt;…</c>). The shorthand is more
/// common in hand-written markup, which is exactly why the element form is the one that goes
/// unhandled.
/// </para>
/// <para>
/// ⚠ <b>A definition's bounds are read beside its size, for the rule that uses them.</b> Measured on
/// Avalonia 12.1.3, a row's <c>MinHeight</c> wins over its <c>Height</c> and its <c>MaxHeight</c>, a
/// <c>MaxHeight</c> of 0 empties any row, <c>Auto</c> and <c>*</c> included, and a star of weight 0
/// gets nothing, alone or beside another star. <see cref="SlotBounds"/> carries those for
/// <c>BNXQ1008</c>. <c>BNXQ1004</c> still measures a slot by its size alone.
/// </para>
/// </remarks>
internal static class GridLayout
{
    /// <summary>A grid's rows, and the words a finding about them is written in.</summary>
    internal static readonly Dimension Rows = new(
        "RowDefinitions", "RowDefinition", "Row", "RowSpan", "RowSpacing", "MinHeight", "MaxHeight", "Height", "row", "height", "tall");

    /// <summary>A grid's columns, and the words a finding about them is written in.</summary>
    internal static readonly Dimension Columns = new(
        "ColumnDefinitions", "ColumnDefinition", "Column", "ColumnSpan", "ColumnSpacing", "MinWidth", "MaxWidth", "Width", "column", "width", "wide");

    /// <summary>
    /// Slot sizes in order, or <c>null</c> when the grid declares none, which the framework treats as
    /// one <c>*</c> slot.
    /// </summary>
    /// <remarks>
    /// ⚠ Reads BOTH spellings. The attribute shorthand wins when both are present, matching the
    /// framework — but markup carrying both is already confusing enough that the rule does not try
    /// to be clever about it. A shorthand that is a markup extension as a whole is one unevaluated
    /// value, never split at the commas inside it.
    /// </remarks>
    private static Definitions? DefinitionsOf(XElement grid, Dimension names)
    {
        XAttribute? shorthand = grid.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, names.Definitions, StringComparison.Ordinal));

        if (shorthand is not null)
        {
            if (IsMarkupExtension(shorthand.Value))
            {
                return new Definitions([], [], new Length(Kind.Unknown, 0, $"{names.Definitions}=\"{shorthand.Value}\""));
            }

            string[] texts = shorthand.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Length[] entries = [.. texts.Select((entry, i) => Slot(entry, $"{names.SlotWord} {i}'s {names.SizeWord} \"{entry}\""))];
            SlotBounds[] bounds = [.. texts.Select(entry => new SlotBounds(Length.Flexible, Length.Flexible, IsZeroStar(entry)))];

            return entries.Length == 0 ? null : new Definitions(entries, bounds, null);
        }

        XElement? collection = grid.Elements()
            .FirstOrDefault(e => e.Name.LocalName.EndsWith(names.Definitions, StringComparison.Ordinal));

        if (collection is null) { return null; }

        XElement[] definitions = [.. collection.Elements()
            .Where(e => string.Equals(e.Name.LocalName, names.Definition, StringComparison.Ordinal))];

        Length[] items = [.. definitions
            .Select((e, i) => AttributeOf(e, names.Size) is { } size
                ? Slot(size.Value, $"{names.SlotWord} {i}'s {names.Size}=\"{size.Value}\"")
                : Length.Flexible)];   // Unset, a definition is *.

        SlotBounds[] limits = [.. definitions
            .Select((e, i) => new SlotBounds(
                Bound(AttributeOf(e, names.MinSize), $"{names.SlotWord} {i}'s {names.MinSize}"),
                Bound(AttributeOf(e, names.MaxSize), $"{names.SlotWord} {i}'s {names.MaxSize}"),
                AttributeOf(e, names.Size) is { } size && IsZeroStar(size.Value)))];

        return items.Length == 0 ? null : new Definitions(items, limits, null);
    }

    /// <summary>
    /// The grid's <c>RowSpacing</c> or <c>ColumnSpacing</c>, in either spelling: 0 when unset, as the
    /// framework treats it.
    /// </summary>
    /// <remarks>
    /// ⚠ Any finite number is a spacing, a negative one included. The framework does not validate
    /// spacing, and applies a negative one as it is, overlapping the slots a span crosses.
    /// </remarks>
    private static Length SpacingOf(XElement grid, string property)
    {
        if (grid.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, property, StringComparison.Ordinal))
            is { } attribute)
        {
            return Gap(attribute.Value, $"{property}=\"{attribute.Value}\"");
        }

        if (grid.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("." + property, StringComparison.Ordinal))
            is { } element)
        {
            return element.Elements().FirstOrDefault() is { } content
                ? new Length(Kind.Unknown, 0, $"{element.Name.LocalName} holding <{content.Name.LocalName}>")
                : Gap(element.Value, $"{property}=\"{element.Value.Trim()}\"");
        }

        return new Length(Kind.Fixed, 0, string.Empty);
    }

    private static Length Gap(string raw, string shown) =>
        double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value)
            ? new Length(Kind.Fixed, value, shown)
            : new Length(Kind.Unknown, 0, shown);

    /// <summary>
    /// What the control asks for: its <c>MinWidth</c> when that is a literal size, else its
    /// <c>Width</c>; and, when neither is literal, which of them markup cannot evaluate.
    /// </summary>
    internal static (Length Declared, string[] Unevaluated) DeclaredSize(XElement child, Dimension names)
    {
        Length minimum = SizeOf(child, names.MinSize);
        Length size = SizeOf(child, names.Size);

        Length declared = minimum.Kind == Kind.Fixed ? minimum
            : size.Kind == Kind.Fixed ? size
            : minimum.Kind == Kind.Unknown ? minimum
            : size;

        return (declared, [.. ((Length[])[minimum, size]).Where(value => value.Kind == Kind.Unknown).Select(value => value.Shown)]);
    }

    private static Length SizeOf(XElement element, string property)
    {
        XAttribute? attribute = element.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, property, StringComparison.Ordinal));

        if (attribute is null) { return Length.Flexible; }

        string text = attribute.Value.Trim();
        string shown = $"{property}=\"{attribute.Value}\"";

        // NaN is how a control says its size is unset, and WPF spells the same thing Auto.
        return IsMarkupExtension(text) ? new Length(Kind.Unknown, 0, shown)
            : string.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase) || IsAuto(text) ? Length.Flexible
            : Amount(text, shown);
    }

    /// <summary>
    /// A slot's size as the framework's parser reads it: <c>Auto</c> in any case, a trailing
    /// <c>*</c> for a star, and otherwise a number.
    /// </summary>
    private static Length Slot(string raw, string shown)
    {
        string text = raw.Trim();
        return IsMarkupExtension(text) ? new Length(Kind.Unknown, 0, shown)
            : IsAuto(text) || text.EndsWith('*') ? Length.Flexible
            : Amount(text, shown);
    }

    /// <summary>A star of weight 0, such as <c>0*</c>, which the framework gives no space.</summary>
    private static bool IsZeroStar(string raw)
    {
        string text = raw.Trim();
        return text.Length > 1
            && text.EndsWith('*')
            && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double weight)
            && weight == 0;
    }

    /// <summary>
    /// A definition's <c>MinHeight</c> or <c>MaxHeight</c>: unset when absent or infinite, a literal
    /// size, or a value markup cannot evaluate.
    /// </summary>
    private static Length Bound(XAttribute? attribute, string property)
    {
        if (attribute is null) { return Length.Flexible; }

        string text = attribute.Value.Trim();
        string shown = $"{property}=\"{attribute.Value}\"";

        return IsMarkupExtension(text) ? new Length(Kind.Unknown, 0, shown)
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsPositiveInfinity(value) ? Length.Flexible
            : Amount(text, shown);
    }

    /// <summary>A finite, non-negative number is a fixed size; anything else is not one markup states.</summary>
    private static Length Amount(string raw, string shown) =>
        double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) && value >= 0
            ? new Length(Kind.Fixed, value, shown)
            : new Length(Kind.Unknown, 0, shown);

    /// <summary>
    /// An attached <c>Grid</c> integer, the <c>Row</c>, <c>Column</c> or a span:
    /// <paramref name="fallback"/> when unset, as the framework does, and unknown when the value is not
    /// a literal integer the framework accepts.
    /// </summary>
    internal static Placement Attached(XElement element, string property, int fallback, int minimum)
    {
        XAttribute? attribute = element.Attributes().FirstOrDefault(a =>
            a.Name.LocalName.EndsWith(property, StringComparison.Ordinal)
            && a.Name.LocalName.Contains("Grid", StringComparison.Ordinal));

        if (attribute is null) { return new Placement(fallback, string.Empty); }

        return int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value >= minimum
            ? new Placement(value, string.Empty)
            : new Placement(null, $"{attribute.Name.LocalName}=\"{attribute.Value}\"");
    }

    private static XAttribute? AttributeOf(XElement element, string property) =>
        element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, property, StringComparison.Ordinal));

    internal static bool IsMarkupExtension(string value) =>
        value.TrimStart().StartsWith('{');

    private static bool IsAuto(string text) =>
        string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase);

    internal static string Join(List<string> values) =>
        values.Count == 1 ? values[0] : $"{string.Join(", ", values.Take(values.Count - 1))} and {values[^1]}";

    internal static int? LineOf(XElement element) =>
        (element as IXmlLineInfo).HasLineInfo() ? ((IXmlLineInfo)element).LineNumber : null;

    internal static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>How a size reads from markup.</summary>
    internal enum Kind
    {
        /// <summary>A literal size.</summary>
        Fixed,

        /// <summary>No fixed size: <c>Auto</c> or <c>*</c> for a slot, and unset for a control or a bound.</summary>
        Flexible,

        /// <summary>A value markup cannot evaluate: a binding, a resource, or text that is not a size.</summary>
        Unknown,
    }

    /// <summary>A size as markup states it, and how to name it in a message.</summary>
    internal readonly record struct Length(Kind Kind, double Value, string Shown)
    {
        public static Length Flexible => new(Kind.Flexible, 0, string.Empty);
    }

    /// <summary>A definition's <c>MinHeight</c> and <c>MaxHeight</c>, and whether it is a star of weight 0.</summary>
    internal readonly record struct SlotBounds(Length Minimum, Length Maximum, bool ZeroStar);

    /// <summary>A grid's index or span: <c>null</c> when markup cannot evaluate it.</summary>
    internal readonly record struct Placement(int? Value, string Shown);

    /// <summary>
    /// A grid's slots in one direction, with each one's bounds, or the one value they all come from
    /// when markup cannot evaluate it.
    /// </summary>
    internal sealed record Definitions(IReadOnlyList<Length> Slots, IReadOnlyList<SlotBounds> Bounds, Length? Unevaluated);

    /// <summary>One direction of a grid, read once for all its children.</summary>
    internal sealed record Axis(Dimension Names, Definitions? Definitions, Length Spacing)
    {
        public static Axis Of(XElement grid, Dimension names) =>
            new(names, DefinitionsOf(grid, names), SpacingOf(grid, names.Spacing));
    }

    /// <summary>The property names and words one direction of a grid is written and reported in.</summary>
    internal sealed record Dimension(
        string Definitions,
        string Definition,
        string Index,
        string Span,
        string Spacing,
        string MinSize,
        string MaxSize,
        string Size,
        string SlotWord,
        string SizeWord,
        string DimensionWord);
}
