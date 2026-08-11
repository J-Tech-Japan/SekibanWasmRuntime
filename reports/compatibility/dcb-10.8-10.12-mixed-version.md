# DCB 10.8 / 10.12 mixed-version evidence

The serialized contract black-box suite was executed against the fixed head
(`scripts/contract/run-serialized-dcb-contract-baseline.sh`, 59/59 passed).
Both directional compatibility probes use the same V1 envelope and explicit
consistency tokens:

| Direction | Executed result |
| --- | --- |
| 10.12.0 client → 10.8.x server | V1 envelope and exact expected token remain readable; matching writes pass. |
| 10.8.x client → 10.12.0 server | V1 envelope remains readable; the 10.12.0 SEK-G22 reservation path re-reads the event store under lock when cache is empty before exact-match evaluation. |

The probes also confirmed serialized null reservations are rejected; clients
must send an empty string for AssertEmpty. Historical 10.1.8/10.2.2 sample
pins remain in place as old-client fixtures.
