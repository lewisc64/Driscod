﻿using MongoDB.Bson;
using System.Net.Http;

namespace Driscod
{
    public static class Connectivity
    {
        public const string HttpApiEndpoint = "https://discordapp.com/api/v8";

        public const string ChannelMessagePathFormat = "channels/{0}/messages";

        public const int GatewayEventsPerMinute = 120 - 20; // slightly less for safety

        public static string WebSocketEndpoint
        {
            get
            {
                var client = new HttpClient();

                var responseContent = client.GetAsync($"{HttpApiEndpoint}/gateway").Result.Content.ReadAsStringAsync().Result;
                var doc = BsonDocument.Parse(responseContent);

                return doc["url"].AsString;
            }
        }

        public static string FormatVoiceSocketEndpoint(string url)
        {
            return $"wss://{url}/?v=4";
        }
    }
}
