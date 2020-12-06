using Driscod.Network;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Driscod.Tests.Network
{
    public class RateLimitTests
    {
        [TestCase(new[] { 200 }, null)]
        [TestCase(new[] { 429, 200 }, "50")]
        [TestCase(new[] { 429, 429, 429, 200 }, "0")]
        public async Task RateLimit_PerformRequest_RetryAfter(int[] statusSequence, string retryAfter)
        {
            var rateLimit = new RateLimit("ID");
            await ResponseSequenceTest(rateLimit, statusSequence, null, null, null, retryAfter);
        }

        [Test]
        public async Task RateLimit_PerformRequest_ResetAtSequence()
        {
            var rateLimit = new RateLimit("ID");
            await ResponseSequenceTest(rateLimit, new[] { 200 }, null, null, "2", null);
            await ResponseSequenceTest(rateLimit, new[] { 200 }, null, null, "1", null);

            var future = DateTime.UtcNow.AddMilliseconds(50);
            await ResponseSequenceTest(rateLimit, new[] { 200 }, future.Subtract(new DateTime(1970, 1, 1)).TotalSeconds.ToString(), null, "0", null);
            await ResponseSequenceTest(rateLimit, new[] { 200 }, null, null, "2", null);

            Assert.GreaterOrEqual(DateTime.UtcNow, future.Subtract(TimeSpan.FromMilliseconds(5)));
        }

        [Test]
        public async Task RateLimit_PerformRequest_Concurrency()
        {
            var rateLimit = new RateLimit("ID");

            bool firstCompleted = false;
            bool firstEntered = false;

            await Task.WhenAll(
                Task.Run(async () =>
                {
                    var n = 0;
                    await rateLimit.PerformRequest(() =>
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
                Task.Run(async () =>
                {
                    while (!firstEntered)
                    {
                        // intentionally empty.
                    }
                    var stopwatch = Stopwatch.StartNew();
                    await rateLimit.PerformRequest(() =>
                    {
                        stopwatch.Stop();
                        Assert.GreaterOrEqual(stopwatch.Elapsed.TotalMilliseconds, 50 - 2);
                        Assert.IsTrue(firstCompleted);
                        return new HttpResponseMessage
                        {
                            StatusCode = HttpStatusCode.OK
                        };
                    });
                }));
        }

        private async Task ResponseSequenceTest(RateLimit rateLimit, int[] statusSequence, string resetAt, string limit, string remaining, string retryAfter)
        {
            var callNumber = 0;

            await rateLimit.PerformRequest(() =>
            {
                var response = new HttpResponseMessage
                {
                    StatusCode = (HttpStatusCode)statusSequence[callNumber],
                };

                if (resetAt != null)
                {
                    response.Headers.Add("X-RateLimit-Reset", resetAt);
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