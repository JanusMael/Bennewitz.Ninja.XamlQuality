using Bennewitz.Ninja.XamlQuality.ThemeAudit;
using Xunit;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit.Tests;

/// <summary>
/// The audit digest describes what was audited, never where it is checked out.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The defect needs two locations in one test to be visible at all.</b> The digest was only
/// ever exercised against one checkout, so "same content, same digest" was true by construction
/// and never asserted. Measured before the fix: one project's audit produced <c>0ef898b7243a</c>
/// on a developer machine, where a dependency was a sibling checkout reached as
/// <c>../cl/Thing/src</c>, and <c>f95218bc267e</c> in CI, where the same 43 byte-identical files
/// were fetched to <c>reference/Thing/src</c>. Three days and three dead hypotheses passed before
/// the two reports were put side by side.
/// </para>
/// <para>
/// ⛔ <b>This runs the whole audit, not the digest helper.</b> A test of the helper alone passes
/// while a caller hands it the wrong root — which is exactly what the defect was.
/// </para>
/// </remarks>
public sealed class DigestLocationTests : IDisposable
{
    private readonly string _stem = Directory.CreateTempSubdirectory("xq-digest-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_stem)) { Directory.Delete(_stem, recursive: true); }
    }

    /// <summary>
    /// Builds one audit whose consumer is found either beside the config or outside it, mirroring
    /// a dependency that is a sibling checkout on one machine and a fetched copy on another.
    /// </summary>
    /// <param name="outside">Put the scanned files outside the config directory.</param>
    private string Layout(string name, bool outside)
    {
        string root = Path.Combine(_stem, name);
        string cfg = Path.Combine(root, "cfg");
        Directory.CreateDirectory(cfg);

        string host = Path.Combine(cfg, "host");
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(host, "Host.axaml"),
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <SolidColorBrush x:Key="HostText">#FF000000</SolidColorBrush>
            </ResourceDictionary>
            """);

        // The two spellings the defect turns on. Only one of them exists in any one layout, and
        // the first that does is the one the audit resolves — as it does for a real dependency.
        string scanned = outside
            ? Path.Combine(root, "sibling", "src")
            : Path.Combine(cfg, "reference", "src");
        Directory.CreateDirectory(Path.Combine(scanned, "Themes"));
        File.WriteAllText(Path.Combine(scanned, "Themes", "App.axaml"),
            """
            <Styles xmlns="https://github.com/avaloniaui">
              <Style Selector="TextBlock"><Setter Property="Foreground" Value="{DynamicResource HostText}" /></Style>
            </Styles>
            """);

        File.WriteAllText(Path.Combine(cfg, "theme-audit.json"),
            """
            {
              "title": "Location fixture",
              "report": "docs/theme-audit.md",
              "themes": [
                { "name": "Host", "entry": "host/Host.axaml", "baseDirectory": "host", "variants": ["Light"] }
              ],
              "consumers": [
                { "name": "Dep", "paths": [["../sibling/src", "reference/src"]] }
              ]
            }
            """);

        return Path.Combine(cfg, "theme-audit.json");
    }

    private static ConsumerScan Dep(string configPath) =>
        AuditRunner.Run(AuditConfig.Load(configPath)).Consumers.Single(c => c.Config.Name == "Dep");

    [Fact]
    public void TheSameContentInTwoLocations_HashesTheSame()
    {
        ConsumerScan sibling = Dep(Layout("a", outside: true));
        ConsumerScan reference = Dep(Layout("b", outside: false));

        // Guard the fixture itself: if neither layout found the files, both digests would be the
        // digest of nothing and this would pass while proving absolutely nothing.
        Assert.Single(sibling.Files);
        Assert.Single(reference.Files);
        Assert.NotEqual(sibling.Files[0], reference.Files[0]);

        Assert.Equal(sibling.Digest, reference.Digest);
    }

    [Fact]
    public void AContentChange_StillMovesTheDigest()
    {
        string config = Layout("c", outside: true);
        string before = Dep(config).Digest;

        string file = Dep(config).Files[0];
        File.WriteAllText(file, File.ReadAllText(file).Replace("TextBlock", "Border", StringComparison.Ordinal));

        Assert.NotEqual(before, Dep(config).Digest);
    }

    /// <summary>
    /// ⚠ A rename is a change to what was audited. Written so the file's CONTENT and its position
    /// in the ordering both stay put, leaving the path as the only difference — an earlier version
    /// of this test renamed across a sort boundary and passed even with path hashing removed.
    /// </summary>
    [Fact]
    public void ARenameThatDoesNotReorder_StillMovesTheDigest()
    {
        string config = Layout("d", outside: true);
        string before = Dep(config).Digest;

        string file = Dep(config).Files[0];
        File.Move(file, Path.Combine(Path.GetDirectoryName(file)!, "Bpp.axaml"));

        Assert.NotEqual(before, Dep(config).Digest);
    }

    [Fact]
    public void AnEmptySet_FallsBackRatherThanInventingARoot()
    {
        Assert.Equal("fallback", AuditRunner.CommonRoot([], "fallback"));
    }
}
