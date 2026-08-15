# Serialized DCB V1 findings ledger

This ledger is part of SWR-G082. It is intentionally not a conformance
pass/fail summary: a green scenario run does not erase a disagreement or an
HTTP boundary that cannot expose a provider outcome.

Comparison method:

1. Draft serialized-dcb-v1.md from the issue contract, public boundary, and
   wire examples.
2. Run the Python standard-library suite from a temporary Git repository
   against the running host over HTTP only.
3. Compare the captured JSON responses with the normative fields and audit the
   provider-facing error path separately. The suite never imports a runtime
   assembly or implementation source.

| ID | Rule / observation | Classification | Decision and evidence |
| --- | --- | --- | --- |
| F-001 | The semantic tag-state head is a SortableUniqueId, but the existing V1 response key is lastSortedUniqueId. | Accepted wire finding | The normative page preserves this historical V1 spelling and calls out the distinction. A client must read that key; changing it would be a V1 wire break. The HTTP run observes and validates that key through the tag-state endpoint. |
| F-002 | A Cosmos partial event/tag write must be reported without deleting durable events. The current generic host maps a failed executor result to an error-only JSON object; the structured partial members required by the normative contract are not guaranteed at this HTTP boundary. | Implementation-gap / provider-lane finding | Do not claim atomicity. The provider exception names visible and failed IDs in diagnostic text, but this is not the structured report required for a language-neutral consumer. A Cosmos fault-injection run is not possible through the five public endpoints, so this remains an explicit follow-up finding rather than an invented green result. |
| F-003 | The reference host accepts the historical unversioned commit envelope as a compatibility extension, while V1 conformance requests require version 1. | Non-breaking extension | The extension is outside the V1 normative shape and does not change V1 behaviour. It is documented as an accepted compatibility extension, not used as evidence for the normative suite. |
| F-004 | Reservation expiry, the cancellation token, and the in-memory reservation token are not directly observable through the five HTTP endpoints. | Unobservable boundary item | The specification states the required finite window and cleanup rule. The suite proves conflict and retry, while the inability to force and inspect expiry is recorded here rather than treated as a pass. |
| F-005 | The allocator's persisted-head seed is internal; only its monotonic post-restart consequence is observable over HTTP. | Unobservable boundary item | Concurrent ID uniqueness/order and post-restart new > old are tested. The internal seed read is not claimed as directly proven. |

There is therefore no unjustified “zero findings” outcome. The observable
suite result may be green while F-002, F-004, and F-005 remain explicitly
reported limitations.
