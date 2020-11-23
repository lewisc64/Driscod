using System;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Driscod.Audio
{
    public class YoutubeVideo : IAudioSource
    {
        private string _videoId;

        public YoutubeVideo(string videoId)
        {
            _videoId = videoId ?? throw new ArgumentNullException(nameof(videoId), $"You must specify a YouTube video ID or URL.");
        }

        public float[] GetSamples(int sampleRate, int channels)
        {
            var youtube = new YoutubeClient();
            var streamManifest = youtube.Videos.Streams.GetManifestAsync(_videoId).Result;
            var streamInfo = streamManifest.GetAudioOnly().WithHighestBitrate();

            return new AudioFile(streamInfo.Url).GetSamples(sampleRate, channels);
        }
    }
}
