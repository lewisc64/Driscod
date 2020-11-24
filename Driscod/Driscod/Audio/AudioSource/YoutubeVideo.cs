using System;
using System.Web;
using System.Linq;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Driscod.Audio
{
    public class YoutubeVideo : IAudioSource
    {
        private string _videoId;

        private string _streamUrl;

        public YoutubeVideo(string videoId)
        {
            if (videoId != null && videoId.Contains("youtube.com"))
            {
                _videoId = HttpUtility.ParseQueryString(videoId.Split(new[] { '?' }).Last())["v"];
            }
            else
            {
                _videoId = videoId ?? throw new ArgumentNullException(nameof(videoId), "You must specify a YouTube video ID or URL.");
            }

            FetchStreamUrl();
        }

        public float[] GetSamples(int sampleRate, int channels)
        {
            return new AudioFile(_streamUrl).GetSamples(sampleRate, channels);
        }

        private void FetchStreamUrl()
        {
            var youtube = new YoutubeClient();
            var streamManifest = youtube.Videos.Streams.GetManifestAsync(_videoId).Result;
            var streamInfo = streamManifest.GetAudioOnly().WithHighestBitrate();

            _streamUrl = streamInfo.Url;
        }
    }
}
