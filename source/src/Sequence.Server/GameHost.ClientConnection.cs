using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sequence.Contracts;
using Sequence.Domain;

namespace Sequence.Server;

/// <summary>One live peer socket plus the identity and seat it is bound to.</summary>
public sealed partial class GameHost
{
    private sealed class ClientConnection
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public string ClientId { get; set; } = string.Empty;

        public PlayerId Seat { get; set; }

        public bool Registered { get; set; }

        public NetworkStream Stream => _stream;

        public ClientConnection(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        /// <summary>A write is always atomic: frames never interleave on the wire.</summary>
        public async ValueTask SendFrameAsync(byte[] payload, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ProtocolFraming.WriteFrameAsync(_stream, payload, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>Unblocks any pending read so the connection loop can wind down.</summary>
        public void Close()
        {
            try
            {
                _stream.Close();
            }
            catch (IOException)
            {
            }

            try
            {
                _client.Close();
            }
            catch (IOException)
            {
            }
        }
    }
}