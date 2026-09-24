using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

public sealed class ResourceIncludeResolverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-inc-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Follows_relative_rooted_and_avares_includes_dedupes_and_breaks_cycles()
    {
        WriteFile("entry.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui">
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="Sub/Child.axaml" />
                <ResourceInclude Source="/Rooted.axaml" />
                <ResourceInclude Source="avares://OtherAssembly/Foo.axaml" />
                <ResourceInclude Source="Missing.axaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);
        WriteFile("Sub/Child.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui">
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="../Rooted.axaml" />
                <ResourceInclude Source="/entry.axaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);
        WriteFile("Rooted.axaml", """<ResourceDictionary xmlns="https://github.com/avaloniaui" />""");

        IncludeResolution resolution = ResourceIncludeResolver.Resolve(Path.Combine(_root, "entry.axaml"), _root);

        Assert.Equal(
            ["entry.axaml", Path.Combine("Sub", "Child.axaml"), "Rooted.axaml"],
            resolution.Files.Select(f => Path.GetRelativePath(_root, f)).ToArray());

        Assert.Equal(
            [
                ("avares://OtherAssembly/Foo.axaml", "cross-assembly: OtherAssembly"),
                ("Missing.axaml", "file not found"),
            ],
            resolution.Unresolved.Select(u => (u.Source, u.Reason)).ToArray());
    }

    [Fact]
    public void An_avares_uri_into_this_assembly_resolves()
    {
        WriteFile("entry.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui">
              <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="avares://Semi.Avalonia/Tokens/Colors.axaml" />
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);
        WriteFile("Tokens/Colors.axaml", """<ResourceDictionary xmlns="https://github.com/avaloniaui" />""");

        IncludeResolution resolution = ResourceIncludeResolver.Resolve(
            Path.Combine(_root, "entry.axaml"), _root, assemblyName: "Semi.Avalonia");

        Assert.Equal(
            ["entry.axaml", Path.Combine("Tokens", "Colors.axaml")],
            resolution.Files.Select(f => Path.GetRelativePath(_root, f)).ToArray());
        Assert.Empty(resolution.Unresolved);
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
