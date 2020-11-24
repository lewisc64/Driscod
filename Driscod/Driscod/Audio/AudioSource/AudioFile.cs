using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;
using System.Linq;

namespace Driscod.Audio
{
    public class AudioFile : IAudioSource
    {
        private readonly string _path;

        public string Name => _path.Split(new[] { '\\', '/' }).Last();

        public AudioFile(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path), $"'{nameof(path)}' cannot be null.");
        }

        public Stream GetSampleStream(int sampleRate, int channels)
        {
            if (channels != 1 && channels != 2)
            {
                throw new ArgumentOutOfRangeException(nameof(channels), "Only 1 or 2 channels are allowed.");
            }

            using (var reader = new MediaFoundationReader(_path))
            {
                ISampleProvider sampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), sampleRate);

                switch (channels)
                {
                    case 1:
                        sampler = sampler.ToMono();
                        break;
                    case 2:
                        sampler = sampler.ToStereo();
                        break;
                }

                return new SamplerWrapper(sampler);
            }
        }

        private class SamplerWrapper : Stream
        {
            private ISampleProvider _sampler;

            public SamplerWrapper(ISampleProvider sampler)
            {
                _sampler = sampler;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotImplementedException();

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var floatBuffer = new float[buffer.Length / 4];
                var floatsRead = _sampler.Read(floatBuffer, 0, count / 4);

                for (var i = 0; i < floatBuffer.Length; i++)
                {
                    var bytes = BitConverter.GetBytes(floatBuffer[i]);
                    bytes.CopyTo(buffer, i * 4);
                }

                return floatsRead * 4;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
    }
}
