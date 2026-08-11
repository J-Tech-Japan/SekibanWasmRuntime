# DCB 10.8 / 10.12 compatibility evidence boundary

Executed evidence is the same-baseline 10.12.0 serialized contract suite
(59/59 passed), plus restore/build/dependency resolution of the preserved
10.2.2 and 10.1.8 compatibility fixtures. No bidirectional runtime exchange
between 10.8.x and 10.12.0 was executed.

The documented compatibility directions remain behavioural boundaries, not
executed results. SEK-G22's cached-empty authoritative event-store re-read
under reservation lock is a published 10.12.0 source-derived finding, not an
observation made by this repository's runtime suite.
