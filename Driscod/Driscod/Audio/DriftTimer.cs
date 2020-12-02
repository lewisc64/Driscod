using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Audio
{
    internal class DriftTimer
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private TimeSpan _interval;

        private double _drift = 0;

        private double IntervalMilliseconds => _interval.TotalMilliseconds;

        public DriftTimer(TimeSpan interval)
        {
            _interval = interval;
        }

        public async Task Wait(CancellationToken cancellationToken = default)
        {
            if (!_stopwatch.IsRunning)
            {
                _stopwatch.Start();
            }

            var delay = (int)Math.Max(IntervalMilliseconds - _stopwatch.Elapsed.TotalMilliseconds + _drift, 0);
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }

            _drift += IntervalMilliseconds - _stopwatch.Elapsed.TotalMilliseconds;
            _stopwatch.Restart();
        }
    }
}
