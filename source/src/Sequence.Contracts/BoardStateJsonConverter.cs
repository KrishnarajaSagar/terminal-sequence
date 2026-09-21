using System.Text.Json;
using System.Text.Json.Serialization;
using Sequence.Domain;

namespace Sequence.Contracts;

/// <summary>
/// Serializes a <see cref="BoardState"/> as JSON arrays of chip/owner and locked
/// positions. <see cref="BoardPosition"/> is a custom struct and cannot be used as a
/// JSON dictionary key, so the immutable dictionary is flattened, then rebuilt through
/// the board's public API on read.
/// </summary>
public sealed class BoardStateJsonConverter : JsonConverter<BoardState>
{
    public override BoardState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a BoardState object.");
        }

        var chips = new List<(BoardPosition Position, PlayerId Owner)>();
        var locked = new List<BoardPosition>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string property = reader.GetString()!;
            if (property.Equals("chips", StringComparison.Ordinal))
            {
                ReadChips(ref reader, chips);
            }
            else if (property.Equals("locked", StringComparison.Ordinal))
            {
                ReadPositions(ref reader, locked);
            }
        }

        BoardState board = BoardState.Empty;
        foreach ((BoardPosition position, PlayerId owner) in chips)
        {
            board = board.PlaceChip(position, owner);
        }

        return board.LockPositions(locked);
    }

    public override void Write(Utf8JsonWriter writer, BoardState value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("chips");
        writer.WriteStartArray();
        foreach ((BoardPosition position, PlayerId owner) in value.Chips)
        {
            writer.WriteStartObject();
            writer.WriteNumber("row", position.Row);
            writer.WriteNumber("col", position.Col);
            writer.WriteNumber("owner", owner.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("locked");
        writer.WriteStartArray();
        foreach (BoardPosition position in value.LockedPositions)
        {
            writer.WriteStartObject();
            writer.WriteNumber("row", position.Row);
            writer.WriteNumber("col", position.Col);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void ReadChips(ref Utf8JsonReader reader, List<(BoardPosition Position, PlayerId Owner)> chips)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected a 'chips' array.");
        }

        while (reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                int row = 0;
                int col = 0;
                int owner = 0;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    switch (reader.GetString())
                    {
                        case "row":
                            reader.Read();
                            row = reader.GetInt32();
                            break;
                        case "col":
                            reader.Read();
                            col = reader.GetInt32();
                            break;
                        case "owner":
                            reader.Read();
                            owner = reader.GetInt32();
                            break;
                    }
                }

                chips.Add((new BoardPosition(row, col), new PlayerId(owner)));
            }

            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of 'chips' array.");
            }
        }
    }

    private static void ReadPositions(ref Utf8JsonReader reader, List<BoardPosition> positions)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected a 'locked' array.");
        }

        while (reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                int row = 0;
                int col = 0;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    switch (reader.GetString())
                    {
                        case "row":
                            reader.Read();
                            row = reader.GetInt32();
                            break;
                        case "col":
                            reader.Read();
                            col = reader.GetInt32();
                            break;
                    }
                }

                positions.Add(new BoardPosition(row, col));
            }

            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of 'locked' array.");
            }
        }
    }
}