using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolkit.Common.Ipc;

/// <summary>Commands the host sends to a module over its named pipe.</summary>
public enum IpcCommandType
{
    Ping,
    SetEnabled,
    ApplySettings,
    Identify,
    Activate,
    Shutdown,
}

/// <summary>A single host → module request.</summary>
public sealed class IpcCommand
{
    public IpcCommandType Type { get; set; }

    /// <summary>Payload for <see cref="IpcCommandType.SetEnabled"/>.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Payload for <see cref="IpcCommandType.ApplySettings"/>.</summary>
    public Dictionary<string, JsonElement>? Settings { get; set; }
}

/// <summary>A module → host reply.</summary>
public sealed class IpcResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Status { get; set; }

    public static IpcResponse Success(string? status = null) => new() { Ok = true, Status = status };

    public static IpcResponse Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>Shared JSON settings for the line-delimited IPC protocol.</summary>
public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
