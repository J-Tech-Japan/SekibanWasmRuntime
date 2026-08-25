# Serialized DCB V1 external black-box run

- Result: **PASS**
- Source repository root: <source checkout>
- External suite repository root: <fresh temporary Git checkout>
- External invocation proven: **true** (fresh Git repository, different root, suite cwd recorded in JSON; temporary root is removed after the run)
- HTTP-only suite: **true** (Python standard library; no local implementation import)
- Restart lifecycle command executed: **true**
- Deliberately broken commit tag comparison (HTTP proxy) failed as expected: **true**
- Target: http://127.0.0.1:65130
- Suite source: conformance/serialized-dcb-v1/suite.py

## Scenario markers

    before.log:BEFORE_RESTART=PASS tag=weather:g082-8f822ebde665 head=063923233801977885001515325769
    before.log:CONFORMANCE_RESULT=PASS phase=before-restart requests=32
    after.log:AFTER_RESTART=PASS tag=weather:g082-8f822ebde665 old=063923233801977885001515325769 new=063923233807276740601626843551
    after.log:CONFORMANCE_RESULT=PASS phase=after-restart requests=6
    negative.log:CONFORMANCE_RESULT=FAIL detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi04ZjgyMmViZGU2NjUtYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTI1VDA1OjUwOjEwLjk0OVoifQ==', 'sortableUniqueIdValue': '063923233810963245400019442796', 'id': '01a03778-1213-7f53-80ec-f10d55df3450', 'eventMetadata': {'causationId': '01a03778-1213-7f53-80ec-f10d55df3450', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-8f822ebde665'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-8f822ebde665', 'version': 1, 'writtenAt': '2026-08-25T05:50:10.9697657+00:00'}], 'duration': '00:00:00.0110939'}
    negative.log:BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi04ZjgyMmViZGU2NjUtYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTI1VDA1OjUwOjEwLjk0OVoifQ==', 'sortableUniqueIdValue': '063923233810963245400019442796', 'id': '01a03778-1213-7f53-80ec-f10d55df3450', 'eventMetadata': {'causationId': '01a03778-1213-7f53-80ec-f10d55df3450', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-8f822ebde665'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-8f822ebde665', 'version': 1, 'writtenAt': '2026-08-25T05:50:10.9697657+00:00'}], 'duration': '00:00:00.0110939'}

## Findings boundary

The normative findings ledger is docs/compatibility/serialized-dcb-v1-findings.md. The run proves the observable scenarios; it does not convert provider partial-write or internal allocator-seed limits into zero findings.
