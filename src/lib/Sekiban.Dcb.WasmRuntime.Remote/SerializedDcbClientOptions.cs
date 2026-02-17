namespace Sekiban.Dcb.WasmRuntime.Remote;

/// <summary>
///     Configuration for the HTTP serialized DCB client.
/// </summary>
public class SerializedDcbClientOptions
{
    /// <summary>
    ///     Base URL of the API service hosting serialized endpoints
    ///     (e.g. "https://localhost:5001").
    /// </summary>
    public required string BaseUrl { get; set; }
}
