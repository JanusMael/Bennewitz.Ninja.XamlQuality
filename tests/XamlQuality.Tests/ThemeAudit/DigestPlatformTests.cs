using Bennewitz.Ninja.XamlQuality.ThemeAudit;
using Xunit;

namespace XamlQuality.Tests.ThemeAudit;

/// <summary>
/// The audit digest is the same on every platform: a checkout's line endings and its path separator
/// are how it was made, not what was audited.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Found on a consumer's Windows runner.</b> Its committed report matched Linux and macOS, and on
/// Windows every row whose files were cloned there moved: git writes LF as CRLF in a Windows
/// checkout, and the digest hashed raw bytes. The one-file row's two values were the digests of one
/// file with LF and with CRLF line endings.
/// </para>
/// <para>
/// ⛔ <b>These run the whole audit</b>, as <see cref="DigestLocationTests"/> does, because a test of
/// the digest helper alone passes while a caller hands it something else.
/// </para>
/// </remarks>
public sealed class DigestPlatformTests : IDisposable
{
    private readonly string _stem = Directory.CreateTempSubdirectory("xq-digest-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_stem)) { Directory.Delete(_stem, recursive: true); }
    }

    private const string Theme =
        """
        <ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="HostText">#FF000000</SolidColorBrush>
        </ResourceDictionary>
        """;

    private const string View =
        """
        <Styles xmlns="https://github.com/avaloniaui">
          <Style Selector="TextBlock"><Setter Property="Foreground" Value="{DynamicResource HostText}" /></Style>
        </Styles>
        """;

    /// <summary>
    /// One audit of a theme and a consumer, every file written with <paramref name="newline"/>, and the
    /// consumer's files at <paramref name="views"/> below its directory.
    /// </summary>
    private string Layout(string name, string newline, params string[] views)
    {
        string cfg = Path.Combine(_stem, name);
        Directory.CreateDirectory(Path.Combine(cfg, "host"));
        File.WriteAllText(Path.Combine(cfg, "host", "Host.axaml"), Theme.ReplaceLineEndings(newline));

        foreach (string view in views)
        {
            string path = Path.Combine(cfg, "src", view);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, View.ReplaceLineEndings(newline));
        }

        File.WriteAllText(Path.Combine(cfg, "theme-audit.json"),
            """
            {
              "title": "Platform fixture",
              "report": "docs/theme-audit.md",
              "themes": [
                { "name": "Host", "entry": "host/Host.axaml", "baseDirectory": "host", "variants": ["Light"] }
              ],
              "consumers": [
                { "name": "Dep", "paths": [["src"]] }
              ]
            }
            """);

        return Path.Combine(cfg, "theme-audit.json");
    }

    private static AuditResult Run(string configPath) => AuditRunner.Run(AuditConfig.Load(configPath));

    [Fact]
    public void ACrlfCheckout_HashesAsAnLfOne()
    {
        AuditResult lf = Run(Layout("lf", "\n", "App.axaml"));
        AuditResult crlf = Run(Layout("crlf", "\r\n", "App.axaml"));

        // Guard the fixture: without a CR in the one layout, the two would agree by construction.
        Assert.Contains((byte)'\r', File.ReadAllBytes(crlf.Consumers.Single().Files.Single()));
        Assert.DoesNotContain((byte)'\r', File.ReadAllBytes(lf.Consumers.Single().Files.Single()));

        Assert.Equal(lf.Consumers.Single().Digest, crlf.Consumers.Single().Digest);
        Assert.Equal(lf.Themes.Single().Digest, crlf.Themes.Single().Digest);
    }

    /// <summary>
    /// ⭐ No digest made from LF checkouts moves, and that is every committed report generated on
    /// Linux or macOS. The values are this fixture's digests from before CRLF was read as LF.
    /// </summary>
    [Fact]
    public void AnLfCheckout_HashesAsItAlwaysHas()
    {
        AuditResult result = Run(Layout("pinned", "\n", "App.axaml"));

        Assert.Equal("70bf9ad2be59", result.Consumers.Single().Digest);
        Assert.Equal("f68bec5a4cce", result.Themes.Single().Digest);
    }

    /// <summary>⚠ A lone CR is content, not a line ending, so adding one still moves the digest.</summary>
    [Fact]
    public void ALoneCarriageReturn_StillMovesTheDigest()
    {
        string config = Layout("cr", "\n", "App.axaml");
        string before = Run(config).Consumers.Single().Digest;

        string file = Run(config).Consumers.Single().Files.Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("TextBlock", "Text\rBlock", StringComparison.Ordinal));

        Assert.NotEqual(before, Run(config).Consumers.Single().Digest);
    }

    /// <summary>
    /// ⛔ A consumer's files are read in the order of their paths below its directory, written with
    /// '/'. An ordinal sort of Windows paths put <c>ControlsExtra\</c> before <c>Controls\</c>,
    /// because '\' sorts after capitals where '/' sorts before them, and the digest moved with it.
    /// </summary>
    [Fact]
    public void AConsumersFiles_AreOrderedByTheirPathWithForwardSlashes()
    {
        string config = Layout("order", "\n", Path.Combine("ControlsExtra", "Y.axaml"), Path.Combine("Controls", "X.axaml"));
        string root = Path.Combine(Path.GetDirectoryName(config)!, "src");

        string[] expected = ["Controls/X.axaml", "ControlsExtra/Y.axaml"];
        Assert.Equal(expected, Run(config).Consumers.Single().Files.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')));
    }
}
