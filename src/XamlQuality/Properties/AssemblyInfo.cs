using System.Runtime.CompilerServices;

// The CLI face of this library, which reaches the seven genuinely internal helpers below.
//
// ⛔ THE AUDIT SURFACE IS PUBLIC, AND HAS BEEN SINCE 2026.3.920. Whatever the intent, that is what
// shipped: 53 public top-level types under ThemeAudit/ against 7 internal. AuditRunner,
// AuditConfig, MarkdownReport, AuditResult, CompatOutcome, CompatHow, CompatEntry,
// ConsumerThemeFindings, UndefinedKeyFinding, ContrastFinding, ContrastStatus and the whole config
// surface are all reachable by any consumer, and Bennewitz.Ninja.DiffView binds to eleven of them.
//
// ⚠ NARROWING IT IS A BREAKING CHANGE. A published surface is a promise once someone binds to it,
// and one already has. Curating a smaller, deliberate audit API is still worth doing — but it is a
// major-version decision with a migration path, not a tidy-up, and the version to beat is
// 2026.3.920.
//
// Internal, and staying that way: NamedColors, XamlFiles, ResourceKeyScanner, ThemeGraphWalker,
// ResourceKey, VariantId, WalkResult.
[assembly: InternalsVisibleTo("ThemeAudit")]

// The test project asserts against the ported analysis directly, exactly as it did before the
// move — which is what makes those fourteen files evidence that the move changed no behaviour.
[assembly: InternalsVisibleTo("XamlQuality.Tests")]
