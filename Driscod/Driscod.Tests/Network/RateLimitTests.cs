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
        public void RateLimit_RetryAfter(int[] statusSequence, string retryAfter)
        {
            var rateLimit = new RateLimit("ID");
            ResponseSequenceTest(rateLimit, statusSequence, null, null, null, retryAfter);
        }

        [Test]
        public void RateLimit_ResetAtSequence()
        {
            var rateLimit = new RateLimit("ID");
            ResponseSequenceTest(rateLimit, new[] { 200 }, null, null, "2", null);
            ResponseSequenceTest(rateLimit, new[] { 200 }, null, null, "1", null);
            ResponseSequenceTest(rateLimit, new[] { 200 }, null, null, "0", null);

            var stopwatch = Stopwatch.StartNew();
            ResponseSequenceTest(rateLimit, new[] { 429, 200 }, DateTime.Now.AddMilliseconds(50).Subtract(new DateTime(1970, 1, 1)).TotalSeconds.ToString(), null, "0", null);
            stopwatch.Stop();
            Assert.GreaterOrEqual(stopwatch.ElapsedMilliseconds, 50);
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
                    while (!firstEntered)
                    {
                        // intentionally empty.
                    }
                    var stopwatch = Stopwatch.StartNew();
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

        private void ResponseSequenceTest(RateLimit rateLimit, int[] statusSequence, string resetAt, string limit, string remaining, string retryAfter)
        {
            var callNumber = 0;

            rateLimit.LockAndWait(() =>
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