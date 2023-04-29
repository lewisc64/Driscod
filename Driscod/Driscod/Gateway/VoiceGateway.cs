using Driscod.Audio;
using Driscod.Audio.Encoding;
using Driscod.Extensions;
using Driscod.Network;
using Driscod.Network.Udp;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Driscod.Gateway;

public class VoiceGateway : Gateway
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static Random _random = new Random();

    private readonly string _serverId;
    private readonly string _userId;
    private readonly string _token;
    private readonly Shard _parentShard;

    private string? _udpSocketIpAddress;
    private ushort? _udpSocketPort;
    private IEnumerable<string>? _endpointEncryptionModes;
    private string? _encryptionMode;
    private uint _ssrc;
    private byte[]? _secretKey;
    private int _localPort;
    private string? _externalAddress;

    public event EventHandler? OnStop;

    public VoiceGateway(Shard parentShard, string url, string serverId, string userId, string sessionId, string token)
        : base(url)
    {
        _serverId = serverId;
        _userId = userId;
        _token = token;

        _parentShard = parentShard;
        SessionId = sessionId;

        Socket.Opened += async (a, b) =>
        {
            if (KeepSocketOpen)
            {
                await Send((int)MessageType.Resume, Identity);
            }
            else
            {
                await Send((int)MessageType.Identify, Identity);
                Ready = false;
            }
        };

        Socket.Closed += (a, b) =>
        {
            OnStop?.Invoke(this, EventArgs.Empty);
        };

        AddListener<JObject>((int)MessageType.Hello, data =>
        {
            HeartbeatIntervalMilliseconds = (int)(data?["heartbeat_interval"]?.ToObject<double>() ?? throw new InvalidOperationException("Did not receive a heartbeat interval in the hello event."));
            StartHeart();
            KeepSocketOpen = true;
        });

        AddListener<JObject>((int)MessageType.Ready, async data =>
        {
            _udpSocketIpAddress = data?["ip"]?.ToObject<string>() ?? throw new InvalidOperationException("Did not receive the IP of the UDP socket in the ready event.");
            _udpSocketPort = data?["port"]?.ToObject<ushort>() ?? throw new InvalidOperationException("Did not receive the port of the UDP socket in the ready event.");
            _endpointEncryptionModes = data?["modes"]?.ToObject<string[]>() ?? throw new InvalidOperationException("Did not receive the encrpytion modes in the ready event.");
            _ssrc = data?["ssrc"]?.ToObject<uint>() ?? throw new InvalidOperationException("Did not receive the SSRC in the ready event.");

            if (!AllowedEncryptionModes!.Any())
            {
                Logger.Fatal($"[{Name}] Found no allowed encryption modes.");
                await Stop();
                return;
            }

            _encryptionMode = AllowedEncryptionModes!.First();

            Logger.Debug($"[{Name}] Using encryption mode '{_encryptionMode}'.");

            await FetchExternalAddress();

            await Send((int)MessageType.SelectProtocol, new JObject
            {
                { "protocol", "udp" },
                { "data",
                    new JObject
                    {
                        { "address", _externalAddress },
                        { "port", _localPort },
                        { "mode", _encryptionMode },
                    }
                },
            });
        });

        AddListener<JObject>((int)MessageType.SessionDescription, data =>
        {
            var codec = data?["audio_codec"]?.ToObject<string>() ?? throw new InvalidOperationException("Did not receive the audio codec in the session description message.");
            if (codec != "opus")
            {
                Logger.Warn($"[{Name}] Voice gateway requested unsupported audio codec: '{codec}'.");
            }
            _secretKey = data?["secret_key"]?.ToObject<byte[]>() ?? throw new InvalidOperationException("Did not receive the secret key in the session description message.");
            Ready = true;
        });
    }

    private JObject Identity => new JObject
    {
        { "server_id", _serverId },
        { "user_id", _userId },
        { "session_id", SessionId },
        { "token", _token },
    };

    private IEnumerable<string>? AllowedEncryptionModes => _endpointEncryptionModes?.Intersect(RtpPacketGenerator.SupportedEncryptionModes);

    protected override IEnumerable<int> RespectedCloseSocketCodes => new[] { 4006, 4014 }; // Should not reconnect upon forced disconnection.

    public override string Name => $"VOICE-{SessionId}";

    public bool Ready { get; private set; } = false;

    public bool Speaking { get; private set; } = false;

    public string SessionId { get; set; }

    public override async Task Start()
    {
        await base.Start();
        Task.Run(async () =>
        {
            await Task.Delay(5000, CancellationTokenSource.Token);
            if (Running && !Ready)
            {
                await Restart();
            }
        }).Forget();
    }

    public override async Task Stop()
    {
        try
        {
            await _parentShard.Send((int)Shard.MessageType.VoiceStateUpdate, new JObject
                {
                    { "guild_id", _serverId },
                    { "channel_id", null },
                    { "self_mute", false },
                    { "self_deaf", false },
                });
        }
        finally
        {
            // Discord should close the socket after the voice state update.
            await Task.WhenAny(
                Task.Run(() =>
                {
                    while (Socket.State != WebSocket4Net.WebSocketState.Closed)
                    {
                        Thread.Sleep(200);
                    }
                }),
                Task.Delay(TimeSpan.FromSeconds(10)));

            if (Socket.State != WebSocket4Net.WebSocketState.Closed)
            {
                Logger.Warn($"[{Name}] Expected Discord to close the socket, but it did not.");
            }

            await base.Stop();
        }
    }

    public async Task WaitForReady(TimeSpan? timeout = null)
    {
        if (timeout is null)
        {
            timeout = TimeSpan.FromSeconds(10);
        }
        var stopwatch = Stopwatch.StartNew();
        while (!Ready && stopwatch.Elapsed < timeout)
        {
            await Task.Delay(50);
        }
        if (!Ready)
        {
            throw new TimeoutException();
        }
    }

    public AudioStreamer CreateAudioStreamer()
    {
        if (!Ready)
        {
            throw new InvalidOperationException("Voice socket is not ready to create audio streamer.");
        }

        var endPointInfo = new VoiceEndPointInfo
        {
            SocketEndPoint = GetUdpEndpoint(),
            LocalPort = _localPort,
            Ssrc = _ssrc,
            EncryptionKey = _secretKey!,
            EncryptionMode = _encryptionMode!,
        };

        var streamer = new AudioStreamer(new OpusAudioEncoder(Connectivity.VoiceSampleRate, Connectivity.VoiceChannels), endPointInfo, cancellationToken: CancellationTokenSource.Token);

        streamer.OnAudioStart += async (a, b) =>
        {
            await BeginSpeaking();
        };

        streamer.OnAudioStop += async (a, b) =>
        {
            await EndSpeaking();
        };

        return streamer;
    }

    protected override async Task Heartbeat()
    {
        var nonce = _random.Next(int.MinValue, int.MaxValue);

        var response = await ListenForEvent<int>(
            (int)MessageType.HeartbeatAck,
            listenerCreateCallback: async () =>
            {
                await Send((int)MessageType.Heartbeat, nonce);
            },
            timeout: TimeSpan.FromSeconds(10));

        if (response != nonce)
        {
            throw new InvalidOperationException("Heartbeat failed, recieved incorrect nonce.");
        }
    }

    private async Task BeginSpeaking()
    {
        await Send((int)MessageType.Speaking, new JObject
        {
            { "ssrc", _ssrc },
            { "delay", 0 },
            { "speaking", 1 },
        });
        Speaking = true;
    }

    private async Task EndSpeaking()
    {
        await Send((int)MessageType.Speaking, new JObject
        {
            { "ssrc", _ssrc },
            { "delay", 0 },
            { "speaking", 0 },
        });
        Speaking = false;
    }

    private IPEndPoint GetUdpEndpoint()
    {
        return new IPEndPoint(IPAddress.Parse(_udpSocketIpAddress!), _udpSocketPort!.Value);
    }

    private async Task FetchExternalAddress()
    {
        var datagram = new byte[] { 0, 1, 0, 70 }
            .Concat(_ssrc.ToBytesBigEndian())
            .Concat(Enumerable.Repeat((byte)0, 66))
            .ToArray();

        byte[] response;

        using (var udpSocket = new UdpSocket(_udpSocketIpAddress!, _udpSocketPort!.Value) { ListenForPackets = true })
        {
            await udpSocket.Send(datagram);
            response = await udpSocket.WaitForNextPacket();
        }

        _localPort = (response[response.Length - 2] << 8) + response[response.Length - 1];
        _externalAddress = Encoding.UTF8.GetString(response.Skip(8).TakeWhile(x => x != 0).ToArray());
    }

    public enum MessageType
    {
        Any = -1,
        Identify = 0,
        SelectProtocol = 1,
        Ready = 2,
        Heartbeat = 3,
        SessionDescription = 4,
        Speaking = 5,
        HeartbeatAck = 6,
        Resume = 7,
        Hello = 8,
        Resumed = 9,
        ClientDisconnect = 13,
    }
}
