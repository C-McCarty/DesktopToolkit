using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Toolkit.Common.Ipc;

/// <summary>The host side of the control channel: sends one command and awaits the reply.</summary>
public static class ModuleClient
{
    public static async Task<IpcResponse> SendAsync(string pipeName, IpcCommand command, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(timeoutMs, cts.Token);

            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(command, IpcJson.Options));

            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null)
                return IpcResponse.Fail("No response from module.");

            return JsonSerializer.Deserialize<IpcResponse>(line, IpcJson.Options)
                   ?? IpcResponse.Fail("Unparseable response from module.");
        }
        catch (Exception ex)
        {
            return IpcResponse.Fail(ex.Message);
        }
    }

    public static Task<IpcResponse> PingAsync(string pipeName, int timeoutMs = 1000) =>
        SendAsync(pipeName, new IpcCommand { Type = IpcCommandType.Ping }, timeoutMs);
}
