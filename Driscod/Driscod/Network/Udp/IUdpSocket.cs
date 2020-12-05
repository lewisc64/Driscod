using System;
using System.Threading.Tasks;

namespace Driscod.Network.Udp
{
    public interface IUdpSocket : IDisposable
    {
        public byte[] GetNextPacket();

        public Task<byte[]> WaitForNextPacket();

        public Task Send(byte[] packet);
    }
}
