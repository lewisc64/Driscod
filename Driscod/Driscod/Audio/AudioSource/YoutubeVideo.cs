using System;
using System.Web;
using System.Linq;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.Threading.Tasks;
using System.IO;

namespace Driscod.Audio
{
    public class YoutubeVideo : IAudioSource
    {
        private string _videoId;

        private YoutubeExplode.Videos.Video Video { get; set; }

        private IStreamInfo StreamInfo { get; set; }

        public string Name => Video.Title;

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

            FetchStreamInfo().Wait();
        }

        public Stream GetSampleStream(int sampleRate, int channels)
        {
            return new AudioFile(StreamInfo.Url).GetSampleStream(sampleRate, channels);
        }

        private async Task FetchStreamInfo()
        {
            var youtube = new YoutubeClient();
            Video = await youtube.Videos.GetAsync(_videoId);

            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(_videoId);
            StreamInfo = streamManifest.GetAudioOnly().WithHighestBitrate();
        }
    }
}
