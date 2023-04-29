namespace Driscod.Extensions;

public static class NumberExtensions
{
    public static byte[] ToBytesBigEndian(this ushort n)
    {
        var bytes = new byte[2];
        bytes[0] = (byte)(n >> 8);
        bytes[1] = (byte)n;
        return bytes;
    }

    public static byte[] ToBytesBigEndian(this uint n)
    {
        var bytes = new byte[4];
        bytes[0] = (byte)(n >> 3 * 8);
        bytes[1] = (byte)(n >> 2 * 8);
        bytes[2] = (byte)(n >> 8);
        bytes[3] = (byte)n;
        return bytes;
    }
}
