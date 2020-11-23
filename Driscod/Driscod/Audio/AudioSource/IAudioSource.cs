namespace Driscod.Audio
{
    public interface IAudioSource
    {
        // TODO: Use streams.
        float[] GetSamples(int sampleRate, int channels);
    }
}
