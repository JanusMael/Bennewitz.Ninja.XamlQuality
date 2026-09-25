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

<!-- BEGIN GENERATED RULES -->
| Id | Requires |
|---|---|
| `BNXQ1001` | Every Expander declares AutomationProperties.Name. |
| `BNXQ1002` | Every interactive control declares AutomationProperties.Name. |
| `BNXQ1003` | Every template part a control looks up is declared in its own theme. |
| `BNXQ1004` | Every control fits the fixed Grid slot it is placed in. |
| `BNXQ1005` | Every key binding on an items control sits where keyboard focus can reach it. |
| `BNXQ1006` | Every themed custom control gets an automation peer, or declares in its own code that it has none. |
<!-- END GENERATED RULES -->

The table above is rendered from the rule types — `Id` and `Summary` on each `IXamlRule` — and a
test fails if it drifts. Regenerate it with `XQ_UPDATE_DOCS=1 dotnet test --solution
XamlQuality.slnx` and commit the result. Everything below the markers is written by hand, because
none of it is a property of any single rule.

**The ids carry the family's prefix.** Every id is `BN`, for the family, then `XQ`, this product's
initials, then its number. Until the release after `2026.3.924` the ids were `XQ1001` to `XQ1005`:
only the prefix changed, and `BNXQ1006` never shipped under the old one. A filter or suppression
keyed on an old id stops matching until it names the new one. The prefix does not change again.

**Run both.** `BNXQ1002` deliberately leaves `Expander` out of its element set, because both rules
firing on one element would report a single defect twice. So `BNXQ1002` alone is not a superset:
adopt it on its own and Expanders go unchecked — which is the failure this library exists to
catch, a gate that reads as comprehensive and is not.

`BNXQ1002` covers a curated framework set, not every focusable type in any one framework. Pass your
own control names to the constructor — a control you wrote is exactly the one no framework list
will ever name, and leaving it out means the rule reports clean over the markup least likely to
have been reviewed. Assert on `Inspected` to tell that apart from a clean result.

**`BNXQ1003` reads part names from compiled code.** A control's parts are the `PART_` names it
declares as string constants, public or not, and the `PART_` string literals its code passes to a
lookup: a call whose name starts with `Find` or `Get`, such as `NameScope.Find("PART_Foo")` or
`GetTemplateChild`. Lambdas and controls that are not public are read too. A lookup made on a template the control hands
on, to a helper, down a chain of calls or into a constructor, or made by a base class's
`OnApplyTemplate`, is the control's, and is checked against its theme. A literal passed to
anything else is ignored, which is what keeps compiled XAML out: it passes every element name to
`set_Name` and `Register`. Three limits. A name built at runtime (`"PART_" + name`) is invisible,
and so is a lookup through a helper named otherwise. And a lookup no control hands its
template to is taken to be on its own type's template, so one that reaches into a child control's
template is reported against that type.

**`BNXQ1004` counts only what it measured.** A control in an `Auto` or `*` row or column, or one that
declares no size, has nothing to measure, so it is neither inspected nor skipped. Markup whose grids
are all `Auto` and `*` therefore reads zero inspected, and that is a true answer, not an inert rule:
a floor on `Inspected` suits markup with fixed rows or columns, or a fixture that has them.

**`BNXQ1005` needs the assemblies the markup belongs to.** A key binding fires only while focus is on
its control or inside it, so the rule asks, of every key binding on an items control, whether
anything in that path can take focus: the control, its item containers, or what its item template
puts in them. Whether a type takes focus by default, which container an items control generates,
and whether an element presents items at all are read from compiled types. Pass the application's
own assembly to `WithAssemblies`; the framework is reached through its references. The rule reports
only what it can decide, and key bindings on other controls are not checked.

**`BNXQ1006` is what makes the names count.** A name `BNXQ1001` or `BNXQ1002` requires on a custom control
reaches nobody when the control has no automation peer: the framework's default gives an empty one,
which a control-view search never finds. So the rule asks, of every control that a `ControlTheme` in
the scan themes and the scanned assemblies define, whether anything in its chain overrides
`OnCreateAutomationPeer`: the control itself, a base of your own, or a framework base that gives a
peer, as `Button` does and `TemplatedControl` and `ContentControl` do not. A decorative control opts
out by overriding it to return the empty peer, which says so in the control's own code. Pass the
application's assembly to `WithAssemblies`, from a process that can load its framework.

**Read `Skipped` as well as `Inspected`.** Every result also names what the rule saw but could not
check. For `BNXQ1003` that is a control with a `ControlTheme` in the scan and no part found in its
code, a control that declares parts but has no theme in the scan, one the scanned assemblies define
but could not load, one whose code could be read only in part, and, when the scan was given no
assemblies at all, every themed control. For `BNXQ1004` it is a control whose fit depends on a value
markup cannot evaluate: a bound or resource-based definition, index, span, spacing or size, named
in the reason. For `BNXQ1005` it is every key binding it could not decide: one where a style sets
focus, rows with no item template, rows that present items of their own as a tree's do, and content
whose template the scan does not hold. For `BNXQ1006` it is a themed control that did not load or
whose peer method could not be read, a name more than one scanned type carries, a type with no
`OnCreateAutomationPeer` at all, and, when the scan was given no assemblies, every themed control.
None is a violation, and some are correct; all are places where a clean result is not what it
seems.

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
