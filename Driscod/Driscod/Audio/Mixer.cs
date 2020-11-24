using Driscod.DiscordObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Driscod.Audio
{
    public class Mixer
    {
        private string _guildId;

        private Bot Bot { get; set; }

        private Guild Guild => Bot.GetObject<Guild>(_guildId);

        private VoiceConnection VoiceConnection => Guild.VoiceConnection;

        private Queue<MusicQueueItem> MusicQueue { get; set; } = new Queue<MusicQueueItem>();

        public IEnumerable<string> QueueAsNames => MusicQueue.Select(x => x.AudioSource.Name);

        public Mixer(Guild guild)
        {
            if (guild.VoiceConnection == null || guild.VoiceConnection.Stale)
            {
                throw new ArgumentException("Not connected to a voice channel in specified guild.", nameof(guild));
            }

            Bot = guild.Bot;
            _guildId = guild.Id;

            CreateListeners();
        }

        public void AddToQueue(IAudioSource audioSource)
        {
            AddToQueue(new MusicQueueItem
            {
                AudioSource = audioSource,
            });
        }

        public void AddToQueue(MusicQueueItem musicQueueItem)
        {
            MusicQueue.Enqueue(musicQueueItem);

            if (MusicQueue.Count == 1)
            {
                PlayNext();
            }
        }

        public void Skip()
        {
            VoiceConnection.StopAudio();
        }

        private void PlayNext()
        {
            VoiceConnection.PlayAudio(MusicQueue.Peek().AudioSource).Forget();
        }

        private void CreateListeners()
        {
            VoiceConnection.OnStopAudio += (a, b) =>
            {
                lock (MusicQueue)
                {
                    MusicQueue.Dequeue();
                    if (MusicQueue.Count > 0)
                    {
                        PlayNext();
                    }
                }
            };
        }
    }

    public struct MusicQueueItem
    {
        public IAudioSource AudioSource { get; set; }
    }
}
