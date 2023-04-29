using Driscod.Audio.AudioSource;
using Driscod.Extensions;
using Driscod.Tracking.Objects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Tracking.Voice;

public class VoiceChannelAudioQueue : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private string _channelId;
    private bool _disposed = false;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationTokenSource _playingCancellationTokenSource = new();
    private readonly ConcurrentQueue<MusicQueueItem> _internalMusicQueue = new ConcurrentQueue<MusicQueueItem>();
    private readonly Task _handleQueueTask;

    public event EventHandler<MusicQueueItem>? OnAudioAdded;
    public event EventHandler<MusicQueueItem>? OnAudioPlay;
    public event EventHandler? OnQueueEmpty;

    public VoiceChannelAudioQueue(Channel channel)
    {
        Bot = channel.Bot;
        _channelId = channel.Id;

        _handleQueueTask = HandleQueue();
    }

    private IBot Bot { get; set; }

    private VoiceConnection VoiceConnection
    {
        get
        {
            if (Guild.VoiceConnection == null || !Guild.VoiceConnection.Connected || Guild.VoiceConnection.Stale)
            {
                Channel.ConnectVoice().Wait();
            }

            return Guild.VoiceConnection!;
        }
    }

    private Guild Guild => Channel.Guild ?? throw new InvalidOperationException("The channel is not part of a guild.");

    public Channel Channel
    {
        get
        {
            ThrowIfDisposed();
            return Bot.GetObject<Channel>(_channelId)!;
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
    }

    public void Skip()
    {
        ThrowIfDisposed();

        CancelCurrentPlay();
    }

    public void ClearQueue()
    {
        ThrowIfDisposed();

        _internalMusicQueue.Clear();
        CancelCurrentPlay();
    }

    private void CancelCurrentPlay()
    {
        var oldSource = _playingCancellationTokenSource;
        _playingCancellationTokenSource = new CancellationTokenSource();
        oldSource.Cancel();
    }

    private async Task HandleQueue()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                if (_internalMusicQueue.TryPeek(out var item))
                {
                    try
                    {
                        var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, _playingCancellationTokenSource.Token);
                        OnAudioPlay?.Invoke(this, item);
                        await VoiceConnection.PlayAudio(item.AudioSource, cancellationToken: combinedTokenSource.Token);
                    }
                    finally
                    {
                        _internalMusicQueue.TryDequeue(out var _);
                        if (!_internalMusicQueue.Any())
                        {
                            Task.Run(() =>
                            {
                                OnQueueEmpty?.Invoke(this, EventArgs.Empty);
                            }).Forget();
                        }
                    }
                }
                await Task.Delay(100, _cancellationTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                await Task.Delay(1000);
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
            // do not send any events while disposing
            OnAudioAdded = null;
            OnAudioPlay = null;
            OnQueueEmpty = null;

            _cancellationTokenSource.Cancel();
            _playingCancellationTokenSource.Cancel();
            _handleQueueTask.Wait();

            _cancellationTokenSource.Dispose();
            _playingCancellationTokenSource.Dispose();

            _internalMusicQueue.Clear();
            Guild.VoiceConnection?.Dispose();
            _disposed = true;
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
