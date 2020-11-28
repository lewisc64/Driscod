﻿using MongoDB.Bson;

namespace Driscod.Extensions
{
    public static class BsonDocumentExtensions
    {
        public static BsonValue GetValueOrNull(this BsonDocument doc, string key)
        {
            return !doc.Contains(key) || doc[key].IsBsonNull ? null : doc[key];
        }
    }
}
