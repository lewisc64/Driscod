using Driscod.Audio;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Driscod.Tests.Audio
{
    public class DriftTimerTests
    {
        [TestCase(200, 20)]
        [TestCase(100, 90)]
        public async Task DriftTimer_Success(int measureTime, int interval)
        {
            var timer = new DriftTimer(TimeSpan.FromMilliseconds(interval));

            var count = 0;

            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < measureTime)
            {
                await timer.Wait();
                count++;
            }

            var expectedCount = measureTime / interval;

            Assert.GreaterOrEqual(count, expectedCount - 1);
            Assert.LessOrEqual(count, expectedCount + 1);
        }
    }
}
