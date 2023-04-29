using System.IO;
using System.Threading.Tasks;

namespace Driscod.Audio.AudioSource;

public interface IAudioSource
{
    string Name { get; }

    Task<Stream> GetSampleStream(int sampleRate, int channels);
}
