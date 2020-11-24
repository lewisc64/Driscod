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
            var client = new HttpClient();

            var response = client.GetAsync(string.Format(Connectivity.YoutubeVideoInfoRequestUrlFormat, _videoId)).Result;
            var content = HttpUtility.UrlDecode(response.Content.ReadAsStringAsync().Result);

            var doc = BsonDocument.Parse(HttpUtility.ParseQueryString(content)["player_response"]);

            if (doc["playabilityStatus"]["status"].AsString == "UNPLAYABLE")
            {
                throw new InvalidOperationException($"Video is unplayable: {doc["playabilityStatus"]["reason"].AsString}");
            }

            var streamDoc = doc["streamingData"]["formats"].AsBsonArray
                .Select(x => x.AsBsonDocument)
                .OrderBy(x => x["bitrate"].AsInt32)
                .Last();

            _streamUrl = streamDoc["url"].AsString;
        }
    }
}
