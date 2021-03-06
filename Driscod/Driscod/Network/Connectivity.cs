﻿using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace Driscod.Network
{
    public static class Connectivity
    {
        public const string HttpApiEndpoint = "https://discordapp.com/api/v8";

        public const string ChannelMessagesPathFormat = "channels/{0}/messages";

        public const string ChannelMessagePathFormat = "channels/{0}/messages/{1}";

        public const int GatewayEventsPerMinute = 120 - 20; // slightly less for safety

        public static string WebSocketEndpoint
        {
            get
            {
                var client = new HttpClient();

                var responseContent = client.GetAsync($"{HttpApiEndpoint}/gateway").Result.Content.ReadAsStringAsync().Result;
                var doc = JObject.Parse(responseContent);

                return doc["url"].ToObject<string>();
            }
        }

        public static string FormatVoiceSocketEndpoint(string url)
        {
            return $"wss://{url}/?v=4";
        }
    }
}
