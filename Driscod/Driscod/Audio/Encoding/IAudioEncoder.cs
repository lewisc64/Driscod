namespace Driscod.Audio.Encoding;

public interface IAudioEncoder
{
    int SampleRate { get; }

    int Channels { get; }

    byte[] Encode(float[] samples);
}
