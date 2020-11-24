using Driscod.DiscordObjects;
using Driscod.Gateway;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Audio
{
    public class VoiceConnection : IDisposable
    {
        private string _channelId;

        private Voice Voice { get; set; }

        private Bot Bot { get; set; }

        public bool Playing => Voice.AudioStreamer.Playing;

        public bool Stale => !Voice.Running;

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public Guild Guild => Channel.Guild;

        public EventHandler OnPlayAudio;

        public EventHandler OnStopAudio;

        internal VoiceConnection(Channel channel, Voice voice)
        {
            _channelId = channel.Id;

            Bot = channel.Bot;
            Voice = voice;

            Voice.OnStop += (a, b) =>
            {
                Disconnect();
            };

            if (!Voice.Running)
            {
                Voice.Start();
            }

            while (!Voice.Ready)
            {
                Thread.Sleep(200);
            }

            Voice.AudioStreamer.OnAudioStart += (a, b) =>
            {
                OnPlayAudio?.Invoke(this, null);
            };

            Voice.AudioStreamer.OnAudioStop += (a, b) =>
            {
                OnStopAudio?.Invoke(this, null);
            };
        }

        public async Task PlayAudio(IAudioSource audioSource)
        {
            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = (a, b) =>
            {
                tcs.SetResult(true);
            };

            Voice.AudioStreamer.OnAudioStop += handler;

            try
            {
                Voice.AudioStreamer.SendAudio(audioSource);
                await tcs.Task;
            }
            finally
            {
                Voice.AudioStreamer.OnAudioStop -= handler;
            }
        }

        public void PlayAudioSync(IAudioSource audioSource)
        {
            ThrowIfStale();
            PlayAudio(audioSource).Wait();
        }

        public void StopAudio()
        {
            Voice.AudioStreamer.ClearAudio();
        }

        public void Disconnect()
        {
            lock (Guild.VoiceLock)
            {
                if (Voice.Running)
                {
                    Voice.Stop();
                }
                Guild.VoiceConnection = null;
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        internal void DisposeIfStale()
        {
            if (Stale)
            {
                Dispose();
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
