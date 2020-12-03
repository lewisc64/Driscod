using NUnit.Framework;
using System.Net;
using System.Net.Http;

namespace Driscod.Tests
{
    public class RateLimitTests
    {
        [TestCase(new[] { 200 }, null, null, null, null)]
        [TestCase(new[] { 429, 200 }, null, null, null, "50")]
        [TestCase(new[] { 429, 429, 429, 200 }, null, null, null, "0")]
        public void RateLimit_Success(int[] statusSequence, string reset, string limit, string remaining, string retryAfter)
        {
            var callNumber = 0;

            var rateLimit = new RateLimit("ID");
            rateLimit.LockAndWait(() =>
            {
                var response = new HttpResponseMessage
                {
                    StatusCode = (HttpStatusCode)statusSequence[callNumber],
                };

                if (reset != null)
                {
                    response.Headers.Add("X-RateLimit-Reset", reset);
                }

                if (limit != null)
                {
                    response.Headers.Add("X-RateLimit-Limit", limit);
                }

                if (remaining != null)
                {
                    response.Headers.Add("X-RateLimit-Remaining", remaining);
                }

                if (retryAfter != null)
                {
                    response.Headers.Add("Retry-After", retryAfter);
                }

                callNumber++;
                return response;
            });

            Assert.AreEqual(statusSequence.Length, callNumber, "Callback should be called enough times.");
        }
    }
}