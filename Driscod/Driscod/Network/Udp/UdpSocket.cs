using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Network.Udp
{
    public class UdpSocket : IUdpSocket
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private UdpClient _udpClient;
        private ConcurrentQueue<byte[]> _recievedPacketBuffer = new ConcurrentQueue<byte[]>();
        private bool _disposed = false;

        private event EventHandler? PacketRecievedEvent;

        public UdpSocket(string endpointAddress, int endpointPort)
            : this(new IPEndPoint(IPAddress.Parse(endpointAddress), endpointPort))
        {
        }

        public UdpSocket(IPEndPoint endpoint)
        {
            _udpClient = new UdpClient();
            _udpClient.Connect(endpoint);
            Logger.Debug($"UDP socket connection to {endpoint.Address}:{endpoint.Port}");

            new Thread(async () =>
            {
                while (!_disposed && !_cancellationTokenSource.IsCancellationRequested)
                {
                    if (ListenForPackets)
                    {
                        try
                        {
                            var result = await _udpClient.ReceiveAsync(cancellationToken: _cancellationTokenSource.Token);
                            _recievedPacketBuffer.Enqueue(result.Buffer);
                            PacketRecievedEvent?.Invoke(this, EventArgs.Empty);
                        }
                        catch (ObjectDisposedException)
                        {
                            // ignore
                        }
                        catch (OperationCanceledException)
                        {
                            // ignore
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Exception in UDP socket recieve loop: {ex}");
                            await Task.Delay(1000);
                        }
                    }
                    else
                    {
                        await Task.Delay(200);
                    }
                }
            }).Start();
        }

        public bool ListenForPackets { get; set; } = false;

        public byte[] GetNextPacket()
        {
            ThrowIfDisposed();

            if (!ListenForPackets)
            {
                throw new InvalidOperationException("Not listening for packets.");
            }

            if (_recievedPacketBuffer.TryDequeue(out var packet))
            {
                return packet;
            }

            return new byte[0];
        }

        public async Task<byte[]> WaitForNextPacket(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (timeout == null)
            {
                timeout = TimeSpan.FromMinutes(10);
            }

            if (!ListenForPackets)
            {
                throw new InvalidOperationException("Not listening for packets.");
            }

            var combinedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);

            var tcs = new TaskCompletionSource<byte[]>();

            EventHandler handler = (a, b) =>
            {
                if (_recievedPacketBuffer.TryDequeue(out var packet))
                {
                    tcs.TrySetResult(packet);
                }
            };

            PacketRecievedEvent += handler;
            try
            {
                var packet = GetNextPacket();
                if (packet.Length > 0)
                {
                    return packet;
                }
                await tcs.Task.WaitAsync(timeout.Value, cancellationToken: combinedCancellationTokenSource.Token);
                return tcs.Task.Result;
            }
            finally
            {
                PacketRecievedEvent -= handler;
            }
        }

        public async Task Send(byte[] packet)
        {
            ThrowIfDisposed();
            await _udpClient.SendAsync(packet, packet.Length);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                _cancellationTokenSource.Cancel();
                _udpClient.Dispose();
                _recievedPacketBuffer.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UdpSocket));
            }
        }
    }
}
