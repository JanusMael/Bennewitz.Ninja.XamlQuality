using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit.Tests;

public sealed class ThemeDefinitionScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-def-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Attributes_keys_to_their_theme_variant_and_reads_colour_values()
    {
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <Color x:Key="PageBackground">#FFFFFF</Color>
                  <SolidColorBrush x:Key="Accent" Color="#2E7D32" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <Color x:Key="PageBackground">#1E1E1E</Color>
                  <SolidColorBrush x:Key="Accent" Color="{DynamicResource AccentColor}" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
              <Thickness x:Key="StandardPadding">4</Thickness>
            </ResourceDictionary>
            """);

        IReadOnlyList<DefinedResource> defined = ThemeDefinitionScanner.Scan(_root);

        DefinedResource lightBackground = Single(defined, "Light", "PageBackground");
        Assert.Equal(new ResourceValue.ColorLiteral(new AuditColor(0xFF, 0xFF, 0xFF, 0xFF)), lightBackground.Value);

        DefinedResource lightAccent = Single(defined, "Light", "Accent");
        Assert.Equal(new ResourceValue.ColorLiteral(new AuditColor(0xFF, 0x2E, 0x7D, 0x32)), lightAccent.Value);

        DefinedResource darkBackground = Single(defined, "Dark", "PageBackground");
        Assert.Equal(new ResourceValue.ColorLiteral(new AuditColor(0xFF, 0x1E, 0x1E, 0x1E)), darkBackground.Value);

        DefinedResource darkAccent = Single(defined, "Dark", "Accent");
        Assert.Equal(new ResourceValue.Alias("AccentColor"), darkAccent.Value);

        // A base resource (outside ThemeDictionaries) has no variant and is not colour-scored.
        DefinedResource padding = Single(defined, null, "StandardPadding");
        Assert.IsType<ResourceValue.Opaque>(padding.Value);
    }

    [Fact]
    public void The_variant_container_dictionaries_are_not_themselves_defined_keys()
    {
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <Color x:Key="Only">#000000</Color>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        IReadOnlyList<DefinedResource> defined = ThemeDefinitionScanner.Scan(_root);
        DefinedResource only = Assert.Single(defined);
        Assert.Equal("Only", only.Key);
        Assert.Equal("Light", only.Variant);
    }

    [Fact]
    public void Reads_a_static_resource_alias_and_a_type_keyed_control_theme()
    {
        WriteFile("Aliases.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StaticResource x:Key="ButtonBackground" ResourceKey="Accent" />
              <ControlTheme x:Key="{x:Type Button}" TargetType="Button" />
            </ResourceDictionary>
            """);

        IReadOnlyList<DefinedResource> defined = ThemeDefinitionScanner.Scan(_root);

        Assert.Equal(new ResourceValue.Alias("Accent"), Single(defined, null, "ButtonBackground").Value);
        DefinedResource controlTheme = Single(defined, null, "{x:Type Button}");
        Assert.IsType<ResourceValue.Opaque>(controlTheme.Value);
    }

    private static DefinedResource Single(IReadOnlyList<DefinedResource> defined, string? variant, string key)
    {
        return defined.Single(d => d.Variant == variant && d.Key == key);
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
