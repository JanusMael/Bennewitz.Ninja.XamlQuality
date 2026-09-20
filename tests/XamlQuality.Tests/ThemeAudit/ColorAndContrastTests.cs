using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit.Tests;

public sealed class AuditColorTests
{
    [Theory]
    [InlineData("#FF804020", 0xFF, 0x80, 0x40, 0x20)]
    [InlineData("#804020", 0xFF, 0x80, 0x40, 0x20)]
    [InlineData("#8421", 0x88, 0x44, 0x22, 0x11)]
    [InlineData("#421", 0xFF, 0x44, 0x22, 0x11)]
    [InlineData("  #ffffff  ", 0xFF, 0xFF, 0xFF, 0xFF)]
    public void Parses_every_hex_form(string text, int a, int r, int g, int b)
    {
        Assert.True(AuditColor.TryParse(text, out AuditColor color));
        Assert.Equal(new AuditColor((byte)a, (byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData("Transparent", 0x00, 0x00, 0x00, 0x00)]
    [InlineData("white", 0xFF, 0xFF, 0xFF, 0xFF)]
    [InlineData("Black", 0xFF, 0x00, 0x00, 0x00)]
    [InlineData("Green", 0xFF, 0x00, 0x80, 0x00)]
    [InlineData("CornflowerBlue", 0xFF, 0x64, 0x95, 0xED)]
    public void Parses_named_colours_case_insensitively(string name, int a, int r, int g, int b)
    {
        Assert.True(AuditColor.TryParse(name, out AuditColor color));
        Assert.Equal(new AuditColor((byte)a, (byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("{StaticResource SystemAccentColor}")]
    [InlineData("NotAColour")]
    public void Rejects_non_colours(string? text)
    {
        Assert.False(AuditColor.TryParse(text, out _));
    }

    [Fact]
    public void Green_is_the_dark_web_green_not_lime()
    {
        // A classic named-colour trap: CSS "Green" is #008000, "Lime" is #00FF00.
        Assert.True(AuditColor.TryParse("Green", out AuditColor green));
        Assert.True(AuditColor.TryParse("Lime", out AuditColor lime));
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x80, 0x00), green);
        Assert.Equal(new AuditColor(0xFF, 0x00, 0xFF, 0x00), lime);
    }

    [Fact]
    public void Composites_a_translucent_colour_over_a_background()
    {
        AuditColor half = new(128, 255, 255, 255); // ~50% white
        AuditColor onBlack = half.CompositeOver(new AuditColor(255, 0, 0, 0));
        Assert.Equal(255, onBlack.A);
        // 255 * (128/255) + 0 ≈ 128 on each channel.
        Assert.InRange(onBlack.R, 127, 129);
        Assert.Equal(onBlack.R, onBlack.G);
        Assert.Equal(onBlack.R, onBlack.B);
    }

    [Fact]
    public void An_opaque_colour_composites_to_itself()
    {
        AuditColor opaque = new(255, 10, 20, 30);
        Assert.Equal(opaque, opaque.CompositeOver(new AuditColor(255, 200, 200, 200)));
    }
}

public sealed class ContrastTests
{
    private const double Tolerance = 0.01;

    [Fact]
    public void Black_on_white_is_the_maximum_ratio()
    {
        double ratio = Contrast.Ratio(new AuditColor(255, 0, 0, 0), new AuditColor(255, 255, 255, 255));
        Assert.Equal(21.0, ratio, Tolerance);
    }

    [Fact]
    public void Identical_colours_have_ratio_one()
    {
        AuditColor grey = new(255, 119, 119, 119);
        Assert.Equal(1.0, Contrast.Ratio(grey, grey), Tolerance);
    }

    [Fact]
    public void Ratio_is_symmetric()
    {
        AuditColor a = new(255, 0x2E, 0x7D, 0x32); // ClaudeForge inserted-pill green
        AuditColor b = new(255, 0xFF, 0xFF, 0xFF);
        Assert.Equal(Contrast.Ratio(a, b), Contrast.Ratio(b, a), Tolerance);
    }

    [Fact]
    public void Known_pair_matches_the_wcag_reference_value()
    {
        // #767676 on white is the canonical 4.54:1 example from the WCAG docs — just above AA text.
        double ratio = Contrast.Ratio(new AuditColor(255, 0x76, 0x76, 0x76), new AuditColor(255, 255, 255, 255));
        Assert.Equal(4.54, ratio, 0.02);
        Assert.True(ratio >= Contrast.AaText);
    }

    [Fact]
    public void A_translucent_foreground_is_scored_after_compositing()
    {
        // 50%-alpha white over black composites to mid-grey, whose contrast on black is well below
        // the opaque-white value of 21.
        AuditColor translucentWhite = new(128, 255, 255, 255);
        AuditColor black = new(255, 0, 0, 0);
        double translucent = Contrast.Ratio(translucentWhite, black);
        double opaque = Contrast.Ratio(new AuditColor(255, 255, 255, 255), black);
        Assert.True(translucent < opaque);
        Assert.True(translucent < 6.0);
    }
}
