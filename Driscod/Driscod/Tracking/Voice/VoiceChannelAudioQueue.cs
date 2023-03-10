﻿using Driscod.Audio;
using Driscod.Extensions;
using Driscod.Tracking.Objects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Tracking.Voice
{
    public class VoiceChannelAudioQueue : IDisposable
    {
        private string _channelId;
        private bool _disposed = false;
        private CancellationTokenSource _playingCancellationTokenSource = new();
        private ConcurrentQueue<MusicQueueItem> _internalMusicQueue = new ConcurrentQueue<MusicQueueItem>();
        private Task audioPlayTask = null;

        public event EventHandler<MusicQueueItem> OnAudioAdded;
        public event EventHandler<MusicQueueItem> OnAudioPlay;
        public event EventHandler OnQueueEmpty;

        public VoiceChannelAudioQueue(Channel channel)
        {
            Bot = channel.Bot;
            Channel = channel;
        }

        private IBot Bot { get; set; }

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

        private Guild Guild => Channel.Guild;

        public Channel Channel
        {
            get
            {
                ThrowIfDisposed();
                return Bot.GetObject<Channel>(_channelId);
            }

            set
            {
                _channelId = value.Id;
            }
        }

        public IEnumerable<string> QueueAsNames => _internalMusicQueue.Select(x => x.AudioSource.Name);

        public IReadOnlyCollection<MusicQueueItem> MusicQueue => _internalMusicQueue;

        public MusicQueueItem CurrentlyPlaying => MusicQueue.FirstOrDefault();

        public MusicQueueItem Next => MusicQueue.Skip(1).FirstOrDefault();

        public void AddToQueue(IEnumerable<IAudioSource> audioSources)
        {
            ThrowIfDisposed();

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

            _internalMusicQueue.Enqueue(musicQueueItem);

            OnAudioAdded?.Invoke(this, musicQueueItem);

            if (_internalMusicQueue.Count == 1)
            {
                PlayNext();
            }
        }

        public void Skip()
        {
            ThrowIfDisposed();

            _playingCancellationTokenSource.Cancel();
            _playingCancellationTokenSource = new CancellationTokenSource();
        }

        public void ClearQueue()
        {
            ThrowIfDisposed();

            _internalMusicQueue.Clear();
            _playingCancellationTokenSource.Cancel();
            _playingCancellationTokenSource = new CancellationTokenSource();
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
                _internalMusicQueue.Clear();
                _playingCancellationTokenSource.Cancel();
                audioPlayTask.Wait();
                _playingCancellationTokenSource = null;
                _internalMusicQueue = null;
                Guild.VoiceConnection?.Dispose();
                Bot = null;
                _disposed = true;
            }
        }

        private void PlayNext()
        {
            if (_internalMusicQueue.TryPeek(out var item))
            {
                VoiceConnection.PlayAudio(item.AudioSource, cancellationToken: _playingCancellationTokenSource.Token)
                    .ContinueWith(_ =>
                    {
                        if (_internalMusicQueue.TryDequeue(out var _) && _internalMusicQueue.Any())
                        {
                            PlayNext();
                        } else
                        {
                            OnQueueEmpty?.Invoke(this, EventArgs.Empty);
                        }
                    });
                OnAudioPlay?.Invoke(this, item);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VoiceChannelAudioQueue));
            }
        }
    }

    public struct MusicQueueItem
    {
        public IAudioSource AudioSource { get; set; }
    }
}