# Serialized DCB V1 normative specification

Status: normative for the five serialized DCB V1 HTTP endpoints. This page is
the compatibility contract; it is not a description of a particular runtime
implementation.

Scope: `POST /api/sekiban/serialized/commit`, `query`, `list-query`,
`tag-state`, and `tag-latest-sortable`. Projection and materialized-view ABI,
command execution, V2/envelope changes, `RemoteSekibanExecutor`, SDKs, and
provider-specific deployment APIs are outside this specification.

## 1. Derivation and independence record

The rules in this page were drafted on 2026-08-14 from the published SWR-G082
issue contract, the public compatibility-boundary page, and the existing
wire examples. The reservation, retry, restart, allocation, and storage
rules were written as intended semantics before the conformance suite was run
against the .NET host. No .NET type, executor, assembly, or source path is a
normative input to a rule below.

The comparison phase is deliberately separate. The HTTP suite records the
requests and responses it actually observes, while the findings ledger records
where that observation differs from this page or cannot be proved through the
boundary. A green HTTP run is therefore not a claim that the implementation
was used to derive the specification.

## 2. Terms and wire conventions

* A **tag** is a non-empty string identifying one logical stream. The reference
  profile uses `group:content`; an implementation may use another tag grammar
  only when its fixture documents that grammar. A tag-state ID is
  `group:content:projector`.
* A **SortableUniqueId** (SUID) is an opaque non-empty string after the first
  write. Implementations MUST make SUIDs unique within a service and MUST
  compare them using the contract's ordinal/bytewise ordering. Clients MUST
  treat them as opaque strings and MUST NOT parse timestamps or GUIDs.
* Requests and responses are UTF-8 JSON with `Content-Type: application/json`.
  Property names in the V1 wire format are the lower camel-case names shown
  below. A successful endpoint response is HTTP 200.
* A base64 field is standard RFC 4648 base64 of the raw bytes. Event payload
  bytes are not decoded, validated, or rewritten by the transport layer.
* `null` is distinct from an omitted JSON member. In a serialized V1
  consistency entry, `lastSortableUniqueId: null` is invalid and MUST be
  rejected before reservation, ID allocation, or storage.

## 3. Consistency and reservation semantics

### 3.1 Three caller states

For a tag `t` in a commit request:

| Wire state | Meaning | Required action |
| --- | --- | --- |
| No entry for `t` | Unobserved/non-consistency tag | Do not compare a cached or persisted head and do not reserve `t`; still write `t` as an event tag. |
| `{"tag": t, "lastSortableUniqueId": ""}` | Assert empty | Compare the authoritative current head with empty. It succeeds only when the tag has no committed SUID. |
| `{"tag": t, "lastSortableUniqueId": "s"}` | Exact match | Compare the authoritative current head with `s` using ordinal equality. Any other value is a conflict. |
| `{"tag": t, "lastSortableUniqueId": null}` | Invalid V1 shape | Reject with no side effect. `null` is not the wire representation of unobserved. |

Every consistency entry MUST refer to a tag present in at least one event
candidate, and a tag MUST occur at most once in `consistencyTags`. A client
that wants an unobserved write omits the entry; it does not send `null`.

### 3.2 Reservation lifecycle

For a request with consistency entries, the server MUST:

1. validate the whole envelope and all tag relationships;
2. lazily catch up and refresh each involved tag from authoritative durable
   storage before deciding its current head. A stale empty cache is never
   sufficient to authorize a write;
3. acquire a reservation for every consistency tag while applying the
   comparison in 3.1;
4. if any reservation fails, cancel all reservations acquired for this request,
   write no candidate event, and return a conflict/refusal outcome;
5. allocate SUIDs and write candidate events only after every reservation has
   succeeded;
6. confirm successful reservations after the durable write; and
7. release/cancel reservations when the write fails before confirmation.

At most one active reservation is visible per consistency tag. A reservation
has a finite cancellation window. The reference profile is 30 seconds; a
deployment MAY choose another value, but it MUST publish that value and MUST
clean expired reservations before evaluating a new reservation. An expired
reservation cannot block a later valid request and cannot be confirmed by a
caller that did not complete the original operation.

