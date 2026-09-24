using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bennewitz.Ninja.XamlQuality.ThemeAudit;

/// <summary>
/// The reviewed table behind a compat dictionary: which of the source theme's variants feeds
/// each target variant, which keys map onto the target theme's own tokens, which are left out,
/// and what the generator may copy verbatim. Ships with the library under <c>Mappings/</c> and is
/// overridable per run by pointing the configuration at another file.
/// </summary>
public sealed class CompatMapping
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>What the mapping is for, for the report.</summary>
    public string? Description { get; set; }

    /// <summary>Target variant → the source variant whose keys feed it, in output order.</summary>
    public Dictionary<string, string> Variants { get; set; } = [];

    /// <summary>Source key → the target theme's token (or a colour literal) it maps onto.</summary>
    public Dictionary<string, MappingTarget> Keys { get; set; } = [];

    /// <summary>Source keys deliberately not carried over.</summary>
    public List<string> Skip { get; set; } = [];

    /// <summary>What may be copied verbatim when no mapping applies.</summary>
    public CopyPolicy Copy { get; set; } = new();

    /// <summary>The file this mapping was read from, for the report.</summary>
    [JsonIgnore]
    public string Name { get; private set; } = string.Empty;

    /// <summary>Reads a mapping file.</summary>
    /// <exception cref="AuditConfigException">The file is missing or malformed.</exception>
    public static CompatMapping Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new AuditConfigException($"mapping file not found: {path}");
        }

        CompatMapping mapping;
        try
        {
            mapping = JsonSerializer.Deserialize<CompatMapping>(File.ReadAllText(path), JsonOptions)
                      ?? throw new AuditConfigException($"{path}: the mapping is empty");
        }
        catch (JsonException ex)
        {
            throw new AuditConfigException($"{path}: {ex.Message}");
        }

        if (mapping.Variants.Count == 0)
        {
            throw new AuditConfigException($"{path}: 'variants' must map at least one target variant to a source variant");
        }

        mapping.Name = Path.GetFileName(path);
        return mapping;
    }

    /// <summary>
    /// Finds a mapping by the configuration's value: a path relative to the configuration, or
    /// the bare name of one shipped with the library (<c>FluentToSemi</c>) under <c>Mappings/</c>
    /// in the application's base directory, where the package copies them.
    /// </summary>
    public static string Locate(AuditConfig config, string mapping)
    {
        bool bareName = !mapping.Contains('/') && !mapping.Contains('\\') && !mapping.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        if (bareName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Mappings", mapping + ".json");
        }

        return config.Resolve(mapping);
    }
}

/// <summary>Where a mapped key goes: the target theme's key or a colour literal, with the reviewer's reason.</summary>
[JsonConverter(typeof(MappingTargetConverter))]
public sealed record MappingTarget(string To, string? Why);

/// <summary>Reads a mapping target as a bare string or as <c>{ "to": …, "why": … }</c>.</summary>
public sealed class MappingTargetConverter : JsonConverter<MappingTarget>
{
    /// <inheritdoc/>
    public override MappingTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new MappingTarget(reader.GetString()!, null);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("a mapping target is a string or an object with 'to' and optional 'why'");
        }

        string? to = null;
        string? why = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string property = reader.GetString()!;
            reader.Read();
            if (property.Equals("to", StringComparison.OrdinalIgnoreCase))
            {
                to = reader.GetString();
            }
            else if (property.Equals("why", StringComparison.OrdinalIgnoreCase))
            {
                why = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return new MappingTarget(to ?? throw new JsonException("a mapping target object needs 'to'"), why);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MappingTarget value, JsonSerializerOptions options)
    {
        if (value.Why is null)
        {
            writer.WriteStringValue(value.To);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("to", value.To);
        writer.WriteString("why", value.Why);
        writer.WriteEndObject();
    }
}

/// <summary>
/// What the generator copies verbatim: any element in an allowed namespace whose kind is not
/// excluded. Templates and styles are excluded by default — carrying Fluent's named
/// sub-templates into a Semi application would restyle controls Semi already themes — and
/// converter namespaces every Avalonia application has are allowed.
/// </summary>
public sealed class CopyPolicy
{
    /// <summary>XML namespaces (besides Avalonia's own and XAML's) whose elements may be copied.</summary>
    public List<string> Namespaces { get; set; } =
    [
        "using:System",
        "clr-namespace:System;assembly=System.Runtime",
        "using:Avalonia.Controls.Converters",
        "clr-namespace:Avalonia.Controls.Converters;assembly=Avalonia.Controls",
    ];

    /// <summary>Element kinds (local names) never copied.</summary>
    public List<string> SkipElements { get; set; } =
    [
        "ControlTheme",
        "Style",
        "Styles",
        "ControlTemplate",
        "DataTemplate",
        "ItemsPanelTemplate",
        "MenuFlyout",
        "ResourceDictionary",
        "ResourceInclude",
        "MergeResourceInclude",
        "StyleInclude",
    ];
}
