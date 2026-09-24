using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

/// <summary>
/// The facts the real reference themes forced into the inventory: Fluent and Simple declare only
/// <c>Default</c> and <c>Dark</c>; their control themes hang off a <c>StyleInclude</c>; Simple links
/// a file in from Fluent's folder; Fluent's accent colours come from a code provider; and Semi's
/// secondary text brushes carry an <c>Opacity</c> that changes what the reader sees.
/// </summary>
public sealed class InventoryFidelityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-fid-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Light_falls_back_to_Default_and_Default_keys_sit_under_every_variant()
    {
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Default">
                  <Color x:Key="Ink">#111111</Color>
                  <Color x:Key="OnlyInDefault">#222222</Color>
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <Color x:Key="Ink">#EEEEEE</Color>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Theme.axaml"), _root);

        // No Light dictionary: Light is Default, as Avalonia resolves it.
        VariantInventory light = inventory.ForVariant("Light");
        Assert.Equal("Default", light.DisplayName);
        Assert.Equal(new AuditColor(0xFF, 0x11, 0x11, 0x11), light.Resolve("Ink"));

        // Dark overrides Ink but still sees the key only Default defines.
        VariantInventory dark = inventory.ForVariant("Dark");
        Assert.Equal(new AuditColor(0xFF, 0xEE, 0xEE, 0xEE), dark.Resolve("Ink"));
        Assert.Equal(new AuditColor(0xFF, 0x22, 0x22, 0x22), dark.Resolve("OnlyInDefault"));

        // An undeclared high-contrast variant walks its inheritance to Dark.
        ThemeInventory withInheritance = ThemeInventory.Build(Path.Combine(_root, "Theme.axaml"), _root,
            inheritance: new Dictionary<string, string>(StringComparer.Ordinal) { ["NightSky"] = "Dark" });
        Assert.Equal("Dark", withInheritance.ForVariant("NightSky")?.DisplayName);

        // A theme with neither the variant nor Default still serves its base keys — and only those.
        WriteFile("NoDefault.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Dark">
                  <Color x:Key="Ink">#EEEEEE</Color>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
              <Thickness x:Key="Pad">4</Thickness>
            </ResourceDictionary>
            """);
        ThemeInventory noDefault = ThemeInventory.Build(Path.Combine(_root, "NoDefault.axaml"), _root);
        Assert.Equal("Dark", Assert.Single(noDefault.Variants).DisplayName);
        VariantInventory baseOnly = noDefault.ForVariant("Light");
        Assert.True(baseOnly.Defines("Pad"));
        Assert.False(baseOnly.Defines("Ink"));
    }

    [Fact]
    public void An_inheriting_variant_sees_its_parent_over_Default_not_Default_over_its_parent()
    {
        // Semi-shaped: Default and Light share the light palette, Dark has its own, and NightSky
        // inherits Dark. The page surface under NightSky must be Dark's, not Default's.
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Default">
                  <Color x:Key="Page">#FFFFFF</Color>
                  <Color x:Key="OnlyDefault">#ABCDEF</Color>
                </ResourceDictionary>
                <ResourceDictionary x:Key="Light">
                  <Color x:Key="Page">#FFFFFF</Color>
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <Color x:Key="Page">#16161A</Color>
                </ResourceDictionary>
                <ResourceDictionary x:Key="NightSky">
                  <Color x:Key="Window">#000000</Color>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
              <Thickness x:Key="Pad">4</Thickness>
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Theme.axaml"), _root,
            inheritance: new Dictionary<string, string>(StringComparer.Ordinal) { ["NightSky"] = "Dark" });

        VariantInventory nightSky = inventory.ForVariant("NightSky");
        Assert.Equal(new AuditColor(0xFF, 0x16, 0x16, 0x1A), nightSky.Resolve("Page"));      // Dark's, through inheritance
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x00, 0x00), nightSky.Resolve("Window"));    // its own
        Assert.Equal(new AuditColor(0xFF, 0xAB, 0xCD, 0xEF), nightSky.Resolve("OnlyDefault")); // Default underneath
        Assert.True(nightSky.Defines("Pad"));                                                  // base underneath everything
    }

    [Fact]
    public void Follows_a_StyleInclude_chain_to_the_control_themes_and_records_every_file()
    {
        // Fluent-shaped: Styles entry → StyleInclude → Styles with merged resource includes.
        WriteFile("FluentTheme.xaml",
            """
            <Styles xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Styles.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Default">
                      <Color x:Key="SystemBaseHighColor">#000000</Color>
                    </ResourceDictionary>
                  </ResourceDictionary.ThemeDictionaries>
                </ResourceDictionary>
              </Styles.Resources>
              <StyleInclude Source="/Controls/FluentControls.xaml" />
            </Styles>
            """);
        WriteFile("Controls/FluentControls.xaml",
            """
            <Styles xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Styles.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
                    <MergeResourceInclude Source="avares://Fixture.Fluent/Controls/Button.xaml" />
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
              </Styles.Resources>
            </Styles>
            """);
        WriteFile("Controls/Button.xaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ControlTheme x:Key="{x:Type Button}" TargetType="Button" />
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(new ThemeSource(Path.Combine(_root, "FluentTheme.xaml"), _root)
        {
            AssemblyName = "Fixture.Fluent",
        });

        Assert.Empty(inventory.Unresolved);
        VariantInventory light = inventory.ForVariant("Light");
        Assert.True(light.Defines("{x:Type Button}"));
        Assert.Equal(
            ["FluentTheme.xaml", "Controls/FluentControls.xaml", "Controls/Button.xaml"],
            inventory.Files.Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/')).ToArray());
    }

    [Fact]
    public void A_linked_file_resolves_through_the_link_map()
    {
        // Simple-shaped: the entry includes /Strings/InvariantResources.xaml, which the project
        // links in from the Fluent folder rather than keeping under its own directory.
        WriteFile("Simple/SimpleTheme.xaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.MergedDictionaries>
                <MergeResourceInclude Source="/Strings/InvariantResources.xaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);
        WriteFile("Fluent/Strings/InvariantResources.xaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <x:String x:Key="StringCut">Cut</x:String>
            </ResourceDictionary>
            """);

        string simple = Path.Combine(_root, "Simple");
        ThemeInventory unlinked = ThemeInventory.Build(Path.Combine(simple, "SimpleTheme.xaml"), simple);
        UnresolvedInclude missing = Assert.Single(unlinked.Unresolved);
        Assert.Equal("/Strings/InvariantResources.xaml", missing.Source);

        ThemeInventory linked = ThemeInventory.Build(new ThemeSource(Path.Combine(simple, "SimpleTheme.xaml"), simple)
        {
            Links = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/Strings/InvariantResources.xaml"] = Path.Combine(_root, "Fluent", "Strings", "InvariantResources.xaml"),
            },
        });
        Assert.Empty(linked.Unresolved);
        Assert.True(Assert.Single(linked.Variants).Defines("StringCut"));
    }

    [Fact]
    public void A_code_provider_supplies_keys_no_file_declares()
    {
        // Fluent-shaped: <accents:SystemAccentColors/> is a ResourceProvider in C#.
        WriteFile("Theme.xaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:accents="using:Fixture.Accents">
              <ResourceDictionary.MergedDictionaries>
                <accents:SystemAccentColors />
              </ResourceDictionary.MergedDictionaries>
              <SolidColorBrush x:Key="AccentBrush" Color="{DynamicResource SystemAccentColor}" />
            </ResourceDictionary>
            """);

        ThemeInventory unprovided = ThemeInventory.Build(Path.Combine(_root, "Theme.xaml"), _root);
        Assert.Equal("SystemAccentColors", Assert.Single(unprovided.Unresolved).Source);

        ThemeInventory provided = ThemeInventory.Build(new ThemeSource(Path.Combine(_root, "Theme.xaml"), _root)
        {
            Providers = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal)
            {
                ["SystemAccentColors"] = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["SystemAccentColor"] = "#FF0078D7",
                    ["SystemAccentColorLight1"] = "SystemAccentColor", // an alias
                    ["SystemAccentOpaque"] = null,                     // defined, not a colour
                },
            },
        });

        Assert.Empty(provided.Unresolved);
        VariantInventory only = Assert.Single(provided.Variants);
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x78, 0xD7), only.Resolve("AccentBrush"));
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x78, 0xD7), only.Resolve("SystemAccentColorLight1"));
        Assert.True(only.Defines("SystemAccentOpaque"));
        Assert.Null(only.Resolve("SystemAccentOpaque"));
        Assert.Null(only.Lookup("SystemAccentColor")!.File);
    }

    [Fact]
    public void Brush_opacity_folds_into_the_alpha_along_the_alias_chain()
    {
        // Semi-shaped: SemiColorText2 is Grey9 at Opacity 0.62.
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Grey9">#1C1F23</Color>
              <SolidColorBrush x:Key="Text0" Color="{StaticResource Grey9}" />
              <SolidColorBrush x:Key="Text2" Opacity="0.62" Color="{StaticResource Grey9}" />
              <SolidColorBrush x:Key="Text2Again" Opacity="0.5" Color="{StaticResource Text2}" />
              <SolidColorBrush x:Key="HalfLiteral" Opacity="0.5" Color="#FFFFFF" />
            </ResourceDictionary>
            """);

        VariantInventory only = Assert.Single(ThemeInventory.Build(Path.Combine(_root, "Theme.axaml"), _root).Variants);
        Assert.Equal(new AuditColor(0xFF, 0x1C, 0x1F, 0x23), only.Resolve("Text0"));
        Assert.Equal(new AuditColor(158, 0x1C, 0x1F, 0x23), only.Resolve("Text2"));       // round(255 * 0.62)
        Assert.Equal(new AuditColor(79, 0x1C, 0x1F, 0x23), only.Resolve("Text2Again"));   // round(255 * 0.31)
        Assert.Equal(new AuditColor(128, 0xFF, 0xFF, 0xFF), only.Resolve("HalfLiteral"));
    }

    [Fact]
    public void A_merged_dictionary_layers_over_the_theme_per_variant_and_keeps_the_definitions()
    {
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:fx="using:Fixture">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <Color x:Key="Ink">#111111</Color>
                </ResourceDictionary>
                <ResourceDictionary x:Key="{x:Static fx:Theme.Vivid}">
                  <Color x:Key="Ink">#FF00FF</Color>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);
        WriteFile("Compat.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <Color x:Key="Ink">#000000</Color>
                  <Thickness x:Key="Pad">2</Thickness>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(new ThemeSource(Path.Combine(_root, "Theme.axaml"), _root)
        {
            Merge = [Path.Combine(_root, "Compat.axaml")],
        });

        VariantInventory light = inventory.ForVariant("Light");
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x00, 0x00), light.Resolve("Ink")); // compat wins
        Assert.True(light.Defines("Pad"));
        Assert.EndsWith("Compat.axaml", light.Lookup("Pad")!.File);
        Assert.Equal("Thickness", light.Lookup("Pad")!.Element!.Name.LocalName);

        // The compat file touched only Light; Vivid keeps its own value and its key's namespaces.
        VariantInventory vivid = inventory.ForVariant("Vivid");
        Assert.Equal(new AuditColor(0xFF, 0xFF, 0x00, 0xFF), vivid.Resolve("Ink"));
        Assert.Equal("{x:Static fx:Theme.Vivid}", vivid.Key);
        Assert.Equal("using:Fixture", vivid.KeyNamespaces["fx"]);
        Assert.Equal("http://schemas.microsoft.com/winfx/2006/xaml", vivid.KeyNamespaces["x"]);
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
