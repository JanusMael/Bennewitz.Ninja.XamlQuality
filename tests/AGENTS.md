# AGENTS.md — `tests/`

One test project, `XamlQuality.Tests`. `tests/Directory.Build.props` makes it an xUnit v3 test
executable on Microsoft.Testing.Platform that is never packed, with `Xunit` as a global using.

| Folder in `XamlQuality.Tests/` | What it covers |
|---|---|
| `Rules/` | One test class per rule in `src/XamlQuality/Rules/`, each writing its markup into a fresh temporary directory; `FocusFakes.cs` is the miniature framework `XQ1005`'s tests read through reflection. `UnloadableBuildOutputTests` covers the two rules that read compiled code, over `UnloadableAssembly.cs`: build output emitted at test time whose dependency does not load |
| `ThemeAudit/` | The `ThemeAudit` analysis; the committed inputs are under `ThemeAudit/Fixtures/` (`audit`, `compat`), located through `FixturePaths.Fixture` |
| `Packaging/` | `AssemblyQualityTests` runs the family's `Bennewitz.Ninja.AssemblyQuality` rules over both shipped assemblies; `ReleaseWorkflowTests` reads `.github/workflows/release.yml` |
| `RulesCatalogTests.cs` | Generates, and checks, the README's rules table from the rule types |

## Rules

| Rule | Why | Guarded by |
|---|---|---|
| `tests/Directory.Build.props` imports the root props explicitly | MSBuild applies only the closest `Directory.Build.props`; without the import the project silently loses the root's target framework, nullable settings and AutoVersioning reference | the `Import` line itself |
| No VSTest bridge packages | Tests run on Microsoft.Testing.Platform, so `dotnet test` takes `--solution` and rejects VSTest-only switches | `global.json`, `test.runner`; `tests/Directory.Build.props` comment |
| The test project references `XamlQuality.ThemeAudit.csproj` as well as the library | `AssemblyQualityTests` loads each shipped assembly from the test output, and `ThemeAudit.dll` arrives only through that reference | `XamlQuality.Tests.csproj` comment; `AssemblyQualityTests.Every_shipped_assembly_is_in_the_scan` |
| A test finds the repository by walking up to `XamlQuality.slnx`, and a fixture by walking up to `XamlQuality.Tests.csproj` | Test binaries sit at a depth that varies by configuration, and a runner picks its own working directory | `RulesCatalogTests.RepositoryRoot`, `ReleaseWorkflowTests.RepoRoot`, `FixturePaths` |
| A test that could pass vacuously has a companion proving there was something to check | A discovery that finds nothing agrees perfectly with an empty table or an empty scan | `RulesCatalogTests.RuleDiscovery_FindsTheRulesThatShip`; `ReleaseWorkflowTests.The_release_workflow_still_has_both_publishing_steps`; the `Inspected` assertions in `AssemblyQualityTests` |
| The README table is regenerated with `XQ_UPDATE_DOCS` set, never edited by hand, and the build never writes it | A build that edits a tracked file leaves CI with a dirty tree | `RulesCatalogTests.TheReadmeRulesTable_IsWhatTheRuleTypesSay` |
| No test constructs a type in `Rules/FocusFakes.cs` | Constructing one runs its static constructor, which is `XQ1005`'s job; a test that ran it first would hide the cold read the rule has to get right | the file's header comment; test `KeyBindingFocusRuleTests.AListWithStockRows_IsLive_BecauseItsRowsTakeFocus` |
| The unloadable fixture is emitted at test time, and loaded from its file where the assembly it references is not | A checked-in binary cannot be reviewed, a fixture project the tests reference brings its dependency into the test output where it loads, and one loaded from bytes has no metadata file for the rules to read type names from | `Rules/UnloadableAssembly.cs` remarks; the `UnloadableBuildOutputTests` guards, which assert the fixture's control did not load |
| `InternalsVisibleTo("XamlQuality.Tests")` lets the `ThemeAudit/` tests assert on internal types | They were ported unchanged, which is what makes them evidence the move changed no behaviour | `src/XamlQuality/Properties/AssemblyInfo.cs` |

⛔ **Never weaken a test under `Packaging/` to make it pass.** When one fails, the workflow, the
project list or a shipped assembly is wrong, not the test.
