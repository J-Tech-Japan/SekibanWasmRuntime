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
///     Host-side acceptance of the serialized commit envelope, mirroring the two-phase contract introduced by
///     Sekiban.Dcb 10.7.0 (SEK-G17) and verified unchanged through 10.8.0 in
///     <see cref="SerializedCommitAcceptor" />.
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
    // These are deliberately local to this gate. Do not add them to the Sekiban.Dcb error enum or response shape.
    private enum CollectionShapeError
    {
        MissingCollectionMember,
        InvalidCollectionMember,
        AliasCollectionMember,
        AmbiguousCollectionMember
    }

    private static readonly JsonDocumentOptions RawShapeOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

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
        if (GetCollectionShapeError(utf8Json) is { } shapeError)
        {
            return Malformed(shapeError);
        }

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
        if (GetCollectionShapeError(utf8Json) is { } shapeError)
        {
            return Malformed(shapeError);
        }

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

    /// <summary>
    ///     Converts the validated envelope to the executor DTO. The null coalescing is defensive only; the raw shape gate
    ///     rejects absent or null collection members before this method can be reached.
    /// </summary>
    private static SerializedCommitEnvelopeBindResult Accepted(VersionedSerializedCommitRequest envelope) =>
        new(
            new SerializedCommitRequest(
                envelope.EventCandidates ?? [],
                envelope.ConsistencyTags ?? []),
            null);

    /// <summary>
    ///     Validates only the top-level collection-member shape. Property values are inspected for their JSON kind but
    ///     never bound to runtime DTOs, decoded, or included in an error. Unknown extension members remain tolerated.
    /// </summary>
    private static CollectionShapeError? GetCollectionShapeError(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8Json.ToArray(), RawShapeOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return CollectionShapeError.InvalidCollectionMember;
            }

            var eventCandidatesCount = 0;
            var consistencyTagsCount = 0;
            var invalidEventCandidates = false;
            var invalidConsistencyTags = false;
            var hasAlias = false;
            var hasCaseVariant = false;

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("eventCandidates", StringComparison.Ordinal))
                {
                    eventCandidatesCount++;
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        invalidEventCandidates = true;
                    }

                    continue;
                }

                if (property.Name.Equals("eventCandidates", StringComparison.OrdinalIgnoreCase))
                {
                    hasCaseVariant = true;
                    continue;
                }

                if (property.Name.Equals("consistencyTags", StringComparison.Ordinal))
                {
                    consistencyTagsCount++;
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        invalidConsistencyTags = true;
                    }

                    continue;
                }

                if (property.Name.Equals("consistencyTags", StringComparison.OrdinalIgnoreCase))
                {
                    hasCaseVariant = true;
                    continue;
                }

                if (property.Name.Equals("candidates", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("consistency", StringComparison.OrdinalIgnoreCase))
                {
                    hasAlias = true;
                }
            }

            if (hasAlias)
            {
                return CollectionShapeError.AliasCollectionMember;
            }

            if (hasCaseVariant || eventCandidatesCount > 1 || consistencyTagsCount > 1)
            {
                return CollectionShapeError.AmbiguousCollectionMember;
            }

            if (invalidEventCandidates || invalidConsistencyTags)
            {
                return CollectionShapeError.InvalidCollectionMember;
            }

            return eventCandidatesCount == 1 && consistencyTagsCount == 1
                ? null
                : CollectionShapeError.MissingCollectionMember;
        }
        catch (JsonException)
        {
            // The version discriminator already rejects malformed JSON; keep this fallback fixed and request-data-free.
            return CollectionShapeError.InvalidCollectionMember;
        }
    }

    private static SerializedCommitEnvelopeBindResult Malformed(CollectionShapeError reason) =>
        Rejected(
            "malformed_commit_envelope",
            $"Serialized commit envelope is not well-formed ({reason}).");

    private static SerializedCommitEnvelopeBindResult Malformed(SerializedCommitShapeError shapeError) =>
        Rejected(
            "malformed_commit_envelope",
            $"Serialized commit envelope is not well-formed ({shapeError}).");

    private static SerializedCommitEnvelopeBindResult Rejected(string code, string message) =>
        new(null, new SerializedCommitEnvelopeError(code, message));
}
