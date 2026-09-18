namespace Sekiban.Dcb.WasmRuntime.Host;

/// <summary>
///     Stable JSON error bodies for runtime-host HTTP endpoints whose semantics match
///     <c>@sekiban/dcb-client@0.2.0</c> <c>FAILURE_KINDS</c>.
/// </summary>
internal static class RuntimeHostErrorResults
{
    internal const string TimeoutCode = "timeout";
    internal const string UnknownOutcomeCode = "unknown_outcome";
    internal const string ProjectionUnavailableCode = "projection_unavailable";

    internal static IResult ReadTimeout(string message) =>
        Results.Json(
            new { error = message, code = TimeoutCode },
            statusCode: StatusCodes.Status504GatewayTimeout);

    internal static IResult CommitUnknownOutcome(string message) =>
        Results.Json(
            new { error = message, code = UnknownOutcomeCode },
            statusCode: StatusCodes.Status504GatewayTimeout);

    internal static IResult ProjectionUnavailable(string message) =>
        Results.Json(
            new { error = message, code = ProjectionUnavailableCode },
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
