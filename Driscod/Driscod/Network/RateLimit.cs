using Driscod.Extensions;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Network
{
    public class RateLimit
    {
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public string Id { get; private set; }

        public int Max { get; private set; }

        public int Remaining { get; private set; }

        public DateTime? ResetAt { get; private set; }

        public RateLimit(string id)
        {
            Id = id;
        }

        public void UpdateFromHeaders(HttpResponseHeaders headers)
        {
            if (headers.Contains("X-RateLimit-Reset"))
            {
                ResetAt = new DateTime(1970, 1, 1).AddSeconds(double.Parse(headers.GetFirstValue("X-RateLimit-Reset")));
            }
            if (headers.Contains("X-RateLimit-Limit"))
            {
                Max = int.Parse(headers.GetFirstValue("X-RateLimit-Limit"));
            }
            if (headers.Contains("X-RateLimit-Remaining"))
            {
                Remaining = int.Parse(headers.GetFirstValue("X-RateLimit-Remaining"));
            }
        }

        public async Task PerformRequest(Func<Task<HttpResponseMessage>> callback)
        {
            await semaphore.WaitAsync();

            try
            {
                HttpResponseMessage response = null;
                int retryAfter = -1;
                do
                {
                    if (retryAfter != -1)
                    {
                        await Task.Delay(retryAfter);
                    }
                    if (Remaining == 0 && ResetAt.HasValue && ResetAt > DateTime.UtcNow)
                    {
                        await Task.Delay(ResetAt.Value - DateTime.UtcNow);
                    }

                    try
                    {
                        response = await callback();
                    }
                    finally
                    {
                        if (response is not null)
                        {
                            UpdateFromHeaders(response.Headers);
                        }
                    }
                    if (response.StatusCode != HttpStatusCode.TooManyRequests)
                    {
                        response.EnsureSuccessStatusCode();
                    }

                    retryAfter = int.Parse(response.Headers.GetFirstValueOrNull("Retry-After") ?? "-1");
                }
                while (response.StatusCode == HttpStatusCode.TooManyRequests);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
