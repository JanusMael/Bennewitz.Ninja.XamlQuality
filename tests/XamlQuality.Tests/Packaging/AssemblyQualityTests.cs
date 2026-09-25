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
/// 2026.3.923 shipped BNAQ1001 and BNAQ1004 violations the day after those rules were published,
/// because nothing there ran them. Nothing here ran them either, until this class.
/// </para>
/// <para>
/// ⭐ <b>BNAQ1003 is the first mechanical guard on this library's oldest promise.</b> "No
/// dependencies, Avalonia included" was held by the csproj alone. A direct reference to a UI
/// framework, a test framework, the CLI's parser or another family package now fails here.
/// </para>
/// <para>
/// ⚠ <b>Zero findings means nothing unless something was inspected and nothing was skipped.</b> The
/// scan holds every project under <c>src</c>, found by the assembly name its csproj declares, because
/// the tool ships as <c>ThemeAudit.dll</c> rather than under its project's name. After that, an
/// inspected count of zero is accepted only where zero is true, and that case says why. And a rule
/// names in <see cref="AssemblyRuleResult.Skipped"/> what it could not examine, such as a type whose
/// signatures name an assembly that would not load, so each test asserts it empty: a skip makes the
/// answer incomplete, and an incomplete answer is not a clean one.
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
    public void BNAQ1001_no_public_method_takes_a_defaulted_cancellation_token()
    {
        AssemblyRuleResult result = new CancellationTokenRule().Analyze(AssemblyScanContext.Of(Shipped));

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);

        // ⓘ Zero inspected is the truth here rather than a scan that missed: the rules and the audit
        // are synchronous, so nothing public takes a token at all. It is asserted so that the first
        // token to arrive gets looked at, instead of quietly raising a count nobody reads.
        Assert.False(result.Inspected > 0, $"BNAQ1001 now inspects {result.Inspected} cancellation "
            + "token(s), where nothing public took one when this test was written. Confirm each is "
            + "required rather than defaulted, then assert Inspected > 0 instead.");
    }

    [Fact]
    public void BNAQ1002_no_leak_prone_type_appears_in_the_public_surface()
    {
        AssemblyRuleResult result = new SurfaceLeakRule().Analyze(AssemblyScanContext.Of(Shipped));

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);

        // ⓘ BNAQ1002 counts only where a leak is possible: an assembly whose references export no
        // leak-prone namespace contributes nothing. The library references System.Text.Json, which
        // exports System.Text.Json.Nodes, so its public surface is counted.
        Assert.True(result.Inspected > 0, "BNAQ1002 inspected no public members, so it proved nothing. "
            + "It counts only where a leak is possible: if the library no longer references "
            + "System.Text.Json, scope the rule to what the shipped assemblies do reference with "
            + "SurfaceLeakRule.Only rather than accepting the zero.");
    }

    [Fact]
    public void BNAQ1003_no_assembly_references_a_framework_or_another_family_package()
    {
        List<string> findings = [];
        List<string> skipped = [];

        foreach (Assembly assembly in Shipped)
        {
            string name = assembly.GetName().Name!;
            AssemblyRuleResult result =
                new ForbiddenReferenceRule(ForbiddenFor(name)).Analyze(AssemblyScanContext.Of(assembly));

            Assert.True(result.Inspected > 0, $"BNAQ1003 inspected no references of {name}.");
            findings.AddRange(result.Findings.Select(f => f.ToString()));
            skipped.AddRange(result.Skipped.Select(s => $"{name}: {s}"));
        }

        Assert.Empty(findings);

        // ⓘ Forward cover: BNAQ1003 reads references by name and loads none, so at 2026.3.925 it
        // never skips. This holds the line if a later version starts to.
        Assert.Empty(skipped);
    }

    [Fact]
    public void BNAQ1004_no_namespace_segment_shadows_a_referenced_root()
    {
        // Internal types as well as public ones: a shadowing segment breaks name resolution inside
        // the assembly that declares it, whether or not a consumer can see the type.
        AssemblyRuleResult result = NamespaceShadowRule.IncludingInternalTypes().Analyze(AssemblyScanContext.Of(Shipped));

        Assert.Empty(result.Findings);
        Assert.Empty(result.Skipped);
        Assert.True(result.Inspected > 0, "BNAQ1004 inspected no namespaces, so it proved nothing.");
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
