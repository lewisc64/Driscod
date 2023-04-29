using System;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Network.Udp;

public interface IUdpSocket : IDisposable
{
    byte[] GetNextPacket();

    Task<byte[]> WaitForNextPacket(TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task Send(byte[] packet);
}
