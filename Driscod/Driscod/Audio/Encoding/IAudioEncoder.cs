namespace Driscod.Audio.Encoding
{
    public interface IAudioEncoder
    {
        void Setup(int sampleRate, int channels);

        byte[] Encode(float[] samples);
    }
}
