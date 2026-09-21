using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sequence.Contracts;

/// <summary>
/// Wire framing for the TCP transport: every message is a 4-byte big-endian length
/// followed by that many UTF-8 JSON bytes. A frame boundary is unambiguous, so streams
/// can be split and reassembled even if messages arrive split or coalesced by TCP.
/// </summary>
public static class ProtocolFraming
{
    private const int LengthPrefixBytes = 4;
    private const int MaxFrameBytes = 16 * 1024 * 1024;

    public static byte[] Encode(string json) => Encoding.UTF8.GetBytes(json);

    public static ValueTask WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        // A single headered buffer kept together with one WriteAsync is atomic with
        // respect to other writers on the same socket and avoids two syscalls.
        byte[] frame = new byte[LengthPrefixBytes + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame.AsMemory(LengthPrefixBytes));
        return stream.WriteAsync(frame, ct);
    }

    public static void WriteFrame(Stream stream, ReadOnlyMemory<byte> payload)
    {
        byte[] frame = new byte[LengthPrefixBytes + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame.AsMemory(LengthPrefixBytes));
        stream.Write(frame, 0, frame.Length);
    }

    /// <summary>
    /// Reads one frame. Returns <c>null</c> when the peer closed cleanly at a frame
    /// boundary (a normal goodbye), and throws when it closed mid-frame or a frame
    /// exceeds the size guard.
    /// </summary>
    public static byte[]? ReadFrame(Stream stream)
    {
        byte[] prefix = new byte[LengthPrefixBytes];
        int filled = 0;
        while (filled < LengthPrefixBytes)
        {
            int read = stream.Read(prefix, filled, LengthPrefixBytes - filled);
            if (read == 0)
            {
                return filled == 0 ? null : throw new EndOfStreamException("Peer disconnected inside a frame length prefix.");
            }

            filled += read;
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Frame of {length} bytes exceeds the {MaxFrameBytes}-byte limit.");
        }

        byte[] payload = new byte[(int)length];
        stream.ReadExactly(payload, 0, payload.Length);
        return payload;
    }

    /// <summary>
    /// Reads one frame. Returns <c>null</c> when the peer closed cleanly at a frame
    /// boundary (a normal goodbye), and throws when it closed mid-frame or a frame
    /// exceeds the size guard.
    /// </summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] prefix = new byte[LengthPrefixBytes];
        int filled = 0;
        while (filled < LengthPrefixBytes)
        {
            int read = await stream.ReadAsync(prefix.AsMemory(filled), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return filled == 0 ? null : throw new EndOfStreamException("Peer disconnected inside a frame length prefix.");
            }

            filled += read;
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Frame of {length} bytes exceeds the {MaxFrameBytes}-byte limit.");
        }

        byte[] payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        return payload;
    }
}