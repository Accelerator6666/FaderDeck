using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace FaderDeck.Obs;

/// <summary>
/// Minimal OBS WebSocket v5 adapter. Credentials are memory-only; ws:// is
/// restricted to loopback to avoid sending credentials over remote plaintext.
/// </summary>
public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> requests = new();
    private ClientWebSocket? socket;
    private CancellationTokenSource? shutdown;
    private Task? reader;

    public bool Connected => socket?.State == WebSocketState.Open;
    public event Action<string, double>? VolumeChanged;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(Uri endpoint, string password, CancellationToken cancel = default)
    {
        if (!ObsProtocol.IsAllowedEndpoint(endpoint))
            throw new ArgumentException("Use local ws://127.0.0.1:4455 or a secure wss:// endpoint.", nameof(endpoint));

        await DisconnectAsync();
        var client = new ClientWebSocket();
        client.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        var tokenSource = new CancellationTokenSource();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(endpoint, timeout.Token);

            using JsonDocument hello = await ReadMessageAsync(client, timeout.Token);
            JsonElement envelope = hello.RootElement;
            if (envelope.GetProperty("op").GetInt32() != 0)
                throw new IOException("Expected OBS WebSocket Hello (op=0).");

            JsonElement helloData = envelope.GetProperty("d");
            string? authentication = null;
            if (helloData.TryGetProperty("authentication", out JsonElement auth))
            {
                if (string.IsNullOrEmpty(password))
                    throw new UnauthorizedAccessException("OBS authentication is enabled. Enter the OBS WebSocket password.");
                authentication = ObsProtocol.Authentication(password,
                    auth.GetProperty("salt").GetString() ?? "",
                    auth.GetProperty("challenge").GetString() ?? "");
            }

            // Inputs events: 1 << 3 = 8. Request-response uses op=7 independently.
            object identify = authentication is null
                ? new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 8 } }
                : new { op = 1, d = new { rpcVersion = 1, eventSubscriptions = 8, authentication } };
            await SendJsonAsync(client, identify, timeout.Token);
            using JsonDocument reply = await ReadMessageAsync(client, timeout.Token);
            if (reply.RootElement.GetProperty("op").GetInt32() != 2)
                throw new IOException("OBS authentication or protocol negotiation failed.");

            shutdown = tokenSource;
            socket = client;
            reader = Task.Run(() => ReceiveLoopAsync(client, tokenSource.Token));
        }
        catch
        {
            tokenSource.Dispose();
            client.Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetAudioInputsAsync(CancellationToken cancel = default)
    {
        JsonElement payload = await RequestAsync("GetInputList", null, cancel);
        var names = new List<string>();
        foreach (JsonElement input in payload.GetProperty("inputs").EnumerateArray())
        {
            string? name = input.GetProperty("inputName").GetString();
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        // Not all OBS inputs necessarily expose audio. Probe volume capability.
        var audio = new List<string>();
        foreach (string inputName in names)
        {
            try
            {
                _ = await GetInputVolumeDbAsync(inputName, cancel);
                audio.Add(inputName);
            }
            catch (ObsRequestException)
            {
                // Video/image-only input: no volume control.
            }
        }
        return audio;
    }

    public async Task<double> GetInputVolumeDbAsync(string inputName, CancellationToken cancel = default)
    {
        JsonElement response = await RequestAsync(
            "GetInputVolume", new { inputName }, cancel);
        return response.GetProperty("inputVolumeDb").GetDouble();
    }

    public async Task SetInputVolumeAsync(string inputName, double sliderPercent, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(inputName)) throw new ArgumentException("Input is required.", nameof(inputName));
        if (!double.IsFinite(sliderPercent) || sliderPercent < 0 || sliderPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(sliderPercent));
        // This range caps the preview to unity gain (0 dB). 0% is true silence.
        await RequestAsync("SetInputVolume",
            new { inputName, inputVolumeMul = ObsProtocol.SliderToMul(sliderPercent) }, cancel);
    }

    private async Task<JsonElement> RequestAsync(string name, object? requestData, CancellationToken cancel)
    {
        ClientWebSocket client = socket ?? throw new IOException("OBS is not connected.");
        if (client.State != WebSocketState.Open) throw new IOException("OBS is disconnected.");

        string id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!requests.TryAdd(id, completion)) throw new IOException("Duplicate request ID.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var message = new { op = 6, d = new { requestType = name, requestId = id, requestData } };
            await SendAsync(client, message, timeout.Token);
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            requests.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(ClientWebSocket client, object message, CancellationToken cancel)
    {
        await sendGate.WaitAsync(cancel);
        try { await SendJsonAsync(client, message, cancel); }
        finally { sendGate.Release(); }
    }

    private static Task SendJsonAsync(ClientWebSocket client, object message, CancellationToken cancel)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        return client.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, cancel).AsTask();
    }

    private async Task ReceiveLoopAsync(ClientWebSocket client, CancellationToken cancel)
    {
        string error = "OBS disconnected";
        try
        {
            while (!cancel.IsCancellationRequested && client.State == WebSocketState.Open)
            {
                using JsonDocument frame = await ReadMessageAsync(client, cancel);
                JsonElement root = frame.RootElement;
                int op = root.GetProperty("op").GetInt32();
                JsonElement data = root.GetProperty("d");
                if (op == 7)
                {
                    string id = data.GetProperty("requestId").GetString() ?? "";
                    if (!requests.TryRemove(id, out var completion)) continue;
                    JsonElement status = data.GetProperty("requestStatus");
                    if (status.GetProperty("result").GetBoolean())
                        completion.TrySetResult(data.TryGetProperty("responseData", out var body)
                            ? body.Clone() : JsonDocument.Parse("{}").RootElement.Clone());
                    else
                        completion.TrySetException(new ObsRequestException(
                            status.TryGetProperty("comment", out var comment)
                                ? comment.GetString() ?? "OBS rejected the request." : "OBS rejected the request."));
                }
                else if (op == 5 && data.GetProperty("eventType").GetString() == "InputVolumeChanged")
                {
                    JsonElement ev = data.GetProperty("eventData");
                    string name = ev.GetProperty("inputName").GetString() ?? "";
                    if (ev.TryGetProperty("inputVolumeDb", out var db) && db.ValueKind == JsonValueKind.Number)
                        VolumeChanged?.Invoke(name, db.GetDouble());
                }
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        finally
        {
            foreach (var pair in requests)
            {
                if (requests.TryRemove(pair.Key, out var pending))
                    pending.TrySetException(new IOException(error));
            }
            Disconnected?.Invoke(error);
        }
    }

    private static async Task<JsonDocument> ReadMessageAsync(ClientWebSocket client, CancellationToken cancel)
    {
        using var stream = new MemoryStream();
        byte[] buffer = new byte[8192];
        WebSocketReceiveResult received;
        do
        {
            received = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cancel);
            if (received.MessageType == WebSocketMessageType.Close)
                throw new IOException("OBS closed the WebSocket connection.");
            if (received.MessageType != WebSocketMessageType.Text)
                throw new IOException("Unexpected binary OBS message.");
            stream.Write(buffer, 0, received.Count);
            if (stream.Length > 1024 * 1024)
                throw new IOException("OBS message larger than 1 MB.");
        } while (!received.EndOfMessage);
        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancel);
    }

    public async Task DisconnectAsync()
    {
        shutdown?.Cancel();
        socket?.Abort();
        if (reader is { } previous)
        {
            try { await previous; } catch { /* best-effort shutdown */ }
        }
        reader = null;
        socket?.Dispose();
        socket = null;
        shutdown?.Dispose();
        shutdown = null;
        foreach (var pair in requests)
        {
            if (requests.TryRemove(pair.Key, out var pending))
                pending.TrySetException(new IOException("OBS connection closed."));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        sendGate.Dispose();
    }
}

public sealed class ObsRequestException(string message) : IOException(message);
