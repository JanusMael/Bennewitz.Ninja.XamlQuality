using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit.Tests;

public sealed class ContrastAuditTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-con-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scores_consumer_tokens_against_each_other_and_against_the_variants_own_surface()
    {
        // The host: a page surface per variant, with a high-contrast variant that paints the
        // page through a different key, inheriting Dark for everything else.
        WriteFile("Host.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="HostPage" Color="#FFFFFF" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="HostPage" Color="#16161A" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="NightSky">
                  <SolidColorBrush x:Key="HostWindow" Color="#000000" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);
        // The consumer: Light/Dark tokens only; NightSky falls back to Dark through inheritance.
        // Dark's Ink is deliberately too faint on Paper.
        WriteFile("Tokens.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="T.Paper" Color="#FFFFFF" />
                  <SolidColorBrush x:Key="T.Ink" Color="#1C1F23" />
                  <SolidColorBrush x:Key="T.Marker" Color="#2E7D32" />
                  <SolidColorBrush x:Key="T.Tint" Color="#332E7D32" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="T.Paper" Color="#1E1E1E" />
                  <SolidColorBrush x:Key="T.Ink" Color="#3A3A3A" />
                  <SolidColorBrush x:Key="T.Marker" Color="#81C784" />
                  <SolidColorBrush x:Key="T.Tint" Color="#3381C784" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        Dictionary<string, string> inheritance = new(StringComparer.Ordinal) { ["NightSky"] = "Dark" };
        ThemeInventory host = ThemeInventory.Build(Path.Combine(_root, "Host.axaml"), _root, inheritance: inheritance);
        ThemeInventory tokens = ThemeInventory.Build(Path.Combine(_root, "Tokens.axaml"), _root, inheritance: inheritance);
        SurfaceMap surfaces = new(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Page"] = "HostPage" },
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["NightSky"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Page"] = "HostWindow" },
            });

        List<ContrastPair> pairs =
        [
            new("T.Ink", "T.Paper", Contrast.AaText) { Purpose = "text on the pane" },
            new("T.Marker", "$Page", Contrast.AaLargeOrGraphic) { Purpose = "marker on the host page" },
            new("T.Ink", "T.Tint", Contrast.AaText) { Over = "T.Paper", Purpose = "text on a tinted row" },
        ];

        IReadOnlyList<ContrastFinding> findings = ContrastAudit.Score(pairs, host, ["Light", "Dark", "NightSky"], tokens, surfaces);
        Assert.Equal(9, findings.Count);

        ContrastFinding Find(string variant, string foreground, string background) =>
            findings.Single(f => f.DisplayName == variant && f.Pair.Foreground == foreground && f.Pair.Background == background);

        // Ink on Paper: passes in Light, fails in Dark (and therefore NightSky, which inherits Dark's tokens).
        Assert.Equal(ContrastStatus.Pass, Find("Light", "T.Ink", "T.Paper").Status);
        Assert.True(Find("Light", "T.Ink", "T.Paper").Ratio > 15);
        ContrastFinding darkInk = Find("Dark", "T.Ink", "T.Paper");
        Assert.Equal(ContrastStatus.Fail, darkInk.Status);
        Assert.True(darkInk.Ratio < Contrast.AaText);
        Assert.Equal(ContrastStatus.Fail, Find("NightSky", "T.Ink", "T.Paper").Status);

        // Marker on the host page: the placeholder resolves per variant, NightSky through its own window key.
        Assert.Equal("HostPage", Find("Light", "T.Marker", "$Page").BackgroundKey);
        Assert.Equal("HostWindow", Find("NightSky", "T.Marker", "$Page").BackgroundKey);
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x00, 0x00), Find("NightSky", "T.Marker", "$Page").Background);
        Assert.Equal(ContrastStatus.Pass, Find("NightSky", "T.Marker", "$Page").Status);

        // The tint is composited over Paper before scoring, so the background reported is opaque.
        ContrastFinding lightTint = Find("Light", "T.Ink", "T.Tint");
        Assert.Equal(255, lightTint.Background!.Value.A);
        Assert.NotEqual(new AuditColor(0xFF, 0xFF, 0xFF, 0xFF), lightTint.Background);
        Assert.Equal(ContrastStatus.Pass, lightTint.Status);
    }

    [Fact]
    public void A_pair_that_cannot_be_measured_is_reported_unscored_with_the_reason_never_dropped()
    {
        WriteFile("Host.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="Page" Color="#FFFFFF" />
                  <SolidColorBrush x:Key="Ink" Color="#000000" />
                  <SolidColorBrush x:Key="Veil" Color="#80000000" />
                  <Thickness x:Key="Pad">4</Thickness>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);
        ThemeInventory host = ThemeInventory.Build(Path.Combine(_root, "Host.axaml"), _root);

        List<ContrastPair> pairs =
        [
            new("Ink", "Missing", Contrast.AaText),
            new("Pad", "Page", Contrast.AaText),
            new("Ink", "$Page", Contrast.AaText),
            new("Ink", "Veil", Contrast.AaText),
            new("Ink", "Veil", Contrast.AaText) { Over = "Veil" },
            new("Ink", "Veil", Contrast.AaText) { Over = "Page" },
        ];

        IReadOnlyList<ContrastFinding> findings = ContrastAudit.Score(pairs, host, ["Light"]);
        Assert.Equal(6, findings.Count);

        Assert.Equal(ContrastStatus.Unscored, findings[0].Status);
        Assert.Contains("Missing is undefined", findings[0].Reason);
        Assert.Equal(ContrastStatus.Unscored, findings[1].Status);
        Assert.Contains("Pad is undefined or not a colour", findings[1].Reason);
        Assert.Equal(ContrastStatus.Unscored, findings[2].Status);
        Assert.Contains("names no $Page surface", findings[2].Reason);
        Assert.Equal(ContrastStatus.Unscored, findings[3].Status);
        Assert.Contains("translucent", findings[3].Reason);
        Assert.Equal(ContrastStatus.Unscored, findings[4].Status);
        Assert.Contains("itself translucent", findings[4].Reason);

        // Composited over the page, the veil is mid-grey: black ink on #7F7F7F is 5.2:1, not the
        // 21:1 it would score against the page or the 1:1 against the raw veil.
        Assert.Equal(ContrastStatus.Pass, findings[5].Status);
        Assert.Equal(new AuditColor(0xFF, 0x7F, 0x7F, 0x7F), findings[5].Background);
        Assert.InRange(findings[5].Ratio!.Value, 5.1, 5.4);
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
