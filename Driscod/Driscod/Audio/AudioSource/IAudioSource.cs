namespace Driscod.Audio
{
    public interface IAudioSource
    {
        float[] GetSamples(int sampleRate, int channels);
    }
}
