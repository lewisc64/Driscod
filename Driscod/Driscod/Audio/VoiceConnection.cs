using Driscod.DiscordObjects;
using Driscod.Gateway;
using System;
using System.Threading.Tasks;

namespace Driscod.Audio
{
    public class VoiceConnection : IDisposable
    {
        private string _channelId;

        private Voice Voice { get; set; }

        private Bot Bot { get; set; }

        public bool Stale => Voice.Running;

        public Channel Channel => Bot.GetObject<Channel>(_channelId);

        public Guild Guild => Channel.Guild;

        internal VoiceConnection(Channel channel, Voice voice)
        {
            _channelId = channel.Id;

            Bot = channel.Bot;
            Voice = voice;
        }

        public async Task Play(IAudioSource audioSource)
        {
            await Voice.Play(audioSource);
        }

        public void PlaySync(IAudioSource audioSource)
        {
            ThrowIfStale();
            Play(audioSource).Wait();
        }

        public void Dispose()
        {
            lock (Guild.VoiceLock)
            {
                Voice.Stop();
                Guild.VoiceConnection = null;
            }
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
