using Driscod.Extensions;
using NUnit.Framework;

namespace Driscod.Tests.Extensions
{
    public class NumberExtensionsTests
    {
        [TestCase((ushort)0, new byte[] { 0, 0 })]
        [TestCase(ushort.MaxValue, new byte[] { 255, 255 })]
        [TestCase((ushort)1, new byte[] { 0, 1 })]
        public void NumberExtensions_UnsignedShortToBytes(ushort value, byte[] expected)
        {
            Assert.AreEqual(expected, value.ToBytesBigEndian());
        }

        [TestCase(0U, new byte[] { 0, 0, 0, 0 })]
        [TestCase(uint.MaxValue, new byte[] { 255, 255, 255, 255 })]
        [TestCase(1U, new byte[] { 0, 0, 0, 1 })]
        public void NumberExtensions_UnsignedIntegerToBytes(uint value, byte[] expected)
        {
            Assert.AreEqual(expected, value.ToBytesBigEndian());
        }
    }
}