The conflict retry rule is observable and mandatory: after a conflict, the
client rereads `tag-latest-sortable`, uses the returned head (or empty for a
still-empty tag), and submits a new request. Reusing the stale expectation is
not a retry and MUST continue to conflict.

### 3.3 Restart recovery

An implementation MUST recover durable tag heads and the service event-store
head before answering a write or allocating a SUID after process restart.
In-memory empty caches, reservations, and allocator state MUST NOT authorize a
write against a durable non-empty tag. A restart MAY discard in-flight
reservations; it MUST NOT discard durable events or make a later SUID lower
than the persisted service head.

### 3.4 Read-side catch-up is not commit protection

The read side MAY use a bounded catch-up window while it observes events that
can arrive out of order. The reference profile uses a 20,000 ms SafeWindow and
tracks lag dynamically; a deployment MUST publish a different value if it
chooses one. This window applies to projection/read catch-up and query waiting.
It does not protect the commit path, does not replace authoritative refresh
before a consistency comparison, and does not make a partial write atomic.

## 4. SortableUniqueId allocation

The service maintains one persisted event-store head. Before the first
reservation or allocation in a process, the allocator MUST seed itself from
that head. Every newly allocated SUID MUST be strictly greater than the
persisted head and every SUID allocated by concurrent successful commits MUST
be unique and strictly orderable. A successful response returns the generated
SUID in each `writtenEvents[*].sortableUniqueIdValue`.

The contract does not assign meaning to the internal encoding. A conformance
client proves the observable rule by issuing concurrent commits, collecting
the returned SUIDs, checking uniqueness and strict ordinal ordering, and then
restarting the service and checking that the next SUID is greater than the
pre-restart head.

## 5. Endpoint contracts

### 5.1 `POST /api/sekiban/serialized/commit`

Request shape:

```json
{
  "version": 1,
  "eventCandidates": [
    {
      "payload": "<base64 bytes>",
      "eventPayloadName": "EventTypeName",
      "tags": ["group:content"]
    }
  ],
  "consistencyTags": [
    {"tag": "group:content", "lastSortableUniqueId": ""}
  ]
}
```

`version` MUST be the number `1`. `eventCandidates` and
`consistencyTags` are arrays; an omitted array is equivalent to an empty array
for the empty-commit compatibility case. An empty commit has no storage side
effect and returns empty `writtenEvents` and `tagWriteResults`.

The server MUST preserve candidate order, payload bytes, event payload name,
and candidate tag lists. It generates the event ID, SUID, and event metadata;
those values are not accepted from the caller. A successful response has this
shape (the duration encoding is diagnostic and implementation-language
neutral):

```json
{
  "writtenEvents": [
    {
      "payload": "<base64 bytes>",
      "sortableUniqueIdValue": "<suid>",
      "id": "<uuid>",
      "eventMetadata": {
        "causationId": "<string>",
        "correlationId": "<string>",
        "executedUser": "<string>"
      },
      "tags": ["group:content"],
      "eventPayloadName": "EventTypeName"
    }
  ],
  "tagWriteResults": [
    {"tag": "group:content", "version": 1, "writtenAt": "<timestamp>"}
  ],
  "duration": "<duration string>"
}
```

The endpoint MUST discriminate the version and validate the complete request
before any tag reservation, ID allocation, or write. Unsupported versions,
malformed JSON, wrong member types, duplicate consistency tags, unknown
consistency tags, and null SUID expectations are rejected without a write.

### 5.2 `POST /api/sekiban/serialized/tag-latest-sortable`

Request: `{"tag":"group:content"}`.

Response:

```json
{"exists": true, "lastSortableUniqueId": "<suid>"}
```

For a tag with no committed event, the response is
`{"exists":false,"lastSortableUniqueId":""}`. The read MUST reflect the
durable tag head after any required catch-up and MUST NOT allocate an ID.

### 5.3 `POST /api/sekiban/serialized/tag-state`

Request: `{"tagStateId":"group:content:projector"}`.

Response fields:

* `payload`: base64 serialized state bytes; an empty state may have an empty
  byte string;
* `version`: non-negative state version;
* `lastSortedUniqueId`: the historical V1 wire spelling for the tag's latest
  SUID. The semantic value is the same SUID described elsewhere as
  `lastSortableUniqueId`; the spelling MUST NOT be silently changed in V1;
