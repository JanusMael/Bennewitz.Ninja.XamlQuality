using System.Runtime.CompilerServices;

// The CLI face of this library. Splitting the tool out of the analysis turned what used to be
// one assembly into two, and the audit types were internal — correct then, and still correct now:
// they are implementation, not the API a consumer should bind to.
//
// ⛔ THE ALTERNATIVE WAS TO MAKE THEM PUBLIC, AND THAT WOULD HAVE BEEN A MISTAKE MADE BY REFLEX.
// Publishing ~4,000 lines of internals as API because the compiler asked is how a package acquires
// a surface nobody designed — and a public surface is a promise, immutable in practice once
// someone binds to it.
//
// ⚠ Curating a real audit API for consumers is worth doing, and is deliberately NOT this change.
// Until then the supported surface is IXamlRule and the rules; the audit is reachable through the
// tool.
[assembly: InternalsVisibleTo("ThemeAudit")]

// The test project asserts against the ported analysis directly, exactly as it did before the
// move — which is what makes those fourteen files evidence that the move changed no behaviour.
[assembly: InternalsVisibleTo("XamlQuality.Tests")]
