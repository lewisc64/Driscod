using Driscod.Audio;
using NUnit.Framework;
using System.Linq;

namespace Driscod.Tests.Audio
{
    public class RtpPacketGeneratorTests
    {
        [Test]
        public void RtpPacketGenerator_CreateHeader_Success()
        {
            Assert.AreEqual(
                new byte[] { 128, 120, 0, 1, 0, 0, 1, 0, 255, 255, 255, 255 },
                new RtpPacketGenerator()
                    .CreateHeader(1, 256, uint.MaxValue)
                    .Finalize());
        }

        [Test]
        public void RtpPacketGenerator_AddPayload_Success()
        {
            Assert.AreEqual(
                new byte[] { 5, 5, 5 },
                new RtpPacketGenerator()
                    .AddPayload(new byte[] { 5, 5, 5 })
                    .Finalize());
        }

        [Test]
        public void RtpPacketGenerator_EncryptPayload_Success()
        {
            Assert.AreEqual(
                new byte[] { 128, 120, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 19, 218, 101, 174, 20, 173, 201, 65, 165, 148, 208, 30, 36, 161, 97, 115, 8, 58, 93 },
                new RtpPacketGenerator()
                    .CreateHeader(0, 0, 0)
                    .AddPayload(new byte[] { 5, 5, 5 })
                    .EncryptPayload("xsalsa20_poly1305", Enumerable.Repeat((byte)0, 32).ToArray())
                    .Finalize());
        }
    }
}
