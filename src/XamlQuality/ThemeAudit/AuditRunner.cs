using System.Security.Cryptography;
using System.Text;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>One theme as audited: its inventory, the variants the report covers, its surfaces, and a digest of its files.</summary>
public sealed record ThemeTarget(
    ThemeConfig Config,
    ThemeInventory Inventory,
    IReadOnlyList<string> Variants,
    SurfaceMap Surfaces,
    string Digest);

/// <summary>One consumer as scanned: the files, the references, the keys it defines itself, its pairs, and a digest.</summary>
public sealed record ConsumerScan(
    ConsumerConfig Config,
    IReadOnlyList<string> Files,
    IReadOnlyList<ResourceReference> References,
    IReadOnlySet<string> OwnKeys,
    IReadOnlyList<ContrastPair> Pairs,
    string Digest);

/// <summary>What one consumer looks like under one theme.</summary>
public sealed record ConsumerThemeFindings(
    ConsumerScan Consumer,
    ThemeTarget Theme,
    IReadOnlyList<UndefinedKeyFinding> Undefined,
    IReadOnlyList<ContrastFinding> Contrast);

/// <summary>One compat dictionary as generated for the report (the <c>compat</c> command writes the file).</summary>
public sealed record CompatOutcome(CompatConfig Config, CompatMapping Mapping, CompatGeneration Generation);

/// <summary>Everything a run produced, in configuration order.</summary>
public sealed record AuditResult(
    AuditConfig Config,
    IReadOnlyList<ThemeTarget> Themes,
    IReadOnlyList<ConsumerScan> Consumers,
    IReadOnlyList<ConsumerThemeFindings> Findings,
    IReadOnlyList<CompatOutcome> Compat);

/// <summary>
/// Runs an audit: inventories every configured theme, scans every consumer, and pairs them up —
/// undefined keys per (consumer, theme, variant) and contrast per pair — in a form the report
/// renders and the drift test compares. Nothing is skipped quietly: a consumer directory that is
/// missing on this machine fails the run naming the candidates it tried.
/// </summary>
public static class AuditRunner
{
    /// <summary>Runs the whole configuration.</summary>
    /// <exception cref="AuditConfigException">A referenced file is missing or malformed.</exception>
    /// <exception cref="DirectoryNotFoundException">A consumer directory does not exist.</exception>
    /// <exception cref="InvalidDataException">A scanned file is not well-formed XML.</exception>
    public static AuditResult Run(AuditConfig config)
    {
        List<ThemeTarget> themes = config.Themes.Select(t => BuildTheme(config, t)).ToList();
        List<ConsumerScan> consumers = config.Consumers.Select(c => ScanConsumer(config, c)).ToList();

        List<ConsumerThemeFindings> findings = [];
        foreach (ConsumerScan consumer in consumers)
        {
            foreach (ThemeTarget theme in themes)
            {
                if (consumer.Config.Themes is { } only && !only.Contains(theme.Config.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                IReadOnlyList<UndefinedKeyFinding> undefined =
                    ThemeAuditFindings.UndefinedKeys(consumer.References, theme.Inventory, theme.Variants, consumer.OwnKeys);

                IReadOnlyList<ContrastFinding> contrast = [];
                if (consumer.Pairs.Count > 0)
                {
                    ThemeInventory? tokens = consumer.Config.Tokens is { } tokensConfig
                        ? ThemeInventory.Build(TokensSource(config, tokensConfig, theme.Config))
                        : null;
                    contrast = ContrastAudit.Score(consumer.Pairs, theme.Inventory, theme.Variants, tokens, theme.Surfaces);
                }

                findings.Add(new ConsumerThemeFindings(consumer, theme, undefined, contrast));
            }
        }

        List<CompatOutcome> compat = [];
        foreach (CompatConfig entry in config.Compat)
        {
            ThemeTarget from = themes.Single(t => t.Config.Name == entry.From);
            ThemeTarget to = themes.Single(t => t.Config.Name == entry.To);
            CompatMapping mapping = CompatMapping.Load(CompatMapping.Locate(config, entry.Mapping));
            compat.Add(new CompatOutcome(entry, mapping, CompatGenerator.Generate(from.Inventory, to.Inventory, mapping, entry.From, entry.To, entry.VariantKeys)));
        }

        return new AuditResult(config, themes, consumers, findings, compat);
    }

    /// <summary>The walker's inputs for a configured theme, paths made absolute.</summary>
    public static ThemeSource ToSource(AuditConfig config, ThemeConfig theme)
    {
        return new ThemeSource(config.Resolve(theme.Entry!), config.Resolve(theme.BaseDirectory!))
        {
            AssemblyName = theme.Assembly,
            Inheritance = theme.Inheritance,
            Links = theme.Links?.ToDictionary(kv => kv.Key, kv => config.Resolve(kv.Value), StringComparer.Ordinal),
            Providers = theme.Providers?.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, string?>)kv.Value,
                StringComparer.Ordinal),
            Merge = theme.Merge?.Select(config.Resolve).ToList(),
        };
    }