* `tagGroup`, `tagContent`, `tagProjector`, `tagPayloadName`, and
  `projectorVersion`: type and identity metadata; and
* optional `actualPayloadName` when the serialized payload's concrete type is
  different from its default payload name.

An unknown or malformed tag-state ID is rejected before projection work. A
valid but empty tag-state response is still a successful response with zero
version, empty payload, and the request's identity metadata.

### 5.4 `POST /api/sekiban/serialized/query`

Request:

```json
{
  "queryType": "QueryName",
  "queryParamsJson": "{\"field\":\"value\"}",
  "waitForSortableUniqueId": "<optional SUID>"
}
```

`queryParamsJson` is a JSON document carried as a string and is returned as a
string inside `resultJson`:

```json
{"resultJson":"<serialized JSON result>"}
```

When `waitForSortableUniqueId` is present, the server waits until the mapped
projection has observed that SUID or returns its documented timeout/error. A
query type that is not mapped is a client error; a projection-disabled
deployment may return service-unavailable. Query execution is read-only.

### 5.5 `POST /api/sekiban/serialized/list-query`

The request shape is the same as 5.4. The response is:

```json
{
  "itemsJson": "[<serialized items>]",
  "totalCount": 0,
  "totalPages": 0,
  "currentPage": 1,
  "pageSize": 20
}
```

The four pagination fields are nullable because a query may not expose paging.
`itemsJson` MUST itself be valid JSON, normally an array. The endpoint is
read-only and follows the same mapping, wait, disabled-mode, and timeout rules
as 5.4.

## 6. Error taxonomy

The response body MUST be JSON and MUST contain an `error` string. A stable
`code` is required for envelope-shape failures and is recommended for all
other failures. Message text is diagnostic, not a compatibility key.

| Code/class | Meaning | Reference HTTP status |
| --- | --- | --- |
| `malformed_commit_envelope` | Invalid JSON/shape, wrong version member, null SUID expectation, or unsupported member type | 400 |
| `unsupported_commit_envelope_version` | `version` is an unsupported number | 400 |
| `consistency_conflict` | Exact/empty expectation did not match the authoritative tag head, or a reservation is active | 400 |
| `validation_error` | Invalid tag, tag-state ID, query, or candidate relationship | 400 |
| `projection_unavailable` | The requested query projection is intentionally disabled or unavailable | 503 |
| `timeout` | The storage/projection wait exceeded its bound | 504 |
| `partial_write` | Some durable records exist and some requested records do not | 500 or 400, with the explicit report below |
| `internal_error` | No more specific public classification is available | 500 |

## 7. Storage outcome and partial failure

The contract does not promise atomicity for every provider. A provider with a
transaction spanning all event and tag partitions MAY report an atomic failure:
either all requested durable records exist or none do.

A provider such as supported Cosmos storage, where event documents can be
created in independent partitions, MAY instead produce a partial outcome. In
that case the implementation MUST NOT claim atomicity and MUST NOT delete a
durable event merely because a sibling event or tag write failed. The failure
response MUST explicitly report at least:

```json
{
  "error": "serialized commit partially failed",
  "code": "partial_write",
  "partial": {
    "writtenEventIds": ["<uuid>"],
    "failedEventIds": ["<uuid>"],
    "writtenTags": ["group:content"],
    "missingTags": ["group:other"],
    "eventsDeleted": false,
    "retryable": true
  }
}
```

The lists may be empty when the provider failed before the relevant phase, but
the response MUST say which outcome occurred. A caller MUST reconcile a
partial result by rereading tag heads and event/query state before retrying;
blindly repeating a request can create a second event because V1 has no caller
event ID/idempotency key.

## 8. What the HTTP boundary cannot prove

The suite can prove observable reservation conflicts, exact/empty matching,
retry, SUID ordering, and restart recovery. It cannot force a provider fault
without a provider fault-injection API, inspect an in-memory reservation token,
or prove the internal seed read except through the post-restart monotonic
observable. Those limits are recorded as findings rather than converted into
green claims. Provider-specific partial-write injection remains a required
downstream/storage-lane verification.

## 9. Out of scope

This V1 contract does not define projection or materialized-view ABI, WASM
exports, V2 envelopes, `RemoteSekibanExecutor` behaviour, TypeScript or
Cloudflare integration, release/package publication, or performance/load
characteristics.
