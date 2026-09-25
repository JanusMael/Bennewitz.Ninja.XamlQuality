using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>
/// The types an assembly holds that load, and the name of every type it defines that does not, with
/// what stopped it.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>A type that does not load has no name reflection will give.</b> A control whose base type is
/// in an assembly that is not beside it fails to load, and <see cref="Assembly.GetTypes"/> hands back
/// only the types that did, with a null where each of the others would be. A rule that took that
/// list as the whole assembly reported the consumer's own control as a framework type the scan was
/// not given, or as no type at all. So the names are read from the assembly's metadata, which loads
/// nothing.
/// </para>
/// <para>
/// ⚠ <b>Only an assembly loaded from a file has metadata to read that way.</b> One loaded from bytes
/// has no location, and the names of its types that did not load stay unknown.
/// </para>
/// </remarks>
/// <param name="Source">The assembly's simple name, as a reader would name it.</param>
/// <param name="Loaded">Every type that loads, public or not.</param>
/// <param name="NotLoaded">Each top-level type the assembly defines that did not load, by name, with what stopped it.</param>
internal sealed record LoadedTypes(
    string Source,
    IReadOnlyList<Type> Loaded,
    IReadOnlyDictionary<string, string> NotLoaded)
{
    /// <summary>Reads <paramref name="assembly"/>'s types, never throwing for one that does not load.</summary>
    internal static LoadedTypes Of(Assembly assembly)
    {
        string name = assembly.GetName().Name ?? assembly.FullName ?? "an unnamed assembly";

        try
        {
            return new LoadedTypes(name, assembly.GetTypes(), new Dictionary<string, string>(StringComparer.Ordinal));
        }
        catch (ReflectionTypeLoadException ex)
        {
            Type[] loaded = [.. ex.Types.OfType<Type>()];
            string cause = CauseOf(ex.LoaderExceptions.OfType<Exception>());
            HashSet<string> present = new(loaded.Select(TopLevelName).OfType<string>(), StringComparer.Ordinal);
            return new LoadedTypes(
                name,
                loaded,
                DefinedNames(assembly).Where(defined => !present.Contains(defined)).ToDictionary(defined => defined, _ => cause, StringComparer.Ordinal));
        }
        catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
        {
            string cause = CauseOf([ex]);
            return new LoadedTypes(
                name,
                [],
                DefinedNames(assembly).ToDictionary(defined => defined, _ => cause, StringComparer.Ordinal));
        }
    }

    /// <summary>What a skip tells a consumer to do about a type that did not load.</summary>
    internal const string Remedy =
        "Scan from a process that can load the scanned assemblies' dependencies, as a test project that references the application does.";

    /// <summary>
    /// What stopped types loading, in words: the assemblies that were not found or would not load,
    /// or else the exceptions' own messages. The causes are the assembly's, not one type's.
    /// </summary>
    internal static string CauseOf(IEnumerable<Exception> exceptions)
    {
        List<string> notFound = [];
        List<string> notLoaded = [];
        List<string> other = [];

        foreach (Exception exception in exceptions)
        {
            switch (exception)
            {
                case FileNotFoundException { FileName: { } file }:
                    notFound.Add(SimpleName(file));
                    break;
                case FileLoadException { FileName: { } file }:
                    notLoaded.Add(SimpleName(file));
                    break;
                default:
                    other.Add($"{exception.GetType().Name}: {exception.Message.TrimEnd('.')}");
                    break;
            }
        }

        string[] causes =
        [
            .. notFound.Count > 0 ? [$"{List(notFound)} could not be found"] : Array.Empty<string>(),
            .. notLoaded.Count > 0 ? [$"{List(notLoaded)} could not be loaded"] : Array.Empty<string>(),
            .. other.Distinct(StringComparer.Ordinal).Take(2),
        ];

        return causes.Length == 0 ? "its dependencies did not load" : string.Join("; ", causes);
    }

    /// <summary>"A", "A and B", "A, B and C", or the first three and how many more.</summary>
    internal static string List(IEnumerable<string> names)
    {
        string[] distinct = [.. names.Distinct(StringComparer.Ordinal)];
        if (distinct.Length > 3)
        {
            return $"{string.Join(", ", distinct[..3])} and {distinct.Length - 3} more";
        }

        return distinct.Length == 1 ? distinct[0] : $"{string.Join(", ", distinct[..^1])} and {distinct[^1]}";
    }

    /// <summary>The top-level type names in the assembly's metadata, or none when it has no file to read.</summary>
    private static IEnumerable<string> DefinedNames(Assembly assembly)
    {
        string location;
        try
        {
            location = assembly.Location;
        }
        catch (NotSupportedException)
        {
            return [];
        }

        if (string.IsNullOrEmpty(location) || !File.Exists(location))
        {
            return [];
        }

        try
        {
            using FileStream stream = File.OpenRead(location);
            using PEReader image = new(stream);
            MetadataReader metadata = image.GetMetadataReader();

            List<string> names = [];
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition definition = metadata.GetTypeDefinition(handle);
                string defined = metadata.GetString(definition.Name);
                if (definition.GetDeclaringType().IsNil && !defined.StartsWith('<'))
                {
                    names.Add(WithoutArity(defined));
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>A loaded type's name as markup would write it, or <c>null</c> for a nested or compiler-generated type.</summary>
    internal static string? TopLevelName(Type type)
    {
        try
        {
            return type.IsNested || type.Name.StartsWith('<') ? null : WithoutArity(type.Name);
        }
        catch (Exception ex) when (ElementTypes.IsUnreadable(ex))
        {
            return null;
        }
    }

    private static string WithoutArity(string name)
    {
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    private static string SimpleName(string file)
    {
        try
        {
            return new AssemblyName(file).Name ?? file;
        }
        catch (Exception ex) when (ex is ArgumentException or FileLoadException)
        {
            return file;
        }
    }
}
