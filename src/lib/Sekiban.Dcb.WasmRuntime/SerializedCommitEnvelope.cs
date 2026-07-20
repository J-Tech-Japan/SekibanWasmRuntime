using System.Text.Json;
using Sekiban.Dcb.Commands;
namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     A typed rejection of a serialized commit envelope, produced before any typed payload binding, base64 decode, tag
///     reservation, EventId allocation, or executor call. <see cref="Code" /> is a stable machine-readable discriminator
///     that callers may branch on; <see cref="Message" /> is human-facing and never carries request content.
/// </summary>
public sealed record SerializedCommitEnvelopeError(string Code, string Message);

/// <summary>The outcome of binding a raw envelope: exactly one of <see cref="Request" /> or <see cref="Error" /> is set.</summary>
public sealed record SerializedCommitEnvelopeBindResult(
    SerializedCommitRequest? Request,
    SerializedCommitEnvelopeError? Error);

/// <summary>
///     Host-side acceptance of the serialized commit envelope, mirroring the two-phase contract that Sekiban.Dcb 10.7.0
///     (SEK-G17) defines in <see cref="SerializedCommitAcceptor" />.
///     <para>
///         Phase 1 reads only the raw <c>version</c> discriminator via
///         <see cref="SerializedCommitVersionDiscriminator" />. Phase 2 binds only the resolved shape: a missing
///         <c>version</c> is the legacy unversioned official shape and is lifted losslessly to V1 through
///         <see cref="LegacyUnversionedSerializedCommitAdapter" />; a known <c>version</c> binds
///         <see cref="VersionedSerializedCommitRequest" />. Per-event tags are preserved verbatim in both paths.
///     </para>
///     <para>
///         This type deliberately binds rather than executes, so the calling endpoint keeps access to the bound
///         candidates (the runtime host needs them to mark written tags) while still failing closed on off-contract
///         envelopes. Raw <see cref="JsonException" /> detail is discarded and never surfaced, so hostile request content
///         cannot leak through the error surface.
///     </para>
/// </summary>
public static class SerializedCommitEnvelope
{
    /// <summary>Reads the whole request body and binds it. The stream is read to completion before binding.</summary>
    public static async Task<SerializedCommitEnvelopeBindResult> BindAsync(
        Stream utf8JsonStream,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await utf8JsonStream.CopyToAsync(buffer, cancellationToken);
        return Bind(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static SerializedCommitEnvelopeBindResult Bind(ReadOnlySpan<byte> utf8Json)
    {
        SerializedCommitVersionResult discrimination = SerializedCommitVersionDiscriminator.Read(utf8Json);
        return discrimination.Kind switch
        {
            SerializedCommitVersionKind.LegacyUnversioned => BindLegacy(utf8Json),
            SerializedCommitVersionKind.KnownVersion => BindVersioned(utf8Json),
            SerializedCommitVersionKind.UnsupportedVersion => Rejected(
                "unsupported_commit_envelope_version",
                $"Serialized commit envelope version {discrimination.Version!.Value} is not supported by this runtime (supported version: {VersionedSerializedCommitRequest.CurrentVersion})."),
            _ => Malformed(discrimination.ShapeError ?? SerializedCommitShapeError.UnreadableJson)
        };
    }

    private static SerializedCommitEnvelopeBindResult BindLegacy(ReadOnlySpan<byte> utf8Json)
    {
        SerializedCommitRequest? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<SerializedCommitRequest>(utf8Json, SerializedCommitWireContract.Options);
        }
        catch (JsonException)
        {
            return Malformed(SerializedCommitShapeError.LegacyPayloadInvalid);
        }

        return legacy is null
            ? Malformed(SerializedCommitShapeError.LegacyPayloadInvalid)
            : Accepted(LegacyUnversionedSerializedCommitAdapter.ToVersionedV1(legacy));
    }

    private static SerializedCommitEnvelopeBindResult BindVersioned(ReadOnlySpan<byte> utf8Json)
    {
        VersionedSerializedCommitRequest? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VersionedSerializedCommitRequest>(
                utf8Json,
                SerializedCommitWireContract.Options);
        }
        catch (JsonException)
        {
            return Malformed(SerializedCommitShapeError.VersionedPayloadInvalid);
        }

        return envelope is null
            ? Malformed(SerializedCommitShapeError.VersionedPayloadInvalid)
            : Accepted(envelope);
    }

    /// <summary>Absent arrays coalesce to empty (a valid empty commit), so a missing collection is never a null reference.</summary>
    private static SerializedCommitEnvelopeBindResult Accepted(VersionedSerializedCommitRequest envelope) =>
        new(
            new SerializedCommitRequest(
                envelope.EventCandidates ?? [],
                envelope.ConsistencyTags ?? []),
            null);

    private static SerializedCommitEnvelopeBindResult Malformed(SerializedCommitShapeError shapeError) => Rejected(
        "malformed_commit_envelope",
        $"Serialized commit envelope is not well-formed ({shapeError}).");

    private static SerializedCommitEnvelopeBindResult Rejected(string code, string message) =>
        new(null, new SerializedCommitEnvelopeError(code, message));
}
