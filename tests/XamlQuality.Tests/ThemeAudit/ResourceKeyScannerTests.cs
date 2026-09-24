using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

public sealed class ResourceKeyScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-audit-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Finds_every_key_including_nested_and_multi_line_ones()
    {
        WriteFile("Themes/Light/Button.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="ButtonBackground" Color="#FFFFFF" />
              <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                  <Color
                    x:Key="ButtonForeground">#000000</Color>
                </ResourceDictionary>
              </ResourceDictionary.MergedDictionaries>
              <ControlTheme x:Key="{x:Type Button}" TargetType="Button" />
            </ResourceDictionary>
            """);

        IReadOnlyList<ResourceKey> keys = ResourceKeyScanner.Scan(_root);

        Assert.Equal(["ButtonBackground", "ButtonForeground", "{x:Type Button}"], keys.Select(k => k.Key));
        Assert.Equal(["SolidColorBrush", "Color", "ControlTheme"], keys.Select(k => k.ElementName));
        Assert.All(keys, k => Assert.True(k.Line > 0));
    }

    [Fact]
    public void Skips_build_output_and_orders_files_by_path()
    {
        WriteFile("b.axaml", Dictionary("Second"));
        WriteFile("a.xaml", Dictionary("First"));
        WriteFile("obj/generated.axaml", Dictionary("Ignored"));
        WriteFile("bin/Debug/copy.axaml", Dictionary("Ignored"));

        IReadOnlyList<ResourceKey> keys = ResourceKeyScanner.Scan(_root);

        Assert.Equal(["First", "Second"], keys.Select(k => k.Key));
    }

    [Fact]
    public void Malformed_xml_is_an_error_not_an_undercount()
    {
        WriteFile("broken.axaml", "<ResourceDictionary><SolidColorBrush x:Key=\"Unclosed\" />");

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => ResourceKeyScanner.Scan(_root));

        Assert.Contains("broken.axaml", ex.Message, StringComparison.Ordinal);
    }

    private static string Dictionary(string key)
    {
        return $"""
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="{key}" Color="#123456" />
            </ResourceDictionary>
            """;
    }

    private void WriteFile(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
