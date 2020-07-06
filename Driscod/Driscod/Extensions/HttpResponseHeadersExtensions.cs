using System.Linq;
using System.Net.Http.Headers;

namespace Driscod
{
    public static class HttpResponseHeadersExtensions
    {
        public static string GetFirstValueOrNull(this HttpResponseHeaders headers, string key)
        {
            var header = headers.FirstOrDefault(x => x.Key == key);
            return header.Key == null ? null : header.Value.FirstOrDefault();
        }
        public static string GetFirstValue(this HttpResponseHeaders headers, string key)
        {
            return headers.First(x => x.Key.ToLower() == key.ToLower()).Value.First();
        }
    }
}