    /// <summary>Inventories one configured theme.</summary>
    public static ThemeTarget BuildTheme(AuditConfig config, ThemeConfig theme)
    {
        ThemeSource source = ToSource(config, theme);
        if (!File.Exists(source.EntryFile))
        {
            throw new AuditConfigException($"theme '{theme.Name}': entry file not found: {source.EntryFile}; fetch the reference sources first");
        }

        ThemeInventory inventory = ThemeInventory.Build(source);
        IReadOnlyList<string> variants = theme.Variants ?? inventory.Variants.Select(v => v.DisplayName).ToList();
        SurfaceMap surfaces = new(
            theme.Surfaces ?? new Dictionary<string, string>(StringComparer.Ordinal),
            theme.VariantSurfaces?.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<string, string>)kv.Value,
                StringComparer.Ordinal));

        return new ThemeTarget(theme, inventory, variants, surfaces, Digest(CommonRoot(inventory.Files, config.ConfigDirectory), inventory.Files));
    }

    private static ThemeSource TokensSource(AuditConfig config, TokensConfig tokens, ThemeConfig theme)
    {
        string entry = config.Resolve(tokens.Entry);
        string baseDirectory = tokens.BaseDirectory is not null
            ? config.Resolve(tokens.BaseDirectory)
            : Path.GetDirectoryName(entry) ?? config.ConfigDirectory;
        return new ThemeSource(entry, baseDirectory)
        {
            AssemblyName = tokens.Assembly,
            Inheritance = theme.Inheritance,
        };
    }

    private static ConsumerScan ScanConsumer(AuditConfig config, ConsumerConfig consumer)
    {
        List<string> files = [];
        List<ResourceReference> references = [];
        HashSet<string> own = new(StringComparer.Ordinal);
        List<string> excluded = consumer.Exclude?.Select(config.Resolve).ToList() ?? [];

        foreach (PathCandidates candidates in consumer.Paths)
        {
            string directory = candidates.ResolveDirectory(config);
            files.AddRange(XamlFiles.Enumerate(directory, excluded));
            references.AddRange(ResourceReferenceScanner.Scan(directory, excluded));
            own.UnionWith(ThemeDefinitionScanner.Scan(directory, excluded).Select(d => d.Key));
        }

        if (consumer.Tokens is { } tokens)
        {
            string entry = config.Resolve(tokens.Entry);
            if (!File.Exists(entry))
            {
                throw new AuditConfigException($"consumer '{consumer.Name}': tokens entry not found: {entry}");
            }

            own.UnionWith(ThemeDefinitionScanner.ScanFile(entry).Select(d => d.Key));
        }

        IReadOnlyList<ContrastPair> pairs = consumer.Contrast is { } pairsPath
            ? ContrastPairsFile.Load(config.Resolve(pairsPath))
            : [];

        return new ConsumerScan(consumer, files, references, own, pairs, Digest(CommonRoot(files, config.ConfigDirectory), files));
    }


    /// <summary>
    /// The deepest directory every one of <paramref name="files"/> sits under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>The digest must describe WHAT was audited, never WHERE it is checked out.</b> Rooting
    /// it at the configuration's own directory breaks that the moment a scanned path leaves the
    /// tree: the same bytes hash differently depending on whether a dependency was found as a
    /// sibling checkout or under a fetched reference directory. Measured — one project's audit
    /// produced <c>0ef898b7243a</c> on a developer machine and <c>f95218bc267e</c> in CI from 43
    /// byte-identical files, and the only difference was <c>../cl/Thing/src</c> against
    /// <c>reference/Thing/src</c>. It could never have passed in both places.
    /// </para>
    /// <para>
    /// ⚠ <b>Rebasing the root changes every digest once.</b> Committed reports must be regenerated
    /// after this, and that is the intended cost: the old values encoded a machine's directory
    /// layout, so they were never comparable across machines in the first place.
    /// </para>
    /// </remarks>
    /// <param name="files">The files whose shared directory is wanted.</param>
    /// <param name="fallback">Used when the set is empty or shares no directory — nothing to be
    /// relative to, and inventing one would be worse than saying so.</param>
    internal static string CommonRoot(IReadOnlyList<string> files, string fallback)
    {
        if (files.Count == 0)
        {
            return fallback;
        }

        string[] first = Path.GetFullPath(files[0]).Split(Path.DirectorySeparatorChar);
        int shared = first.Length - 1;

        foreach (string file in files.Skip(1))
        {
            string[] parts = Path.GetFullPath(file).Split(Path.DirectorySeparatorChar);
            int limit = Math.Min(shared, parts.Length - 1);
            int i = 0;
            while (i < limit && string.Equals(first[i], parts[i], StringComparison.Ordinal))
            {
                i++;
            }

            shared = i;
        }

        return shared <= 0 ? fallback : string.Join(Path.DirectorySeparatorChar, first.Take(shared));
    }

    /// <summary>
    /// A short content digest over files in order — relative path and bytes — so the report
    /// changes whenever a pin bump changes what was audited, and only then.
    /// </summary>
    public static string Digest(string root, IReadOnlyList<string> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..12];
    }
}
