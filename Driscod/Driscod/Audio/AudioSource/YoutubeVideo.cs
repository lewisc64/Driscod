using System;
using System.Net.Http;
using System.Web;
using System.Linq;
using MongoDB.Bson;

namespace Driscod.Audio
{
    public class YoutubeVideo : IAudioSource
    {
        private string _videoId;

        public YoutubeVideo(string videoId)
        {
            _videoId = videoId ?? throw new ArgumentNullException(nameof(videoId), $"You must specify a YouTube video ID.");
        }

        public float[] GetSamples(int sampleRate, int channels)
        {
            var client = new HttpClient();

            var response = client.GetAsync(string.Format(Connectivity.YoutubeVideoInfoRequestUrlFormat, _videoId)).Result;
            var content = HttpUtility.UrlDecode(response.Content.ReadAsStringAsync().Result);

            var doc = BsonDocument.Parse(HttpUtility.ParseQueryString(content)["player_response"]);

            var streamDoc = doc["streamingData"].AsBsonDocument["formats"].AsBsonArray
                .Select(x => x.AsBsonDocument)
                .OrderBy(x => x["bitrate"].AsInt32)
                .Last();

            return new AudioFile(streamDoc["url"].AsString).GetSamples(sampleRate, channels);
        }
    }
}
