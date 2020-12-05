using Driscod.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Network.Udp
{
    public class UdpSocket : IUdpSocket
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private bool _disposed = false;

        private readonly UdpClient _udpClient;

        private readonly CancellationTokenSource _cancellationToken = new CancellationTokenSource();

        private readonly object _packetQueueLock = new object();

        private event EventHandler _packetRecievedEvent;

        private Queue<byte[]> ReceivedPackets { get; set; } = new Queue<byte[]>();

        public bool ListenForPackets { get; set; } = false;

        public UdpSocket(string endpointAddress, int endpointPort)
            : this(new IPEndPoint(IPAddress.Parse(endpointAddress), endpointPort))
        {
        }

        public UdpSocket(IPEndPoint endpoint)
        {
            _udpClient = new UdpClient();
            _udpClient.Connect(endpoint);
            Logger.Debug($"UDP socket connection to {endpoint.Address}:{endpoint.Port}");

            Task.Run(async () =>
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    if (ListenForPackets)
                    {
                        try
                        {
                            var result = await Task.Run(async () => await _udpClient.ReceiveAsync(), _cancellationToken.Token);
                            lock (_packetQueueLock)
                            {
                                ReceivedPackets.Enqueue(result.Buffer);
                                _packetRecievedEvent?.Invoke(this, EventArgs.Empty);
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // ignored
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
            }).Forget();
        }

        public byte[] GetNextPacket()
        {
            ThrowIfDisposed();

            if (!ListenForPackets)
            {
                throw new InvalidOperationException("Not listening for packets.");
            }

            lock (_packetQueueLock)
            {
                if (!ReceivedPackets.Any())
                {
                    return new byte[0];
                }

                return ReceivedPackets.Dequeue();
            }
        }

        public async Task<byte[]> WaitForNextPacket()
        {
            ThrowIfDisposed();

            if (!ListenForPackets)
            {
                throw new InvalidOperationException("Not listening for packets.");
            }

            var tcs = new TaskCompletionSource<byte[]>();

            EventHandler handler = (a, b) =>
            {
                lock (_packetQueueLock)
                {
                    tcs.TrySetResult(ReceivedPackets.Dequeue());
                }
            };

            _packetRecievedEvent += handler;
            try
            {
                var packet = GetNextPacket();
                if (packet.Length > 0)
                {
                    return packet;
                }
                await Task.Run(() =>
                {
                    tcs.Task.Wait(_cancellationToken.Token);
                });
                return tcs.Task.Result;
            }
            finally
            {
                _packetRecievedEvent -= handler;
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
                _cancellationToken.Cancel();
                _udpClient.Dispose();
                ReceivedPackets.Clear();
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
