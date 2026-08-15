# Serialized DCB V1 external black-box run

- Result: **PASS**
- Source repository root: <source checkout>
- External suite repository root: <fresh temporary Git checkout>
- External invocation proven: **true** (fresh Git repository, different root, suite cwd recorded in JSON; temporary root is removed after the run)
- HTTP-only suite: **true** (Python standard library; no local implementation import)
- Restart lifecycle command executed: **true**
- Deliberately broken commit tag comparison (HTTP proxy) failed as expected: **true**
- Target: http://127.0.0.1:57829
- Suite source: conformance/serialized-dcb-v1/suite.py

## Scenario markers

    before.log:BEFORE_RESTART=PASS tag=weather:g082-769db0b4a7fa head=063922370658001131301643047315
    before.log:CONFORMANCE_RESULT=PASS phase=before-restart requests=32
    after.log:AFTER_RESTART=PASS tag=weather:g082-769db0b4a7fa old=063922370658001131301643047315 new=063922370667673401702136463127
    after.log:CONFORMANCE_RESULT=PASS phase=after-restart requests=6
    negative.log:CONFORMANCE_RESULT=FAIL detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi03NjlkYjBiNGE3ZmEtYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTE1VDA2OjA0OjMyLjcyOVoifQ==', 'sortableUniqueIdValue': '063922370672740586001898298693', 'id': '01a00405-a064-7ee7-bbdd-a4fed0d406c1', 'eventMetadata': {'causationId': '01a00405-a064-7ee7-bbdd-a4fed0d406c1', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-769db0b4a7fa'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-769db0b4a7fa', 'version': 1, 'writtenAt': '2026-08-15T06:04:32.7426511+00:00'}], 'duration': '00:00:00.0155969'}
    negative.log:BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE detail=broken-commit-exact-conflict: expected conflict HTTP 400/409, got 200: {'writtenEvents': [{'payload': 'eyJmb3JlY2FzdElkIjoiZzA4Mi03NjlkYjBiNGE3ZmEtYnJva2VuLWNvbW1pdCIsImxvY2F0aW9uIjoiVG9reW8iLCJ0ZW1wZXJhdHVyZUMiOjIxLCJzdW1tYXJ5IjoiU1dSLUcwODIiLCJjcmVhdGVkQXQiOiIyMDI2LTA4LTE1VDA2OjA0OjMyLjcyOVoifQ==', 'sortableUniqueIdValue': '063922370672740586001898298693', 'id': '01a00405-a064-7ee7-bbdd-a4fed0d406c1', 'eventMetadata': {'causationId': '01a00405-a064-7ee7-bbdd-a4fed0d406c1', 'correlationId': 'SerializedCommit', 'executedUser': 'SerializedSekibanExecutor'}, 'tags': ['weather:g082-769db0b4a7fa'], 'eventPayloadName': 'WeatherForecastCreated'}], 'tagWriteResults': [{'tag': 'weather:g082-769db0b4a7fa', 'version': 1, 'writtenAt': '2026-08-15T06:04:32.7426511+00:00'}], 'duration': '00:00:00.0155969'}

## Findings boundary

The normative findings ledger is docs/compatibility/serialized-dcb-v1-findings.md. The run proves the observable scenarios; it does not convert provider partial-write or internal allocator-seed limits into zero findings.
