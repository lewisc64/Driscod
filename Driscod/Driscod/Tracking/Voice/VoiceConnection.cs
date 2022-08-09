using Driscod.Audio;
using Driscod.Gateway;
using Driscod.Tracking.Objects;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Tracking.Voice
{
    public class VoiceConnection : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string _channelId;

        private VoiceGateway VoiceGateway { get; set; }

        private IBot Bot { get; set; }

        private AudioStreamer AudioStreamer { get; set; }

        public string VoiceSessionId => VoiceGateway.SessionId;

        public bool Playing => AudioStreamer.TransmittingAudio;

        public bool Stale => !VoiceGateway.Running;

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public Guild Guild => Channel?.Guild;

        public EventHandler OnPlayAudio { get; set; }

        public EventHandler OnStopAudio { get; set; }

        internal VoiceConnection(Channel channel, VoiceGateway voice)
        {
            _channelId = channel.Id;

            Bot = channel.Bot;
            VoiceGateway = voice;

            VoiceGateway.OnStop += (a, b) =>
            {
                Disconnect();
            };

            if (!VoiceGateway.Running)
            {
                VoiceGateway.Start();
            }

            while (!VoiceGateway.Ready)
            {
                Thread.Sleep(200);
            }

            AudioStreamer = VoiceGateway.CreateAudioStreamer();

            AudioStreamer.OnAudioStart += (a, b) =>
            {
                OnPlayAudio?.Invoke(this, EventArgs.Empty);
            };

            AudioStreamer.OnAudioStop += (a, b) =>
            {
                OnStopAudio?.Invoke(this, EventArgs.Empty);
            };
        }

        public async Task PlayAudio(IAudioSource audioSource, CancellationToken cancellationToken = default)
        {
            ThrowIfStale();

            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = (a, b) =>
            {
                tcs.TrySetResult(true);
            };

            AudioStreamer.OnAudioStop += handler;

            var inControl = false;

            try
            {
                cancellationToken.Register(() =>
                {
                    if (inControl)
                    {
                        AudioStreamer.ClearAudio();
                    }
                });
                inControl = true;
                AudioStreamer.SendAudio(audioSource, cancellationToken: cancellationToken);
                AudioStreamer.QueueSilence();
                await tcs.Task;
            }
            finally
            {
                inControl = false;
                AudioStreamer.OnAudioStop -= handler;
            }
        }

        public Task StopAudio()
        {
            throw new NotImplementedException();
        }

        public Task Disconnect()
        {
            if (Guild == null)
            {
                Logger.Warn("Attempted to disconnect a voice connection, but was unable to locate the guild. Assuming connection has already closed.");
                return Task.CompletedTask;
            }

            lock (Guild.VoiceLock)
            {
                if (VoiceGateway != null && VoiceGateway.Running)
                {
                    VoiceGateway.Stop().Wait();
                }
                if (Guild.VoiceConnection == this)
                {
                    Guild.VoiceConnection = null;
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disconnect();
                AudioStreamer = null;
                VoiceGateway = null;
            }
        }

        private void ThrowIfStale()
        {
            if (Stale)
            {
                throw new InvalidOperationException("Connection object is stale.");
            }
        }
    }
}
