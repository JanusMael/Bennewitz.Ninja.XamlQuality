using System.Reflection;
namespace Bennewitz.Ninja.XamlQuality;

/// <summary>The markup a scan covers.</summary>
/// <remarks>
/// <para>
/// ⭐ <b>This is the seam that made the rules extractable at all.</b> Every guard these rules came
/// from found its own scan root by walking up until it saw a particular <c>.slnx</c>, then scanned
/// <c>src</c>. Both facts belong to one repository, not to a rule — so they moved here, where a
/// consumer states them once.
/// </para>
/// <para>
/// ⚠ <b>A value handed to rules, never ambient state.</b> Two scans with different roots must be
/// able to run in one process without seeing each other.
/// </para>
/// </remarks>
public sealed class XamlScanContext
{
    /// <summary>Extensions treated as XAML markup.</summary>
    /// <remarks>
    /// <c>.axaml</c> is Avalonia's; <c>.xaml</c> is everyone else's. Both, because nothing in these
    /// rules is Avalonia-specific and excluding <c>.xaml</c> would narrow the library for no reason.
    /// </remarks>
    public static IReadOnlyList<string> MarkupExtensions { get; } = [".axaml", ".xaml"];

    private XamlScanContext(string root, IReadOnlyList<XamlFile> files, IReadOnlyList<Assembly> assemblies)
    {
        Root = root;
        Files = files;
        Assemblies = assemblies;
    }

    /// <summary>The directory the scan was rooted at; <see cref="XamlFile.RelativePath"/> is relative to it.</summary>
    public string Root { get; }

    /// <summary>Every markup file in the scan, read once.</summary>
    public IReadOnlyList<XamlFile> Files { get; }

    /// <summary>
    /// Files that parsed, for rules that need structure.
    /// ⚠ Rules that care about unparseable markup read <see cref="Files"/> and check
    /// <see cref="XamlFile.ParseError"/>; this deliberately hides them rather than pretending
    /// they are clean.
    /// </summary>
    public IEnumerable<XamlFile> ParsedFiles => Files.Where(f => f.Document is not null);

    /// <summary>
    /// Assemblies whose compiled types the scan may consult; empty unless a consumer supplies them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Some contracts have two halves and only one of them is markup.</b> A control looks up
    /// its template parts by name in code and a theme declares them in markup; neither half alone
    /// says whether they agree. A rule that reads only markup cannot see the question, and one that
    /// reads only types cannot see the answer.
    /// </para>
    /// <para>
    /// ⚠ <b>Still no framework dependency.</b> These are whatever assemblies the consumer hands
    /// over, read through <see cref="System.Reflection"/> — nothing here knows what Avalonia, WPF or
    /// MAUI is, and a rule that needs types and is given none must report zero <c>Inspected</c>
    /// rather than zero findings.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Assembly> Assemblies { get; } = [];

    /// <summary>The same scan, with <paramref name="assemblies"/> available to rules that need types.</summary>
    /// <remarks>
    /// Returns a new context rather than mutating this one: a scan handed to two rules must not
    /// change under the second because the first asked for something.
    /// </remarks>
    public XamlScanContext WithAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return new XamlScanContext(Root, Files, [.. assemblies]);
    }

    /// <summary>Read every markup file under <paramref name="root"/>.</summary>
    /// <param name="root">Directory to scan, recursively.</param>
    /// <param name="excludedDirectories">
    /// Directory names skipped anywhere in the tree. Defaults to <c>bin</c> and <c>obj</c>.
    /// <para>
    /// ⛔ <b>Not skipping these is not a performance question.</b> A build copies markup into
    /// <c>obj</c>, so a scan that includes them reports every violation twice and — worse — keeps
    /// reporting one after the source is fixed, until someone cleans. Every guard this library
    /// replaced had its own copy of this exclusion, and that is how it came to be here.
    /// </para>
    /// </param>
    public static XamlScanContext Load(string root, IEnumerable<string>? excludedDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Scan root not found: '{full}'.");
        }

        HashSet<string> excluded = new(
            excludedDirectories ?? ["bin", "obj"],
            StringComparer.OrdinalIgnoreCase);

        List<XamlFile> files = [];

        foreach (string path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            if (!MarkupExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(full, path).Replace('\\', '/');

            if (relative.Split('/').Any(excluded.Contains))
            {
                continue;
            }

            files.Add(new XamlFile(path, relative, File.ReadAllText(path)));
        }

        // Ordered so findings are stable run to run: an unordered scan makes a diff of two
        // reports unreadable, and makes a baseline file churn for no reason.
        files.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        return new XamlScanContext(full, files, []);
    }
}
