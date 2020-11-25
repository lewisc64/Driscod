using Driscod.DiscordObjects;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public event EventHandler<MusicQueueItem> OnMusicAdded;

        public event EventHandler<MusicQueueItem> OnMusicPlay;

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

        public void AddToQueue(IEnumerable<IAudioSource> audioSources)
        {
            foreach (var audioSource in audioSources)
            {
                AddToQueue(audioSource);
            }
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

            OnMusicAdded?.Invoke(this, musicQueueItem);

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
            var item = MusicQueue.Peek();
            OnMusicPlay?.Invoke(this, item);
            VoiceConnection.PlayAudio(item.AudioSource).Forget();
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
