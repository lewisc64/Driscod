using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Driscod.Audio
{
    public class YoutubeVideo : IAudioSource
    {
        private readonly string _videoId;

        private Video Video { get; set; }

        private IStreamInfo StreamInfo { get; set; }

        public string Name => Video.Title;

        public YoutubeVideo(string videoId)
        {
            _videoId = videoId ?? throw new ArgumentNullException(nameof(videoId), "You must specify a YouTube video ID or URL.");
            FetchStreamInfo().Wait();
        }

        public Stream GetSampleStream(int sampleRate, int channels)
        {
            return new AudioFile(StreamInfo.Url).GetSampleStream(sampleRate, channels);
        }

        public static IEnumerable<YoutubeVideo> CreateFromPlaylist(string playlistId)
        {
            var youtube = new YoutubeClient();
            var videos = youtube.Playlists.GetVideosAsync(playlistId).GetAwaiter().GetResult();

            foreach (var video in videos)
            {
                YoutubeVideo source;
                try
                {
                    source = new YoutubeVideo(video.Id);
                }
                catch
                {
                    // ignored, probably unavailable.
                    continue;
                }

                yield return source;
            }
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
