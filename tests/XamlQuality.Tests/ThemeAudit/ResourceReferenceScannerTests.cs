using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit.Tests;

public sealed class ResourceReferenceScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-refs-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Finds_dynamic_and_static_references_in_attributes_and_elements()
    {
        WriteFile("Panel.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="A" Color="{DynamicResource SystemAccentColor}" />
              <Setter Property="Background" Value="{StaticResource SearchPanelBackgroundBrush}" />
              <StaticResource ResourceKey="SharedThing" />
              <Border Background="{DynamicResource ResourceKey=ThemeBackgroundColor}" />
            </ResourceDictionary>
            """);

        IReadOnlyList<ResourceReference> refs = ResourceReferenceScanner.Scan(_root);

        Assert.Equal(
            [
                ("SystemAccentColor", ReferenceKind.Dynamic),
                ("SearchPanelBackgroundBrush", ReferenceKind.Static),
                ("SharedThing", ReferenceKind.Static),
                ("ThemeBackgroundColor", ReferenceKind.Dynamic),
            ],
            refs.Select(r => (r.Key, r.Kind)).ToArray());
    }

    [Fact]
    public void Captures_a_nested_type_key_whole()
    {
        WriteFile("Based.axaml",
            """
            <Styles xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style Selector="ToggleButton" BasedOn="{StaticResource {x:Type ToggleButton}}" />
            </Styles>
            """);

        ResourceReference reference = Assert.Single(ResourceReferenceScanner.Scan(_root));
        Assert.Equal("{x:Type ToggleButton}", reference.Key);
        Assert.Equal(ReferenceKind.Static, reference.Kind);
    }

    [Fact]
    public void Ignores_other_markup_extensions()
    {
        WriteFile("Bindings.axaml",
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <TextBlock Text="{Binding Title}"
                         Foreground="{TemplateBinding Foreground}"
                         Tag="{x:Static local:Strings.Name}" />
            </UserControl>
            """);

        Assert.Empty(ResourceReferenceScanner.Scan(_root));
    }

    [Fact]
    public void Finds_two_references_in_one_value()
    {
        // A pen or gradient value can name two resources in the same attribute.
        Assert.Equal(
            [
                (ReferenceKind.Dynamic, "First"),
                (ReferenceKind.Dynamic, "Second"),
            ],
            ResourceReferenceScanner.ExtractMarkupReferences("{DynamicResource First} {DynamicResource Second}").ToArray());
    }

    [Fact]
    public void Extracts_nothing_from_a_plain_value()
    {
        Assert.Empty(ResourceReferenceScanner.ExtractMarkupReferences("#FF00FF"));
        Assert.Empty(ResourceReferenceScanner.ExtractMarkupReferences("{Binding Foo}"));
        Assert.Empty(ResourceReferenceScanner.ExtractMarkupReferences("{DynamicResourceExtension Foo}"));
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
