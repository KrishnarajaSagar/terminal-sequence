using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sequence.Contracts;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for the network protocol (and, later, for
/// persisted game states). Compact, camel-cased, with enums as strings.
/// </summary>
public static class ProtocolJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new BoardStateJsonConverter());
        return options;
    }
}