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
| `XQ1002` | Every interactive control declares `AutomationProperties.Name` |

**Run both.** `XQ1002` deliberately leaves `Expander` out of its element set, because both rules
firing on one element would report a single defect twice. So `XQ1002` alone is not a superset:
adopt it on its own and Expanders go unchecked — which is the failure this library exists to
catch, a gate that reads as comprehensive and is not.

`XQ1002` covers a curated framework set, not every focusable type in any one framework. Pass your
own control names to the constructor — a control you wrote is exactly the one no framework list
will ever name, and leaving it out means the rule reports clean over the markup least likely to
have been reviewed. Assert on `Inspected` to tell that apart from a clean result.

## Versioning

Versions come from the release tag (`v1.2.3` → `1.2.3`), passed to the build as `/p:Version=`.
`Bennewitz.Ninja.AutoVersioning` records build metadata into the assemblies; it does not set the
package version. A local build reads `1.0.0`, which is what a local build is.

Publishing uses **NuGet.org Trusted Publishing (OIDC)** — no long-lived API key exists in this
repository.

## Publishing

See [docs/publishing.md](docs/publishing.md) — one-time trusted-publishing setup, the version rule,
and what to check after a release.

## Licence

MIT. See [LICENSE](LICENSE).
