using Driscod.Network;
using NUnit.Framework;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Tests.Network
{
    public class RateLimitTests
    {
        [TestCase(new[] { 200 }, null, null, null, null)]
        [TestCase(new[] { 429, 200 }, null, null, null, "50")]
        [TestCase(new[] { 429, 429, 429, 200 }, null, null, null, "0")]
        public void RateLimit_Wait(int[] statusSequence, string reset, string limit, string remaining, string retryAfter)
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

        [Test]
        public async Task RateLimit_Concurrency()
        {
            var rateLimit = new RateLimit("ID");

            bool firstCompleted = false;
            bool firstEntered = false;

            await Task.WhenAll(
                Task.Run(() =>
                {
                    var n = 0;
                    rateLimit.LockAndWait(() =>
                    {
                        firstEntered = true;

                        var response = new HttpResponseMessage();

                        n++;
                        if (n == 1)
                        {
                            response.StatusCode = (HttpStatusCode)429;
                            response.Headers.Add("Retry-After", "50");
                        }
                        if (n == 2)
                        {
                            response.StatusCode = HttpStatusCode.OK;
                            firstCompleted = true;
                        }

                        return response;
                    });
                }),
                Task.Run(() =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    while (!firstEntered)
                    {
                        // intentionally empty.
                    }
                    rateLimit.LockAndWait(() =>
                    {
                        stopwatch.Stop();
                        Assert.GreaterOrEqual(stopwatch.Elapsed.TotalMilliseconds, 50);
                        Assert.IsTrue(firstCompleted);
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK
                        };
                    });
                }));
        }
    }
}