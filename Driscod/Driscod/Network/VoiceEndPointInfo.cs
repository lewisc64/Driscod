using System.Net;

namespace Driscod.Network
{
    public struct VoiceEndPointInfo
    {
        public IPEndPoint SocketEndPoint { get; set; }

        public int LocalPort { get; set; }

        public uint Ssrc { get; set; }

        public byte[] EncryptionKey { get; set; }

        public string EncryptionMode { get; set; }
    }
}
