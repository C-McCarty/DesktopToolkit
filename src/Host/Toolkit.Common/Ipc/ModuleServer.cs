using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Toolkit.Common.Ipc;

/// <summary>
/// The module side of the control channel. A module starts one of these to receive
/// commands from the host (enable/disable, settings, identify, shutdown). Each
/// connection carries a single line-delimited JSON command and a JSON response.
/// </summary>
public sealed class ModuleServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<IpcCommand, IpcResponse> _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public ModuleServer(string pipeName, Func<IpcCommand, IpcResponse> handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    public void Start() => _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true,
                };

                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                    continue;

                IpcResponse response;
                try
                {
                    var cmd = JsonSerializer.Deserialize<IpcCommand>(line, IpcJson.Options) ?? new IpcCommand();
                    response = _handler(cmd);
                }
                catch (Exception ex)
                {
                    response = IpcResponse.Fail(ex.Message);
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcJson.Options));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Transient pipe/connection error — drop this connection and keep listening.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(500); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
