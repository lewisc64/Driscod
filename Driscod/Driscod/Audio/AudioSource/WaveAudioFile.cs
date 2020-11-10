using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Driscod.Audio
{
    public class WaveAudioFile : IAudioSource
    {
        private readonly string _path;

        public WaveAudioFile(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path), $"'{nameof(path)}' cannot be null.");
        }

        public float[] GetSamples(int sampleRate, int channels)
        {
            if (channels != 1 && channels != 2)
            {
                throw new ArgumentOutOfRangeException(nameof(channels), "Only 1 or 2 channels are allowed.");
            }

            var samples = new List<float>();

            using (var reader = new AudioFileReader(_path))
            {
                ISampleProvider sampler = new WdlResamplingSampleProvider(reader, sampleRate);

                switch (channels)
                {
                    case 1:
                        sampler = sampler.ToMono();
                        break;
                    case 2:
                        sampler = sampler.ToStereo();
                        break;
                }

                int samplesRead = -1;
                while (samplesRead != 0)
                {
                    var buffer = new float[4000];
                    samplesRead = sampler.Read(buffer, 0, buffer.Length);
                    samples.AddRange(buffer.Take(samplesRead));
                }
            }

            return samples.ToArray();
        }
    }
}
