# Bennewitz.Ninja.XamlQuality

Static analysis for XAML markup quality — accessibility names, theme-token discipline, resource
keys a theme leaves undefined, contrast floors, and the conventions a XAML codebase drifts away
from silently.

| Package | What it is |
|---|---|
| `Bennewitz.Ninja.XamlQuality` | The **rules library**. Zero dependencies |
| `Bennewitz.Ninja.XamlQuality.ThemeAudit` | The **`theme-audit` CLI**, for CI and for generating theme-compat dictionaries |

## Why a library and not a test package

Each rule takes a scan context and returns findings. Nothing in the library references a test
framework, so the same rules drive MSTest, xUnit, a CLI, or an MSBuild target — the consumer
chooses the runner, and the severity.

```csharp
var context = XamlScanContext.Load("src");
var result  = new ExpanderAutomationNameRule().Analyze(context);

Assert.AreEqual(0, result.Findings.Count, string.Join("\n", result.Findings));
Assert.AreNotEqual(0, result.Inspected, "The rule matched nothing at all — it is inert.");
```

That second assertion is the point of `Inspected`. A rule whose selector stops matching — a
renamed element, a changed namespace, a folder that moved — returns zero findings and looks
exactly like a clean codebase. Findings alone is a number that cannot rise, so it can never fail.

## No Avalonia dependency, on purpose

Every rule reads markup as text or as XML, so this works against WPF and MAUI XAML as well as
Avalonia's. A quality analyser that drags dependencies into a consumer's test project is one
people stop referencing.

## Rules

| Id | Requires |
|---|---|
| `XQ1001` | Every `Expander` declares `AutomationProperties.Name` |

## Versioning

Versions come from the release tag (`v1.2.3` → `1.2.3`), passed to the build as `/p:Version=`.
`Bennewitz.Ninja.AutoVersioning` records build metadata into the assemblies; it does not set the
package version. A local build reads `1.0.0`, which is what a local build is.

Publishing uses **NuGet.org Trusted Publishing (OIDC)** — no long-lived API key exists in this
repository.

## Licence

MIT. See [LICENSE](LICENSE).
