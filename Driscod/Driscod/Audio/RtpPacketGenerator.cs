using Driscod.Extensions;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Driscod.Audio
{
    public class RtpPacketGenerator
    {
        public static readonly string[] SupportedEncryptionModes = new[] { "xsalsa20_poly1305", "xsalsa20_poly1305_suffix" };

        private static readonly Random Random = new Random();

        private int _headerLength = 0;

        private int _payloadStart = 0;

        private int _payloadLength = 0;

        private List<byte> Packet { get; set; } = new List<byte>();

        public byte[] Finalize()
        {
            return Packet.ToArray();
        }

        public RtpPacketGenerator CreateHeader(ushort sequence, uint timestamp, uint ssrc)
        {
            var header = new byte[12];

            header[0] = 0x80;
            header[1] = 0x78;

            sequence.ToBytesBigEndian().CopyTo(header, 2);
            timestamp.ToBytesBigEndian().CopyTo(header, 4);
            ssrc.ToBytesBigEndian().CopyTo(header, 8);

            Packet.AddRange(header);

            _headerLength = header.Length;
            _payloadStart = Packet.Count;

            return this;
        }

        public RtpPacketGenerator AddPayload(byte[] payload)
        {
            Packet.InsertRange(_payloadStart, payload);
            _payloadLength = payload.Length;

            return this;
        }

        public RtpPacketGenerator EncryptPayload(string mode, byte[] key)
        {
            byte[] payload = RemovePayload();
            var nonce = new byte[24];

            switch (mode)
            {
                case "xsalsa20_poly1305":
                    Packet.GetRange(0, _headerLength).CopyTo(nonce, 0);
                    payload = EncryptXSalsa20Poly1305(payload, key, nonce);
                    break;

                case "xsalsa20_poly1305_suffix":
                    Random.NextBytes(nonce);
                    payload = EncryptXSalsa20Poly1305(payload, key, nonce)
                        .Concat(nonce)
                        .ToArray();
                    break;

                default:
                    throw new ArgumentException($"Encryption mode '{mode}' is not supported.", nameof(mode));
            }

            return AddPayload(payload);
        }

        private byte[] RemovePayload()
        {
            var payload = Packet.GetRange(_payloadStart, _payloadLength).ToArray();
            Packet.RemoveRange(_payloadStart, _payloadLength);
            return payload;
        }

        private byte[] EncryptXSalsa20Poly1305(byte[] bytes, byte[] key, byte[] nonce)
        {
            var salsa = new XSalsa20Engine();
            var poly = new Poly1305();

            salsa.Init(true, new ParametersWithIV(new KeyParameter(key), nonce));

            byte[] subKey = new byte[key.Length];
            salsa.ProcessBytes(subKey, 0, key.Length, subKey, 0);

            byte[] output = new byte[bytes.Length + poly.GetMacSize()];

            salsa.ProcessBytes(bytes, 0, bytes.Length, output, poly.GetMacSize());

            poly.Init(new KeyParameter(subKey));
            poly.BlockUpdate(output, poly.GetMacSize(), bytes.Length);
            poly.DoFinal(output, 0);

            return output;
        }
    }
}
