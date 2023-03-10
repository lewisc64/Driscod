using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Driscod.Audio
{
    public class YoutubeVideo : IAudioSource
    {
        private readonly YoutubeClient _client;
        private readonly string _videoId;

        public string Name { get; }

        private YoutubeVideo(string videoId, string name)
        {
            _videoId = videoId ?? throw new ArgumentNullException(nameof(videoId), "You must specify a YouTube video ID or URL.");
            _client = new YoutubeClient();
            Name = name;
        }

        public async Task<Stream> GetSampleStream(int sampleRate, int channels)
        {
            var streamManifest = await _client.Videos.Streams.GetManifestAsync(_videoId);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            return await new AudioFile(streamInfo.Url).GetSampleStream(sampleRate, channels);
        }

        public static async Task<YoutubeVideo> Create(string videoId)
        {
            var youtubeClient = new YoutubeClient();
            var video = await youtubeClient.Videos.GetAsync(videoId);
            return new YoutubeVideo(videoId, video.Title);
        }

        public static async IAsyncEnumerable<YoutubeVideo> CreateFromPlaylist(string playlistId)
        {
            var youtubeClient = new YoutubeClient();
            await foreach (var video in youtubeClient.Playlists.GetVideosAsync(playlistId))
            {
                yield return new YoutubeVideo(video.Id, video.Title);
            }
        }
    }
}
