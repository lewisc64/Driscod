namespace Driscod.Audio.Encoding
{
    public interface IAudioEncoder
    {
        byte[] Encode(float[] samples);
    }
}
