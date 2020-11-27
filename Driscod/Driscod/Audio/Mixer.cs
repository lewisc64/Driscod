using Driscod.DiscordObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Driscod.Audio
{
    public class Mixer : IDisposable
    {
        private string _channelId;

        private bool _disposed = false;

        private Bot Bot { get; set; }

        private VoiceConnection VoiceConnection
        {
            get
            {
                if (Guild.VoiceConnection == null || Guild.VoiceConnection.Stale)
                {
                    Channel.ConnectVoice();
                }

                return Guild.VoiceConnection;
            }
        }

        private Queue<MusicQueueItem> InternalMusicQueue { get; set; } = new Queue<MusicQueueItem>();

        private Guild Guild => Channel.Guild;

        public Channel Channel
        {
            get
            {
                return Bot.GetObject<Channel>(_channelId);
            }

            set
            {
                _channelId = value.Id;
            }
        }

        public IEnumerable<string> QueueAsNames => InternalMusicQueue.Select(x => x.AudioSource.Name);

        public IReadOnlyCollection<MusicQueueItem> MusicQueue => InternalMusicQueue;

        public MusicQueueItem CurrentlyPlaying => MusicQueue.FirstOrDefault();

        public MusicQueueItem Next => MusicQueue.Skip(1).FirstOrDefault();

        public event EventHandler<MusicQueueItem> OnMusicAdded;

        public event EventHandler<MusicQueueItem> OnMusicPlay;

        public Mixer(Channel channel)
        {
            Bot = channel.Bot;
            Channel = channel;

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
            ThrowIfDisposed();

            AddToQueue(new MusicQueueItem
            {
                AudioSource = audioSource,
            });
        }

        public void AddToQueue(MusicQueueItem musicQueueItem)
        {
            ThrowIfDisposed();

            InternalMusicQueue.Enqueue(musicQueueItem);

            OnMusicAdded?.Invoke(this, musicQueueItem);

            if (InternalMusicQueue.Count == 1)
            {
                PlayNext();
            }
        }

        public void Skip()
        {
            ThrowIfDisposed();

            VoiceConnection.StopAudio();
        }

        public void Dispose()
        {
            _disposed = true;

            lock (InternalMusicQueue)
            {
                InternalMusicQueue.Clear();
            }
            Guild.VoiceConnection?.Dispose();
        }

        private void PlayNext()
        {
            var item = InternalMusicQueue.Peek();
            OnMusicPlay?.Invoke(this, item);
            VoiceConnection.PlayAudio(item.AudioSource).Forget();
        }

        private void CreateListeners()
        {
            VoiceConnection.OnStopAudio += (a, b) =>
            {
                lock (InternalMusicQueue)
                {
                    if (InternalMusicQueue.Any())
                    {
                        InternalMusicQueue.Dequeue();
                        if (InternalMusicQueue.Any())
                        {
                            PlayNext();
                        }
                    }
                }
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Mixer));
            }
        }
    }

    public struct MusicQueueItem
    {
        public IAudioSource AudioSource { get; set; }
    }
}
