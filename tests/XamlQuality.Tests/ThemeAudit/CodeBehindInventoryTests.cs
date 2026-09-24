using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

/// <summary>
/// The Semi-shaped case the flat model could not handle: a shared include carries a nested
/// ThemeDictionaries that maps a code-behind palette dictionary to each variant, so the palette
/// keys must be attributed to the variant they are reached through — not merged into a shared
/// bucket where the last one wins.
/// </summary>
public sealed class CodeBehindInventoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-cb-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_code_behind_palette_selected_per_variant_in_a_shared_include_stays_per_variant()
    {
        WriteFile("Index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceInclude x:Key="Light" Source="/Themes/Light.axaml" />
                <ResourceInclude x:Key="Dark" Source="/Themes/Dark.axaml" />
              </ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="/Tokens/_index.axaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);
        WriteFile("Themes/Light.axaml", Brush("ButtonBg", "#FFFFFF"));
        WriteFile("Themes/Dark.axaml", Brush("ButtonBg", "#000000"));
        // The shared token index selects a code-behind palette per variant and includes a shared one.
        WriteFile("Tokens/_index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:local="using:Fix">
              <ResourceDictionary.ThemeDictionaries>
                <local:LightPalette x:Key="Light" />
                <local:DarkPalette x:Key="Dark" />
              </ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary.MergedDictionaries>
                <local:Shared />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);
        WritePalette("Tokens/LightPalette.axaml", "Fix.LightPalette", "#0064FA");
        WritePalette("Tokens/DarkPalette.axaml", "Fix.DarkPalette", "#54A9FF");
        WriteFile("Tokens/Shared.axaml",
            """
            <ResourceDictionary x:Class="Fix.Shared" xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="BaseInk">#111111</Color>
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Index.axaml"), _root);
        Assert.Empty(inventory.Unresolved);

        VariantInventory light = inventory.Variants.Single(v => v.DisplayName == "Light");
        VariantInventory dark = inventory.Variants.Single(v => v.DisplayName == "Dark");

        // The palette brush aliases a colour defined in the same code-behind palette, and each
        // variant keeps its own — the bug the flat model produced was both resolving to one value.
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x64, 0xFA), light.Resolve("Primary"));
        Assert.Equal(new AuditColor(0xFF, 0x54, 0xA9, 0xFF), dark.Resolve("Primary"));

        // The shared code-behind dictionary is a base key on both.
        Assert.Equal(new AuditColor(0xFF, 0x11, 0x11, 0x11), light.Resolve("BaseInk"));
        Assert.Equal(new AuditColor(0xFF, 0x11, 0x11, 0x11), dark.Resolve("BaseInk"));

        // The ResourceInclude variant control keys stay per variant too.
        Assert.Equal(new AuditColor(0xFF, 0xFF, 0xFF, 0xFF), light.Resolve("ButtonBg"));
        Assert.Equal(new AuditColor(0xFF, 0x00, 0x00, 0x00), dark.Resolve("ButtonBg"));
    }

    [Fact]
    public void An_unresolvable_code_behind_element_is_recorded()
    {
        WriteFile("Index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:local="using:Fix">
              <ResourceDictionary.MergedDictionaries>
                <local:NoSuchType />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);

        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Index.axaml"), _root);
        UnresolvedInclude unresolved = Assert.Single(inventory.Unresolved);
        Assert.Equal("NoSuchType", unresolved.Source);
        Assert.Equal("no x:Class type named NoSuchType", unresolved.Reason);
    }

    private static string Brush(string key, string color) =>
        $"""
        <ResourceDictionary xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="{key}" Color="{color}" />
        </ResourceDictionary>
        """;

    private void WritePalette(string relative, string className, string blue)
    {
        WriteFile(relative,
            $$"""
            <ResourceDictionary x:Class="{{className}}" xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Blue">{{blue}}</Color>
              <SolidColorBrush x:Key="Primary" Color="{StaticResource Blue}" />
            </ResourceDictionary>
            """);
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
