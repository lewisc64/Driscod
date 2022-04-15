using Concentus.Enums;
using Concentus.Structs;
using System.Linq;

namespace Driscod.Audio.Encoding
{
    public class OpusAudioEncoder : IAudioEncoder
    {
        private int _channels;

        private OpusEncoder encoder;

        public void Setup(int sampleRate, int channels)
        {
            _channels = channels;
            encoder = OpusEncoder.Create(sampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        }

        public byte[] Encode(float[] samples)
        {
            var opusPacket = new byte[samples.Length];
            int opusPacketSize = encoder.Encode(samples, 0, samples.Length / _channels, opusPacket, 0, opusPacket.Length);
            return opusPacket.Take(opusPacketSize).ToArray();
        }
    }
}
