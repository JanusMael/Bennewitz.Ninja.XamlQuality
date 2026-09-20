using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// A variant discovered while walking a theme: its raw key, readable name, and the XML namespace
/// declarations the raw key depends on (<c>{x:Static semi:SemiTheme.Aquatic}</c> needs <c>x</c>
/// and <c>semi</c>), so a generated dictionary can declare the same key.
/// </summary>
internal sealed record VariantId(string Key, string DisplayName, IReadOnlyDictionary<string, string> Namespaces);

/// <summary>
/// The raw material a <see cref="ThemeInventory"/> is built from: the variants the theme declares,
/// the base keys shared by all of them, each variant's own keys, any include or code-behind
/// reference that could not be resolved, and every file walked.
/// </summary>
internal sealed record WalkResult(
    IReadOnlyList<VariantId> Variants,
    IReadOnlyDictionary<string, ResourceDefinition> Base,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResourceDefinition>> Own,
    IReadOnlyList<UnresolvedInclude> Unresolved,
    IReadOnlyList<string> Files);

/// <summary>
/// Walks a theme's resource graph carrying a variant context, so a key is attributed to the
/// variant whose <c>ThemeDictionaries</c> slot it was reached through — even when that slot is in a
/// file included lower down (Semi selects its Light/Dark palettes inside a shared
/// <c>Tokens/_index</c>, not the entry). Follows <c>ResourceInclude</c>, <c>StyleInclude</c>
/// (Fluent and Simple reach their control themes that way), inline dictionaries, and code-behind
/// dictionary elements resolved by <see cref="XClassIndex"/> or supplied by a declared provider
/// (a <c>ResourceProvider</c> implemented in code, such as Fluent's accent colours). A resource
/// reached with no variant context is a base key shared by every variant.
/// </summary>
internal static class ThemeGraphWalker
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] IncludeElements = ["ResourceInclude", "MergeResourceInclude", "StyleInclude"];

    public static WalkResult Walk(ThemeSource source)
    {
        string root = Path.GetFullPath(source.BaseDirectory);
        XClassIndex classIndex = XClassIndex.Build(root);

        Walker walker = new(root, source, classIndex);
        walker.WalkFile(Path.GetFullPath(source.EntryFile), context: null);
        foreach (string merged in source.Merge ?? [])
        {
            walker.WalkFile(Path.GetFullPath(merged), context: null);
        }

        return walker.ToResult();
    }

    private sealed class Walker(string root, ThemeSource source, XClassIndex classIndex)
    {
        private readonly Dictionary<string, VariantId> _variants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ResourceDefinition> _base = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, ResourceDefinition>> _own = new(StringComparer.Ordinal);
        private readonly List<UnresolvedInclude> _unresolved = [];
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
        private readonly List<string> _files = [];

        public void WalkFile(string file, VariantId? context)
        {
            if (!_visited.Add(file + "\0" + (context?.Key ?? string.Empty)))
            {
                return;
            }

            if (!File.Exists(file))
            {
                _unresolved.Add(new UnresolvedInclude(file, file, "file not found"));
                return;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(file);
            }
            catch (XmlException ex)
            {
                _unresolved.Add(new UnresolvedInclude(file, file, $"not well-formed XML: {ex.Message}"));
                return;
            }

            if (!_files.Contains(file, StringComparer.Ordinal))
            {
                _files.Add(file);
            }

            if (document.Root is { } documentRoot)
            {
                Process(documentRoot, file, context);
            }
        }

        private void Process(XElement element, string file, VariantId? context)
        {
            foreach (XElement child in element.Elements())
            {
                string localName = child.Name.LocalName;

                if (localName.EndsWith("ThemeDictionaries", StringComparison.Ordinal))
                {
                    foreach (XElement slotChild in child.Elements())
                    {
                        XAttribute? key = slotChild.Attribute(XamlNamespace + "Key");
                        if (key is null)
                        {
                            continue;
                        }

                        DescendReference(slotChild, file, Register(key.Value, slotChild));
                    }
                }
                else if (localName.EndsWith("MergedDictionaries", StringComparison.Ordinal))
                {
                    foreach (XElement slotChild in child.Elements())
                    {
                        DescendReference(slotChild, file, context);
                    }
                }
                else if (localName == "StyleInclude")
                {
                    IncludeSource(child, file, context);
                }
                else
                {
                    if (child.Attribute(XamlNamespace + "Key") is { } keyed)
                    {
                        Record(context, keyed.Value, new ResourceDefinition(ThemeDefinitionScanner.ValueOf(child), file, new XElement(child)));
                    }

                    Process(child, file, context);
                }
            }
        }

        private void DescendReference(XElement node, string includingFile, VariantId? context)
        {
            string localName = node.Name.LocalName;

            if (IncludeElements.Contains(localName, StringComparer.Ordinal))
            {
                IncludeSource(node, includingFile, context);
            }
            else if (localName == "ResourceDictionary")
            {
                Process(node, includingFile, context); // inline dictionary; its keys take this context
            }
            else if (classIndex.TryResolve(localName, out string file, out string reason))
            {
                WalkFile(file, context); // a code-behind dictionary element (e.g. <semi:Light/>), by x:Class
            }
            else if (source.Providers is not null && source.Providers.TryGetValue(localName, out IReadOnlyDictionary<string, string?>? provided))
            {
                foreach ((string key, string? literal) in provided)
                {
                    Record(context, key, new ResourceDefinition(ProvidedValue(literal), null, null));
                }
            }
            else
            {
                _unresolved.Add(new UnresolvedInclude(includingFile, localName, reason));
            }
        }

        private void IncludeSource(XElement node, string includingFile, VariantId? context)
        {
            string? includeSource = node.Attribute("Source")?.Value;
            if (string.IsNullOrWhiteSpace(includeSource))
            {
                return;
            }

            if (ResourceIncludeResolver.TryResolveSource(includeSource.Trim(), includingFile, root, source.AssemblyName,
                                                         out string resolved, out string reason, source.Links))
            {
                WalkFile(resolved, context);
            }
            else
            {
                _unresolved.Add(new UnresolvedInclude(includingFile, includeSource.Trim(), reason));
            }
        }

        /// <summary>A provider's declared value: a colour literal, an alias to another key, or opaque.</summary>
        private static ResourceValue ProvidedValue(string? literal)
        {
            if (string.IsNullOrWhiteSpace(literal))
            {
                return new ResourceValue.Opaque();
            }

            return AuditColor.TryParse(literal, out AuditColor color)
                ? new ResourceValue.ColorLiteral(color)
                : new ResourceValue.Alias(literal.Trim());
        }

        private VariantId Register(string key, XElement declaringElement)
        {
            if (!_variants.TryGetValue(key, out VariantId? variant))
            {
                variant = new VariantId(key, ThemeVariant.DisplayNameOf(key), NamespacesOf(key, declaringElement));
                _variants[key] = variant;
                _own[key] = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
            }

            return variant;
        }

        /// <summary>
        /// The namespace declarations a raw variant key needs: every <c>prefix:</c> inside a
        /// markup-extension key, resolved through the declaring element's scope.
        /// </summary>
        private static IReadOnlyDictionary<string, string> NamespacesOf(string key, XElement element)
        {
            Dictionary<string, string> namespaces = new(StringComparer.Ordinal);
            if (!key.StartsWith('{'))
            {
                return namespaces;
            }

            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] != ':')
                {
                    continue;
                }

                int start = i;
                while (start > 0 && (char.IsLetterOrDigit(key[start - 1]) || key[start - 1] == '_'))
                {
                    start--;
                }

                if (start == i)
                {
                    continue;
                }

                string prefix = key[start..i];
                XNamespace? ns = element.GetNamespaceOfPrefix(prefix);
                if (ns is not null && !namespaces.ContainsKey(prefix))
                {
                    namespaces[prefix] = ns.NamespaceName;
                }
            }

            return namespaces;
        }

        private void Record(VariantId? context, string key, ResourceDefinition definition)
        {
            if (context is null)
            {
                _base[key] = definition;
            }
            else
            {
                _own[context.Key][key] = definition;
            }
        }

        public WalkResult ToResult()
        {
            Dictionary<string, IReadOnlyDictionary<string, ResourceDefinition>> own =
                _own.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, ResourceDefinition>)kv.Value, StringComparer.Ordinal);
            return new WalkResult([.. _variants.Values], _base, own, _unresolved, _files);
        }
    }
}
