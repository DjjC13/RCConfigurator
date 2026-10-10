using System.Text.Json;
using System.Text.Json.Serialization;

namespace RCConfigurator.Core;

/// <summary>The JSON format the probe writes and the app reads.</summary>
public static class SnapshotJson
{
    private static readonly JsonSerializerOptions Compact = Create(indented: false);
    private static readonly JsonSerializerOptions Indented = Create(indented: true);

    public static string Serialize(MenuSnapshot snapshot, bool indented = false) =>
        JsonSerializer.Serialize(snapshot, indented ? Indented : Compact);

    public static MenuSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<MenuSnapshot>(json, Compact)
        ?? throw new JsonException("The snapshot was empty.");

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
