# AGENTS.md — `src/`

The two shipped projects. Both are packed and published from one tag, so everything here is
permanent once released. There is no `src/Directory.Build.props`; both projects take the root one
directly.

| Project | Assembly | Package | What it ships |
|---|---|---|---|
| `XamlQuality/` | `XamlQuality` | `Bennewitz.Ninja.XamlQuality` | The rules (`Rules/`, each an `IXamlRule`), the scan model (`XamlScanContext`, `XamlFile`, `XamlFinding`, `XamlSkip`), and the whole `ThemeAudit` analysis with its reviewed `ThemeAudit/Mappings/*.json` |
| `XamlQuality.ThemeAudit/` | `ThemeAudit` | `Bennewitz.Ninja.XamlQuality.ThemeAudit` | The `theme-audit` dotnet tool: `Program.cs`, argument parsing and reporting over the library's `ThemeAudit` namespace, and nothing else |

## Rules

| Rule | Why | Guarded by |
|---|---|---|
| `XamlQuality.csproj` takes no `PackageReference`; `System.CommandLine` belongs to the tool alone | The library's dependencies become every consumer's test-project dependencies | `Directory.Packages.props`, group `Tool`; `AssemblyQualityTests.AQ1003_no_assembly_references_a_framework_or_another_family_package`, whose `ForbiddenInTheLibraryOnly` names `System.CommandLine` |
| Analysis goes in the library, never in `Program.cs` | A consumer's test project runs the same analysis without shelling out to the tool | `XamlQuality.ThemeAudit.csproj`, `Description` |
| The tool's `AssemblyName` stays `ThemeAudit` and its `ToolCommandName` stays `theme-audit` | `InternalsVisibleTo("ThemeAudit")` is how the tool reaches `ResourceKeyScanner` and `ResourceKey`; the command name is what consumers' scripts and CI steps invoke | `src/XamlQuality/Properties/AssemblyInfo.cs`; `XamlQuality.ThemeAudit.csproj` comment on `ToolCommandName` |
| `Properties/AssemblyInfo.cs` names which `ThemeAudit` types are public and which stay internal | The audit surface shipped public and a consumer binds to it, so narrowing a type is a breaking change | `src/XamlQuality/Properties/AssemblyInfo.cs` comment |
| Every public member carries an XML doc comment | `GenerateDocumentationFile` is on in both csproj files and warnings are errors, so a missing comment fails the build | `XamlQuality.csproj`, `XamlQuality.ThemeAudit.csproj`; root `Directory.Build.props`, `TreatWarningsAsErrors` |
| The `None` item for `ThemeAudit/Mappings/*.json` keeps `Link`, `PackageCopyToOutput` and `PackagePath` | `CompatMapping.Locate` resolves a bare mapping name against `Mappings/` beside the assembly; without them a `PackageReference` consumer gets no files there | `XamlQuality.csproj`, the comments on that item |
| `theme-audit` exits 0 when clean, 1 on drift or an unreadable file, 2 on a configuration error | CI steps in consuming repositories branch on the code | `Program.cs`, the `compat` and `report` actions |
| A project added here is also referenced from `tests/XamlQuality.Tests/XamlQuality.Tests.csproj` and named in `release.yml` | `AssemblyQualityTests` scans every csproj under `src` and fails when its assembly is not in the test output; the release publishes only the packages it names | `AssemblyQualityTests.Every_shipped_assembly_is_in_the_scan`; `ReleaseWorkflowTests` |
