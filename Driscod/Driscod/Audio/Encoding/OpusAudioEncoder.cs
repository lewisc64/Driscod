using Concentus.Enums;
using Concentus.Structs;
using System.Linq;

namespace Driscod.Audio.Encoding;

public class OpusAudioEncoder : IAudioEncoder
{
    private readonly OpusEncoder _encoder;

    public int SampleRate { get; }

    public int Channels { get; }

    public OpusAudioEncoder(int sampleRate, int channels)
    {
        SampleRate = sampleRate;
        Channels = channels;
        _encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
    }

    public byte[] Encode(float[] samples)
    {
        var opusPacket = new byte[samples.Length];
        int opusPacketSize = _encoder.Encode(samples, 0, samples.Length / Channels, opusPacket, 0, opusPacket.Length);
        return opusPacket.Take(opusPacketSize).ToArray();
    }
}
