using System.Net;
using System.Net.Sockets;

namespace FaderDeck.Reaper;

/// <summary>
/// Local-only UDP OSC adapter. UDP bind does not prove REAPER is running;
/// only received valid volume feedback may mark values Verified.
/// </summary>
public sealed class ReaperOscClient : IAsyncDisposable
{
    private UdpClient? receiver;
    private UdpClient? sender;
    private CancellationTokenSource? stop;
    private Task? loop;
    public event Action<int, double>? VolumeFeedback;
    public event Action<string>? Stopped;

    public bool Listening => receiver is not null;
    public bool HasFeedback { get; private set; }

    public void Start(int reaperListenPort = 8000, int localFeedbackPort = 9000)
    {
        if (Listening) throw new InvalidOperationException("Adapter already started.");
        if (reaperListenPort is < 1 or > 65535 || localFeedbackPort is < 1 or > 65535 ||
            reaperListenPort == localFeedbackPort)
            throw new ArgumentOutOfRangeException(nameof(reaperListenPort), "OSC ports must be valid and different.");
        var receive = new UdpClient(new IPEndPoint(IPAddress.Loopback, localFeedbackPort));
        var send = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            send.Connect(IPAddress.Loopback, reaperListenPort);
            receiver = receive;
            sender = send;
            HasFeedback = false;
            stop = new CancellationTokenSource();
            loop = Task.Run(() => ListenAsync(receive, stop.Token));
        }
        catch
        {
            receive.Dispose();
            send.Dispose();
            throw;
        }
    }

    public async Task WriteTrackVolumeAsync(int track, double normalized, CancellationToken cancel = default)
    {
        UdpClient client = sender ?? throw new InvalidOperationException("REAPER OSC adapter not started.");
        byte[] packet = OscCodec.EncodeTrackVolume(track, normalized);
        await client.SendAsync(packet.AsMemory(), cancel);
        // UDP send success is NOT a readback acknowledgement.
    }

    private async Task ListenAsync(UdpClient client, CancellationToken cancel)
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                UdpReceiveResult result = await client.ReceiveAsync(cancel);
                if (!IPAddress.IsLoopback(result.RemoteEndPoint.Address)) continue;
                if (!OscCodec.TryDecodeTrackVolume(result.Buffer, out int track, out double normalized)) continue;
                HasFeedback = true;
                try { VolumeFeedback?.Invoke(track, normalized); }
                catch { /* external UI handler must not kill receive loop */ }
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancel.IsCancellationRequested) { }
        catch (SocketException e) when (cancel.IsCancellationRequested) { _ = e; }
        catch (Exception e)
        {
            Stopped?.Invoke(e.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop?.Cancel();
        receiver?.Dispose();
        sender?.Dispose();
        if (loop is not null)
            try { await loop; }
            catch (OperationCanceledException) { }
        loop = null;
        receiver = null;
        sender = null;
        stop?.Dispose();
        stop = null;
        HasFeedback = false;
    }
}
