using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

public sealed class ThemeInventoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-inv-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Composes_per_variant_keys_from_the_include_graph_and_resolves_alias_chains()
    {
        // Semi-shaped: an entry with ThemeDictionaries include-variants and a shared Tokens include.
        WriteFile("Index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceInclude x:Key="Light" Source="/Themes/Light/_index.axaml" />
                <ResourceInclude x:Key="Dark" Source="/Themes/Dark/_index.axaml" />
              </ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="/Tokens/_index.axaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);
        // Shared token: a base colour every variant inherits.
        WriteFile("Tokens/_index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="BaseInk">#111111</Color>
            </ResourceDictionary>
            """);
        // Light: a literal plus a brush that aliases a token defined in this same variant.
        WriteFile("Themes/Light/_index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="Button.axaml" />
              </ResourceDictionary.MergedDictionaries>
              <Color x:Key="AccentColor">#2E7D32</Color>
              <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentColor}" />
            </ResourceDictionary>
            """);
        WriteFile("Themes/Light/Button.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="ButtonBackground" Color="#FFFFFF" />
            </ResourceDictionary>
            """);
        WriteFile("Themes/Dark/_index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="AccentColor">#4CAF50</Color>
              <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource AccentColor}" />
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Index.axaml"), _root);
        Assert.Empty(inventory.Unresolved);

        VariantInventory light = inventory.Variants.Single(v => v.DisplayName == "Light");
        VariantInventory dark = inventory.Variants.Single(v => v.DisplayName == "Dark");

        // Shared base key is present in both.
        Assert.Equal(new AuditColor(0xFF, 0x11, 0x11, 0x11), light.Resolve("BaseInk"));
        Assert.Equal(new AuditColor(0xFF, 0x11, 0x11, 0x11), dark.Resolve("BaseInk"));

        // Variant-specific literal, and the alias brush resolving to it, differ per variant.
        Assert.Equal(new AuditColor(0xFF, 0x2E, 0x7D, 0x32), light.Resolve("AccentBrush"));
        Assert.Equal(new AuditColor(0xFF, 0x4C, 0xAF, 0x50), dark.Resolve("AccentBrush"));

        // A key from a nested include on one side is present there and absent on the other.
        Assert.True(light.Defines("ButtonBackground"));
        Assert.False(dark.Defines("ButtonBackground"));
        Assert.Equal(new AuditColor(0xFF, 0xFF, 0xFF, 0xFF), light.Resolve("ButtonBackground"));
    }

    [Fact]
    public void A_high_contrast_variant_inherits_its_parents_keys()
    {
        WriteFile("Index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:semi="https://irihi.tech/semi">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceInclude x:Key="Dark" Source="/Dark.axaml" />
                <ResourceInclude x:Key="{x:Static semi:SemiTheme.NightSky}" Source="/HighContrast.axaml" />
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);
        WriteFile("Dark.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="OnlyInDark">#222222</Color>
              <Color x:Key="Shared">#333333</Color>
            </ResourceDictionary>
            """);
        WriteFile("HighContrast.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Shared">#000000</Color>
            </ResourceDictionary>
            """);

        Dictionary<string, string> inheritance = new(StringComparer.Ordinal) { ["NightSky"] = "Dark" };
        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Index.axaml"), _root, inheritance: inheritance);

        VariantInventory nightSky = inventory.Variants.Single(v => v.DisplayName == "NightSky");
        // Inherited from Dark.
        Assert.Equal(new AuditColor(0xFF, 0x22, 0x22, 0x22), nightSky.Resolve("OnlyInDark"));
        // Overridden by the high-contrast variant.
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x00, 0x00), nightSky.Resolve("Shared"));
    }

    [Fact]
    public void An_undefined_or_opaque_key_resolves_to_no_colour()
    {
        WriteFile("Index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <Thickness x:Key="Pad">4</Thickness>
                  <SolidColorBrush x:Key="Dangling" Color="{DynamicResource NoSuchKey}" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Index.axaml"), _root);
        VariantInventory light = inventory.Variants.Single(v => v.DisplayName == "Light");

        Assert.True(light.Defines("Pad"));
        Assert.Null(light.Resolve("Pad"));           // opaque
        Assert.Null(light.Resolve("Dangling"));       // alias to a missing key
        Assert.Null(light.Resolve("NoSuchKey"));      // undefined
        Assert.False(light.Defines("NoSuchKey"));
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
