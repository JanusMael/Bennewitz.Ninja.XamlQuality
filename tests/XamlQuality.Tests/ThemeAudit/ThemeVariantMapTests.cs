using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

public sealed class ThemeVariantMapTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-map-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Reads_include_variants_shared_roots_and_display_names_like_semi()
    {
        WriteFile("Index.axaml",
            """
            <Styles xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:semi="https://irihi.tech/semi">
              <Styles.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.ThemeDictionaries>
                    <ResourceInclude x:Key="Light" Source="/Themes/Light/_index.axaml" />
                    <ResourceInclude x:Key="Dark" Source="/Themes/Dark/_index.axaml" />
                    <ResourceInclude x:Key="{x:Static semi:SemiTheme.NightSky}" Source="/Themes/HighContrast/_index.axaml" />
                  </ResourceDictionary.ThemeDictionaries>
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceInclude Source="/Tokens/_index.axaml" />
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
              </Styles.Resources>
            </Styles>
            """);
        WriteFile("Themes/Light/_index.axaml", Empty);
        WriteFile("Themes/Dark/_index.axaml", Empty);
        WriteFile("Themes/HighContrast/_index.axaml", Empty);
        WriteFile("Tokens/_index.axaml", Empty);

        ThemeVariantMap map = ThemeVariantMapParser.Parse(Path.Combine(_root, "Index.axaml"), _root);

        Assert.Empty(map.Unresolved);
        Assert.Equal(["Light", "Dark", "NightSky"], map.Variants.Select(v => v.DisplayName).ToArray());
        Assert.All(map.Variants, v => Assert.False(v.Inline));
        Assert.Equal(
            Path.Combine("Themes", "Light", "_index.axaml"),
            Path.GetRelativePath(_root, Assert.Single(map.Variants.Single(v => v.Key == "Light").Roots)));

        // Shared roots are the entry file plus the top-level merged include.
        Assert.Equal(
            ["Index.axaml", Path.Combine("Tokens", "_index.axaml")],
            map.SharedRoots.Select(f => Path.GetRelativePath(_root, f)).ToArray());
    }

    [Fact]
    public void Marks_inline_variants_and_still_shares_the_entry()
    {
        WriteFile("Fluent.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <Color x:Key="Accent">#0078D4</Color>
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <Color x:Key="Accent">#4CC2FF</Color>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        ThemeVariantMap map = ThemeVariantMapParser.Parse(Path.Combine(_root, "Fluent.axaml"), _root);

        Assert.Equal(["Light", "Dark"], map.Variants.Select(v => v.Key).ToArray());
        Assert.All(map.Variants, v => Assert.True(v.Inline));
        Assert.All(map.Variants, v => Assert.Empty(v.Roots));
        Assert.Equal(["Fluent.axaml"], map.SharedRoots.Select(f => Path.GetRelativePath(_root, f)).ToArray());
    }

    [Fact]
    public void Records_an_unresolved_variant_include()
    {
        WriteFile("Index.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceInclude x:Key="Light" Source="/Themes/Missing.axaml" />
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);

        ThemeVariantMap map = ThemeVariantMapParser.Parse(Path.Combine(_root, "Index.axaml"), _root);

        UnresolvedInclude unresolved = Assert.Single(map.Unresolved);
        Assert.Equal("/Themes/Missing.axaml", unresolved.Source);
        Assert.Equal("file not found", unresolved.Reason);
        Assert.Empty(map.Variants.Single().Roots);
    }

    private const string Empty = """<ResourceDictionary xmlns="https://github.com/avaloniaui" />""";

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
