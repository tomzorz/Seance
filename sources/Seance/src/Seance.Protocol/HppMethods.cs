namespace Seance.Protocol;

/// <summary>
/// The method names of HPP generation 1 (HPP section 6.2).
/// </summary>
public static class HppMethods
{
    public const string Query = "presence.query";
    public const string Context = "presence.context";
    public const string Reach = "presence.reach";
    public const string Subscribe = "presence.subscribe";
    public const string Pin = "presence.pin";
    public const string Unpin = "presence.unpin";
    public const string Authenticate = "presence.authenticate";

    /// <summary>
    /// The notification the daemon sends to subscribed connections (REQ-EVT-1).
    /// </summary>
    public const string Event = "presence.event";
}

/// <summary>
/// JSON-RPC 2.0 error codes plus the two this protocol defines (HPP section 6.4).
/// </summary>
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    /// <summary>
    /// The connection is a TCP connection and is not authenticated.
    /// </summary>
    public const int AuthRequired = -32000;

    /// <summary>
    /// The presented token matches no configured token.
    /// </summary>
    public const int AuthFailed = -32001;
}

/// <summary>
/// A JSON-RPC error result returned by the daemon.
/// </summary>
public sealed class JsonRpcException(int code, string message, string? detail = null)
    : Exception($"{message} (JSON-RPC {code})")
{
    public int Code { get; } = code;

    public string ErrorMessage { get; } = message;

    public string? Detail { get; } = detail;
}
