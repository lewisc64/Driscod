using System.IO;

namespace Driscod.Audio
{
    public interface IAudioSource
    {
        string Name { get; }

        Stream GetSampleStream(int sampleRate, int channels);
    }
}
