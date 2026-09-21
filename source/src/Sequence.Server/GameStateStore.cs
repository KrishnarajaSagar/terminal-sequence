using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sequence.Contracts;
using Sequence.Domain;

namespace Sequence.Server;

/// <summary>
/// Server-side persistence for the authoritative <see cref="GameState"/>. Saves are
/// plain JSON files under a <c>saves</c> directory (git-ignored) containing everything
/// needed to resume a game: the setup, both hands (including the opening draw), the
/// draw pile, discards, all chips and locked positions, turn bookkeeping, and the
/// shuffle round. Clients are never involved in persistence.
/// </summary>
public static class GameStateStore
{
    public const string DefaultSaveName = "game-001";

    private const string SaveDirectoryName = "saves";

    /// <summary>
    /// JSON options for save files. Like the wire options (camel-case, enums as strings,
    /// <see cref="BoardState"/> flattened), plus converters for the two immutable
    /// collections <see cref="System.Text.Json"/> cannot handle on its own: the draw
    /// pile queue and the per-player sequence counts.
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Directory the default save set lives in, relative to the current directory.</summary>
    public static string DefaultSaveDirectory => Path.Combine(Directory.GetCurrentDirectory(), SaveDirectoryName);

    /// <summary>True when a save with that name already exists.</summary>
    public static bool Exists(string saveName, string? directory = null) =>
        File.Exists(ResolvePath(saveName, directory));

    /// <summary>
    /// Writes the authoritative state atomically (temp file + rename), so a crash at
    /// any point can never leave a half-written save.
    /// </summary>
    public static string Save(GameState state, string? saveName = null, string? directory = null)
    {
        string path = ResolvePath(saveName ?? DefaultSaveName, directory);
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);

        string temp = path + ".tmp";
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(state, Options);
        File.WriteAllBytes(temp, json);

        // Replaces any existing file. File.Move does this atomically on the same volume.
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Reads a previously saved authoritative state.</summary>
    public static GameState Load(string saveName, string? directory = null)
    {
        string path = ResolvePath(saveName, directory);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No game save found at '{path}'. Start a new game with '--server --seed <n>'.", path);
        }

        GameState? state = JsonSerializer.Deserialize<GameState>(File.ReadAllBytes(path), Options);
        if (state is null)
        {
            throw new InvalidDataException($"The save at '{path}' is not a valid game state.");
        }

        Validate(state, path);
        return state;
    }

    private static GameState Validate(GameState state, string path)
    {
        if (state.Players.Count != GameSetup.MinPlayers)
        {
            throw new InvalidDataException($"The save at '{path}' does not describe a two-player game.");
        }

        if (state.Board is null)
        {
            throw new InvalidDataException($"The save at '{path}' is missing its board.");
        }

        return state;
    }

    /// <summary>
    /// Produces the file path for a save name. Names are used with a ".json" extension
    /// unless they already carry one; path separators in a name are rejected so a save
    /// cannot escape the saves directory.
    /// </summary>
    public static string ResolvePath(string saveName, string? directory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveName);

        if (saveName.Contains(Path.DirectorySeparatorChar) || saveName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("A save name must be a plain file name.", nameof(saveName));
        }

        string folder = directory ?? DefaultSaveDirectory;
        string fileName = Path.HasExtension(saveName) ? saveName : saveName + ".json";
        return Path.Combine(folder, fileName);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new BoardStateJsonConverter());
        options.Converters.Add(new DrawPileJsonConverter());
        options.Converters.Add(new SequenceCountsJsonConverter());
        return options;
    }

    /// <summary>
    /// Serializes the draw pile queue as a plain array. JSON has no first-in/first-out
    /// shape, so the queue is rebuilt by enqueuing in order while flood-filling the
    /// dequeuing that already happens during normal play.
    /// </summary>
    private sealed class DrawPileJsonConverter : JsonConverter<ImmutableQueue<Card>>
    {
        public override ImmutableQueue<Card> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Card[] cards = JsonSerializer.Deserialize<Card[]>(ref reader, options) ?? Array.Empty<Card>();
            ImmutableQueue<Card> queue = ImmutableQueue<Card>.Empty;
            foreach (Card card in cards)
            {
                queue = queue.Enqueue(card);
            }

            return queue;
        }

        public override void Write(Utf8JsonWriter writer, ImmutableQueue<Card> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (Card card in value)
            {
                JsonSerializer.Serialize(writer, card, options);
            }

            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// The sequence counts use <see cref="PlayerId"/> as a dictionary key, which JSON
    /// cannot express directly, so the map is flattened to an array of player/count
    /// pairs and rebuilt on read.
    /// </summary>
    private sealed class SequenceCountsJsonConverter : JsonConverter<ImmutableDictionary<PlayerId, int>>
    {
        public override ImmutableDictionary<PlayerId, int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var entries = JsonSerializer.Deserialize<PlayerCountEntry[]>(ref reader, options) ?? [];
            var counts = ImmutableDictionary<PlayerId, int>.Empty;
            foreach (PlayerCountEntry entry in entries)
            {
                counts = counts.SetItem(new PlayerId(entry.PlayerId), entry.Count);
            }

            return counts;
        }

        public override void Write(Utf8JsonWriter writer, ImmutableDictionary<PlayerId, int> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach ((PlayerId playerId, int count) in value.OrderBy(pair => pair.Key.Value))
            {
                writer.WriteStartObject();
                writer.WriteNumber("playerId", playerId.Value);
                writer.WriteNumber("count", count);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private sealed record PlayerCountEntry(int PlayerId, int Count);
    }
}