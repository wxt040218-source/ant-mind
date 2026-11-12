using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using AntMind.Contracts;
namespace AntMind.Transport;
public static class Frames {
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct) {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
        if (data.Length > Protocol.MaxBytes) throw new InvalidDataException("Frame too large.");
        byte[] length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        await stream.WriteAsync(length, ct); await stream.WriteAsync(data, ct); await stream.FlushAsync(ct);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct) {
        byte[] prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > Protocol.MaxBytes) throw new InvalidDataException("Invalid frame length.");
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data) ?? throw new InvalidDataException("Empty message.");
    }
}
public sealed class PipeServer(string name, Func<WireRequest, CancellationToken, Task<WireResponse>> dispatch) {
    // One request per connection; clients reconnect after each response. A finite timeout prevents stalled peers.
    // Linux implements named pipes as Unix Domain Sockets. Disposing the listening socket after every request
    // (the old per-iteration "using") unlinks/rebinds the socket path, which resets connections still queued
    // in the accept backlog with ECONNRESET when MCP and checks connect concurrently. Windows queues pipes in
    // the kernel and hides this race. Keep one server stream alive and reuse its listening socket via
    // Disconnect(); only rebuild the instance when a connection can no longer be reset.
    public async Task RunAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            while (!ct.IsCancellationRequested) {
                try {
                    await pipe.WaitForConnectionAsync(ct);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    var request = await Frames.ReadAsync<WireRequest>(pipe, deadline.Token);
                    var response = request.ProtocolVersion != Protocol.Version
                        ? new WireResponse(Protocol.Version, Error: "protocol_version")
                        : await dispatch(request, deadline.Token);
                    await Frames.WriteAsync(pipe, response, deadline.Token);
                    // Reuse the same listening socket for the next request (no unlink/rebind).
                    pipe.Disconnect();
                } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidOperationException) {
                    Console.Error.WriteLine("IPC request rejected: " + ex.Message);
                    break; // unrecoverable connection: rebuild the instance so one bad peer cannot stall the host
                }
            }
        }
    }
}
public sealed class PipeClient(string name) {
    public async Task<WireResponse> SendAsync(WireRequest request, CancellationToken ct = default) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token);
        await Frames.WriteAsync(pipe, request, deadline.Token);
        var response = await Frames.ReadAsync<WireResponse>(pipe, deadline.Token);
        if (response.ProtocolVersion != Protocol.Version) throw new InvalidDataException("Protocol version mismatch.");
        return response;
    }
}
