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
        private readonly string _channelId;

        private VoiceGateway VoiceGateway { get; set; }

        private IBot Bot { get; set; }

        private AudioStreamer AudioStreamer { get; set; }

        public string VoiceSessionId => VoiceGateway.SessionId;

        public bool Playing => AudioStreamer.SendingAudio;

        public bool Stale => !VoiceGateway.Running;

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public Guild Guild => Channel.Guild;

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
                await tcs.Task;
            }
            finally
            {
                inControl = false;
                AudioStreamer.OnAudioStop -= handler;
            }
        }

        public void PlayAudioSync(IAudioSource audioSource, CancellationToken cancellationToken = default)
        {
            ThrowIfStale();
            PlayAudio(audioSource, cancellationToken: cancellationToken).Wait();
        }

        public void StopAudio()
        {
            throw new NotImplementedException();
        }

        public void Disconnect()
        {
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
