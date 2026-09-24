using Bennewitz.Ninja.XamlQuality.ThemeAudit;

namespace XamlQuality.Tests.ThemeAudit;

public sealed class ThemeAuditFindingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("theme-find-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Flags_referenced_keys_no_variant_defines_and_respects_the_consumer_own_keys()
    {
        // A theme that defines an accent per variant but none of the System* family.
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="AccentBrush" Color="#2E7D32" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="AccentBrush" Color="#4CAF50" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);
        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Theme.axaml"), _root);

        List<ResourceReference> references =
        [
            new("c.axaml", "AccentBrush", ReferenceKind.Dynamic, 1),      // defined -> not a finding
            new("c.axaml", "SystemAccentColor", ReferenceKind.Dynamic, 2),// undefined -> invisible
            new("c.axaml", "SearchBg", ReferenceKind.Static, 3),          // undefined -> throws
            new("c.axaml", "OwnKey", ReferenceKind.Dynamic, 4),           // the consumer defines it itself
        ];
        HashSet<string> consumerOwn = new(StringComparer.Ordinal) { "OwnKey" };

        IReadOnlyList<UndefinedKeyFinding> findings = ThemeAuditFindings.UndefinedKeys(references, inventory, consumerOwn);

        // Two undefined keys, each on both variants: ordered by variant display name then key.
        Assert.Equal(
            [
                ("Dark", "SearchBg", ReferenceKind.Static),
                ("Dark", "SystemAccentColor", ReferenceKind.Dynamic),
                ("Light", "SearchBg", ReferenceKind.Static),
                ("Light", "SystemAccentColor", ReferenceKind.Dynamic),
            ],
            findings.Select(f => (f.DisplayName, f.Key, f.WorstKind)).ToArray());
    }

    [Fact]
    public void Static_reference_outranks_dynamic_for_the_same_key()
    {
        WriteFile("Theme.axaml",
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light" />
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """);
        ThemeInventory inventory = ThemeInventory.Build(Path.Combine(_root, "Theme.axaml"), _root);

        List<ResourceReference> references =
        [
            new("c.axaml", "Missing", ReferenceKind.Dynamic, 1),
            new("c.axaml", "Missing", ReferenceKind.Static, 2),
        ];

        UndefinedKeyFinding finding = Assert.Single(ThemeAuditFindings.UndefinedKeys(references, inventory));
        Assert.Equal("Missing", finding.Key);
        Assert.Equal(ReferenceKind.Static, finding.WorstKind);
    }

    private void WriteFile(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
