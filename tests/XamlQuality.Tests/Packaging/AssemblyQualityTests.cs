using System.Reflection;
using System.Xml.Linq;
using Bennewitz.Ninja.AssemblyQuality;
using Bennewitz.Ninja.AssemblyQuality.Rules;
using Bennewitz.Ninja.XamlQuality;

namespace XamlQuality.Tests.Packaging;

/// <summary>
/// The family's own assembly rules, <c>Bennewitz.Ninja.AssemblyQuality</c>, run over every assembly
/// this repository ships: the rules library and the <c>theme-audit</c> tool.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>A rule that exists but is not run is indistinguishable from no rule.</b> AppServices'
/// 2026.3.923 shipped AQ1001 and AQ1004 violations the day after those rules were published,
/// because nothing there ran them. Nothing here ran them either, until this class.
/// </para>
/// <para>
/// ⭐ <b>AQ1003 is the first mechanical guard on this library's oldest promise.</b> "No
/// dependencies, Avalonia included" was held by the csproj alone. A direct reference to a UI
/// framework, a test framework, the CLI's parser or another family package now fails here.
/// </para>
/// <para>
/// ⚠ <b>Zero findings means nothing unless something was inspected.</b> The scan holds every project
/// under <c>src</c>, found by the assembly name its csproj declares, because the tool ships as
/// <c>ThemeAudit.dll</c> rather than under its project's name. After that, an inspected count of
/// zero is accepted only where zero is true, and that case says why.
/// </para>
/// </remarks>
public sealed class AssemblyQualityTests
{
    /// <summary>What no shipped assembly may reference directly, by simple-name prefix.</summary>
    private static readonly string[] ForbiddenEverywhere =
    [
        // UI frameworks. The rules read markup as text or XML, which is what lets one library serve
        // Avalonia, WPF and MAUI markup alike; a reference to any of them would end that.
        "Avalonia", "Microsoft.Maui", "PresentationCore", "PresentationFramework", "System.Xaml", "WindowsBase",

        // Test frameworks. A rule reports; the consumer chooses the runner.
        "Microsoft.Testing", "Microsoft.VisualStudio.TestPlatform", "MSTest", "nunit", "NUnit", "xunit",

        // The rest of the family, AssemblyQuality included: it is this repository's test dependency,
        // never its product's.
        "AgentForge", "AppServices", "AssemblyQuality", "ClaudeForge", "DiffView", "LayeredEditors", "ScopedEditors",
    ];

    /// <summary>What the library may not reference although the tool may: the tool's own parser.</summary>
    private static readonly string[] ForbiddenInTheLibraryOnly = ["System.CommandLine"];

    private static readonly string LibraryName = typeof(XamlScanContext).Assembly.GetName().Name!;
    private static readonly string[] AssemblyNames = LoadAssemblyNames();
    private static readonly Assembly[] Shipped = [.. AssemblyNames.Select(LoadFromOutput)];

    [Fact]
    public void Every_shipped_assembly_is_in_the_scan()
    {
        Assert.NotEmpty(AssemblyNames);
        Assert.Contains(LibraryName, AssemblyNames);
        Assert.Equal(AssemblyNames.Length, Shipped.Length);
    }

    [Fact]
    public void AQ1001_no_public_method_takes_a_defaulted_cancellation_token()
    {
        AssemblyRuleResult result = new CancellationTokenRule().Analyze(AssemblyScanContext.Of(Shipped));

        Assert.Empty(result.Findings);

        // ⓘ Zero inspected is the truth here rather than a scan that missed: the rules and the audit
        // are synchronous, so nothing public takes a token at all. It is asserted so that the first
        // token to arrive gets looked at, instead of quietly raising a count nobody reads.
        Assert.False(result.Inspected > 0, $"AQ1001 now inspects {result.Inspected} cancellation "
            + "token(s), where nothing public took one when this test was written. Confirm each is "
            + "required rather than defaulted, then assert Inspected > 0 instead.");
    }

    [Fact]
    public void AQ1002_no_leak_prone_type_appears_in_the_public_surface()
    {
        AssemblyRuleResult result = new SurfaceLeakRule().Analyze(AssemblyScanContext.Of(Shipped));

        Assert.Empty(result.Findings);
        Assert.True(result.Inspected > 0, "AQ1002 inspected no public members, so it proved nothing.");
    }

    [Fact]
    public void AQ1003_no_assembly_references_a_framework_or_another_family_package()
    {
        List<string> findings = [];

        foreach (Assembly assembly in Shipped)
        {
            string name = assembly.GetName().Name!;
            AssemblyRuleResult result =
                new ForbiddenReferenceRule(ForbiddenFor(name)).Analyze(AssemblyScanContext.Of(assembly));

            Assert.True(result.Inspected > 0, $"AQ1003 inspected no references of {name}.");
            findings.AddRange(result.Findings.Select(f => f.ToString()));
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void AQ1004_no_namespace_segment_shadows_a_referenced_root()
    {
        AssemblyRuleResult result = new NamespaceShadowRule().Analyze(AssemblyScanContext.Of(Shipped));

        Assert.Empty(result.Findings);
        Assert.True(result.Inspected > 0, "AQ1004 inspected no namespaces, so it proved nothing.");
    }

    private static string[] ForbiddenFor(string assemblyName)
    {
        if (assemblyName == LibraryName)
        {
            return [.. ForbiddenEverywhere, .. ForbiddenInTheLibraryOnly];
        }

        return ForbiddenEverywhere;
    }

    private static Assembly LoadFromOutput(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        Assert.True(File.Exists(path), $"{name}.dll is not in the test output, so it cannot be scanned.");
        return Assembly.LoadFrom(path);
    }

    /// <summary>
    /// The assembly name of every project under <c>src</c>: the <c>AssemblyName</c> its csproj
    /// declares, or the project's file name when it declares none.
    /// </summary>
    private static string[] LoadAssemblyNames() =>
    [
        .. Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(AssemblyNameOf)
            .Order(StringComparer.Ordinal),
    ];

    private static string AssemblyNameOf(string projectPath)
    {
        string? declared = XDocument.Load(projectPath)
            .Descendants("AssemblyName")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        return declared ?? Path.GetFileNameWithoutExtension(projectPath);
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding the solution, so the tests do not
    /// depend on the working directory a runner happens to choose.
    /// </summary>
    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XamlQuality.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
