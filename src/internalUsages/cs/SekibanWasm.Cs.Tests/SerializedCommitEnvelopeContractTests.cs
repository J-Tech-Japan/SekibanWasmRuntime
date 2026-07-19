using System.Text;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

/// <summary>
///     Black-box contract tests for host-side acceptance of the serialized commit envelope
///     (<see cref="SerializedCommitEnvelope" />), pinned against Sekiban.Dcb 10.7.0 / SEK-G17.
///     <para>
///         These pin two things the runtime must never lose: a pre-10.7 client sending the legacy unversioned shape is
///         still accepted losslessly, and an envelope that is off-contract fails closed with a typed error rather than
///         being bound optimistically.
///     </para>
/// </summary>
public class SerializedCommitEnvelopeContractTests
{
    private const string CandidateA =
        """{"payload":"eyJmb3JlY2FzdElkIjoiZi0xIn0=","eventPayloadName":"WeatherForecastCreated","tags":["weather:f-1"]}""";
    private const string CandidateB =
        """{"payload":"eyJmb3JlY2FzdElkIjoiZi0yIn0=","eventPayloadName":"WeatherForecastCreated","tags":["weather:f-2","region:jp"]}""";

    private static SerializedCommitEnvelopeBindResult Bind(string json) =>
        SerializedCommitEnvelope.Bind(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Bind_Version1Envelope_ShouldAcceptAndPreservePerCandidateTags()
    {
        SerializedCommitEnvelopeBindResult result = Bind(
            $$"""{"version":1,"eventCandidates":[{{CandidateA}}],"consistencyTags":[{"tag":"weather:f-1","lastSortableUniqueId":"0639"}]}""");

        Assert.Null(result.Error);
        SerializedCommitRequest request = Assert.IsType<SerializedCommitRequest>(result.Request);
        SerializableEventCandidate candidate = Assert.Single(request.EventCandidates);
        Assert.Equal("WeatherForecastCreated", candidate.EventPayloadName);
        Assert.Equal(["weather:f-1"], candidate.Tags);
        ConsistencyTagEntry consistencyTag = Assert.Single(request.ConsistencyTags);
        Assert.Equal("weather:f-1", consistencyTag.Tag);
        Assert.Equal("0639", consistencyTag.LastSortableUniqueId);
    }

    /// <summary>A 10.2.2-era client predates the version discriminator; its envelope must still be accepted.</summary>
    [Fact]
    public void Bind_LegacyUnversionedEnvelope_ShouldBeLiftedLosslessly()
    {
        SerializedCommitEnvelopeBindResult result = Bind(
            $$"""{"eventCandidates":[{{CandidateA}}],"consistencyTags":[]}""");

        Assert.Null(result.Error);
        SerializableEventCandidate candidate = Assert.Single(result.Request!.EventCandidates);
        Assert.Equal(["weather:f-1"], candidate.Tags);
    }

    /// <summary>
    ///     Heterogeneous per-candidate tags must survive binding verbatim. Collapsing them into one per-commit list is
    ///     explicitly NOT this contract (see LegacyUnversionedSerializedCommitAdapter's own remarks); a regression here
    ///     would silently retag events.
    /// </summary>
    [Fact]
    public void Bind_DifferingPerCandidateTags_ShouldNotBeCollapsed()
    {
        SerializedCommitEnvelopeBindResult result = Bind(
            $$"""{"version":1,"eventCandidates":[{{CandidateA}},{{CandidateB}}],"consistencyTags":[]}""");

        Assert.Null(result.Error);
        Assert.Collection(
            result.Request!.EventCandidates,
            first => Assert.Equal(["weather:f-1"], first.Tags),
            second => Assert.Equal(["weather:f-2", "region:jp"], second.Tags));
    }

    [Fact]
    public void Bind_UnsupportedVersion_ShouldFailClosedWithTypedError()
    {
        SerializedCommitEnvelopeBindResult result = Bind(
            $$"""{"version":999,"eventCandidates":[{{CandidateA}}],"consistencyTags":[]}""");

        Assert.Null(result.Request);
        Assert.Equal("unsupported_commit_envelope_version", result.Error!.Code);
        Assert.Contains("999", result.Error.Message);
    }

    /// <summary>
    ///     The discriminator is case-SENSITIVE by contract: a case-variant must not be read optimistically as either V1
    ///     or legacy.
    /// </summary>
    [Theory]
    [InlineData("""{"Version":1,"eventCandidates":[],"consistencyTags":[]}""")]
    [InlineData("""{"version":1,"version":1,"eventCandidates":[],"consistencyTags":[]}""")]
    [InlineData("""{"version":"1","eventCandidates":[],"consistencyTags":[]}""")]
    [InlineData("""["not","an","object"]""")]
    [InlineData("""{"version":1,"eventCandidates":"not-an-array"}""")]
    public void Bind_OffContractEnvelope_ShouldFailClosedAsMalformed(string json)
    {
        SerializedCommitEnvelopeBindResult result = Bind(json);

        Assert.Null(result.Request);
        Assert.Equal("malformed_commit_envelope", result.Error!.Code);
    }

    /// <summary>An absent collection is a valid empty commit, never a null-reference failure.</summary>
    [Fact]
    public void Bind_AbsentCollections_ShouldCoalesceToEmpty()
    {
        SerializedCommitEnvelopeBindResult result = Bind("""{"version":1}""");

        Assert.Null(result.Error);
        Assert.Empty(result.Request!.EventCandidates);
        Assert.Empty(result.Request.ConsistencyTags);
    }

    /// <summary>
    ///     Round-trips what the client actually emits through what the host actually accepts, so the two halves of the
    ///     contract cannot drift apart independently.
    /// </summary>
    [Fact]
    public void Bind_ClientEmittedEnvelope_ShouldRoundTrip()
    {
        var emitted = new VersionedSerializedCommitRequest(
            VersionedSerializedCommitRequest.CurrentVersion,
            [new SerializableEventCandidate("payload"u8.ToArray(), "WeatherForecastCreated", ["weather:f-1"])],
            [new ConsistencyTagEntry("weather:f-1", "0639")]);

        SerializedCommitEnvelopeBindResult result = SerializedCommitEnvelope.Bind(
            SerializedCommitWireContract.SerializeToUtf8Bytes(emitted));

        Assert.Null(result.Error);
        SerializableEventCandidate candidate = Assert.Single(result.Request!.EventCandidates);
        Assert.Equal("payload"u8.ToArray(), candidate.Payload);
        Assert.Equal(["weather:f-1"], candidate.Tags);
    }
}
