using System.IO;
using System.Threading.Tasks;

namespace Driscod.Audio
{
    public interface IAudioSource
    {
        string Name { get; }

        Task<Stream> GetSampleStream(int sampleRate, int channels);
    }
}
