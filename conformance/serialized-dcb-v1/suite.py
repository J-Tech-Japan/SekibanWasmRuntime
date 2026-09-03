#!/usr/bin/env python3
"""HTTP-only serialized DCB V1 conformance suite.

The program is deliberately standalone: Python's standard library is the only
dependency, and the fixture is the only implementation-specific input.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import datetime as dt
import json
import os
import sys
import threading
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


class CheckFailure(RuntimeError):
    pass


def check(condition: bool, message: str) -> None:
    if not condition:
        raise CheckFailure(message)


def now_text() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    check(isinstance(value, dict), f"fixture/state must be an object: {path}")
    return value


class HttpClient:
    def __init__(self, base_url: str) -> None:
        self.base_url = base_url.rstrip("/")
        self.records: list[dict[str, Any]] = []
        self._lock = threading.Lock()

    def post(self, name: str, path: str, body: dict[str, Any]) -> tuple[int, Any]:
        raw_request = json.dumps(body, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        request = Request(
            f"{self.base_url}{path}",
            data=raw_request,
            headers={"Accept": "application/json", "Content-Type": "application/json"},
            method="POST",
        )
        status = 0
        raw_response = b""
        try:
            with urlopen(request, timeout=60) as response:
                status = response.status
                raw_response = response.read()
        except HTTPError as error:
            status = error.code
            raw_response = error.read()
        except URLError as error:
            raise CheckFailure(f"{name}: HTTP transport failed: {error}") from error

        text = raw_response.decode("utf-8", errors="replace")
        try:
            decoded: Any = json.loads(text) if text else {}
        except json.JSONDecodeError:
            decoded = text
        with self._lock:
            self.records.append(
                {
                    "name": name,
                    "path": path,
                    "status": status,
                    "request": body,
                    "response": decoded,
                }
            )
        return status, decoded


def render(value: str, token: str, tag: str) -> str:
    return (
        value.replace("{id}", token)
        .replace("{tag}", tag)
        .replace("{timestamp}", now_text())
    )


class Conformance:
    def __init__(self, base_url: str, fixture: dict[str, Any]) -> None:
        self.client = HttpClient(base_url)
        self.fixture = fixture
        self.scenarios: dict[str, str] = {}

        required = (
            "eventPayloadName",
            "payloadTemplate",
            "tagTemplate",
            "tagGroupTemplate",
            "tagContentTemplate",
            "tagProjector",
            "tagStateIdTemplate",
            "tagStateLastIdKey",
            "scalarQuery",
            "listQuery",
        )
        for key in required:
            check(key in fixture, f"fixture is missing {key}")

    def tag(self, token: str) -> str:
        return render(str(self.fixture["tagTemplate"]), token, "")

    def tag_group(self, token: str) -> str:
        return render(str(self.fixture["tagGroupTemplate"]), token, self.tag(token))

    def tag_content(self, token: str) -> str:
        return render(str(self.fixture["tagContentTemplate"]), token, self.tag(token))

    def tag_state_id(self, token: str) -> str:
        return render(str(self.fixture["tagStateIdTemplate"]), token, self.tag(token))

    def payload_bytes(self, token: str, tag: str) -> bytes:
        template = render(str(self.fixture["payloadTemplate"]), token, tag)
        try:
            parsed = json.loads(template)
        except json.JSONDecodeError:
            return template.encode("utf-8")
        return json.dumps(parsed, separators=(",", ":"), ensure_ascii=False).encode("utf-8")

    def event_candidate(self, token: str, tags: list[str]) -> dict[str, Any]:
        payload = base64.b64encode(self.payload_bytes(token, tags[0])).decode("ascii")
        return {
            "payload": payload,
            "eventPayloadName": self.fixture["eventPayloadName"],
            "tags": tags,
        }

    def commit_body(
        self,
        token: str,
        tags: list[str],
        expectations: dict[str, Any] | None,
    ) -> dict[str, Any]:
        return {
            "version": 1,
            "eventCandidates": [self.event_candidate(token, tags)],
            "consistencyTags": [
                {"tag": tag, "lastSortableUniqueId": value}
                for tag, value in (expectations or {}).items()
            ],
        }

    def commit_raw(self, name: str, body: dict[str, Any]) -> tuple[int, Any]:
        return self.client.post(name, "/api/sekiban/serialized/commit", body)

    def commit(
        self,
        name: str,
        token: str,
        tags: list[str],
        expectations: dict[str, Any] | None,
    ) -> str:
        body = self.commit_body(token, tags, expectations)
        status, response = self.commit_raw(name, body)
        check(status == 200, f"{name}: expected HTTP 200, got {status}: {response}")
        check(isinstance(response, dict), f"{name}: response is not an object")
        events = response.get("writtenEvents")
        check(isinstance(events, list) and events, f"{name}: writtenEvents is empty")
        event = events[0]
        check(isinstance(event, dict), f"{name}: written event is not an object")
        expected_candidate = body["eventCandidates"][0]
        check(event.get("payload") == expected_candidate["payload"], f"{name}: payload bytes changed")
        check(event.get("eventPayloadName") == expected_candidate["eventPayloadName"], f"{name}: payload name changed")
        check(event.get("tags") == expected_candidate["tags"], f"{name}: tag list changed")
        suid = event.get("sortableUniqueIdValue")
        check(isinstance(suid, str) and suid, f"{name}: missing sortableUniqueIdValue")
        tag_results = response.get("tagWriteResults")
        check(isinstance(tag_results, list), f"{name}: tagWriteResults is not an array")
        written_tags = [entry.get("tag") for entry in tag_results if isinstance(entry, dict)]
        check(written_tags == tags, f"{name}: tagWriteResults do not preserve candidate tag order")
        return suid

    def latest(self, name: str, tag: str) -> tuple[bool, str]:
        status, response = self.client.post(
            name,
            "/api/sekiban/serialized/tag-latest-sortable",
            {"tag": tag},
        )
        check(status == 200, f"{name}: expected HTTP 200, got {status}: {response}")
        check(isinstance(response, dict), f"{name}: response is not an object")
        exists = response.get("exists")
        suid = response.get("lastSortableUniqueId")
        check(isinstance(exists, bool), f"{name}: exists is not boolean")
        check(isinstance(suid, str), f"{name}: lastSortableUniqueId is not a string")
        check((exists and bool(suid)) or (not exists and suid == ""), f"{name}: invalid empty/existing pair")
        return exists, suid

    def require_latest(self, name: str, tag: str, expected: str) -> None:
        exists, actual = self.latest(name, tag)
        check(exists and actual == expected, f"{name}: expected tag head {expected!r}, got {exists}/{actual!r}")

    def expect_conflict(self, name: str, body: dict[str, Any]) -> None:
        status, response = self.commit_raw(name, body)
        check(status in (400, 409), f"{name}: expected conflict HTTP 400/409, got {status}: {response}")
        check(isinstance(response, dict), f"{name}: conflict body is not JSON")
        check(isinstance(response.get("error"), str), f"{name}: conflict body has no error string")

    def tag_state(self, name: str, token: str, expected_suid: str) -> None:
        tag = self.tag(token)
        state_id = self.tag_state_id(token)
        status, response = self.client.post(
            name,
            "/api/sekiban/serialized/tag-state",
            {"tagStateId": state_id},
        )
        check(status == 200, f"{name}: expected HTTP 200, got {status}: {response}")
        check(isinstance(response, dict), f"{name}: response is not an object")
        payload = response.get("payload")
        check(isinstance(payload, str), f"{name}: payload is not base64 text")
        try:
            base64.b64decode(payload, validate=True)
        except (ValueError, binascii.Error) as error:
            raise CheckFailure(f"{name}: payload is not valid base64") from error
        check(isinstance(response.get("version"), int), f"{name}: version is not an integer")
        head_key = str(self.fixture["tagStateLastIdKey"])
        head = response.get(head_key)
        check(isinstance(head, str), f"{name}: missing {head_key}")
        check(head == expected_suid, f"{name}: expected state head {expected_suid!r}, got {head!r}")
        check(response.get("tagGroup") == self.tag_group(token), f"{name}: tagGroup mismatch")
        check(response.get("tagContent") == self.tag_content(token), f"{name}: tagContent mismatch")
        check(response.get("tagProjector") == self.fixture["tagProjector"], f"{name}: tagProjector mismatch")
        check(isinstance(response.get("tagPayloadName"), str), f"{name}: tagPayloadName missing")
        check(isinstance(response.get("projectorVersion"), str), f"{name}: projectorVersion missing")

    def queries(self, name: str, wait_for: str | None = None) -> None:
        scalar = dict(self.fixture["scalarQuery"])
        list_query = dict(self.fixture["listQuery"])
        for key, query in (("scalar", scalar), ("list", list_query)):
            body = {
                "queryType": query["queryType"],
                "queryParamsJson": query["queryParamsJson"],
            }
            if wait_for:
                body["waitForSortableUniqueId"] = wait_for
            path = "/api/sekiban/serialized/query" if key == "scalar" else "/api/sekiban/serialized/list-query"
            status, response = self.client.post(f"{name}-{key}", path, body)
            check(status == 200, f"{name}-{key}: expected HTTP 200, got {status}: {response}")
            check(isinstance(response, dict), f"{name}-{key}: response is not an object")
            result_key = "resultJson" if key == "scalar" else "itemsJson"
            raw = response.get(result_key)
            check(isinstance(raw, str), f"{name}-{key}: missing {result_key}")
            try:
                parsed = json.loads(raw)
            except json.JSONDecodeError as error:
                raise CheckFailure(f"{name}-{key}: {result_key} is not JSON") from error
            if key == "list":
                check(isinstance(parsed, list), f"{name}-list: itemsJson is not an array")
                for pagination_key in ("totalCount", "totalPages", "currentPage", "pageSize"):
                    check(pagination_key in response, f"{name}-list: missing {pagination_key}")

    def wrong_dialect(self) -> None:
        token = f"g087-wrong-dialect-{uuid.uuid4().hex[:12]}"
        tag = self.tag(token)
        status, response = self.commit_raw(
            "wrong-dialect",
            {
                "candidates": [self.event_candidate(token, [tag])],
                "consistency": [],
            },
        )
        check(status == 400, f"wrong-dialect: expected HTTP 400, got {status}: {response}")
        check(isinstance(response, dict), "wrong-dialect: response is not JSON")
        check(
            response.get("code") == "malformed_commit_envelope",
            f"wrong-dialect: expected malformed_commit_envelope, got {response}",
        )
        check(isinstance(response.get("error"), str), "wrong-dialect: response has no fixed error string")
        check(
            "AliasCollectionMember" in response["error"],
            f"wrong-dialect: expected AliasCollectionMember descriptor, got {response}",
        )
        check(token not in json.dumps(response, ensure_ascii=False), "wrong-dialect: response exposed request content")
        exists, head = self.latest("wrong-dialect-no-write", tag)
        check(not exists and head == "", "wrong-dialect: rejected request changed tag state")
        self.scenarios["wrong_dialect"] = "PASS"
        print(
            "WRONG_DIALECT=PASS status=400 code=malformed_commit_envelope "
            "descriptor=AliasCollectionMember isolation=PASS"
        )

    def before_restart(self, state_path: Path) -> None:
        self.wrong_dialect()
        token = f"g082-{uuid.uuid4().hex[:12]}"
        exact_tag = self.tag(token)

        first = self.commit("assert-empty-success", token, [exact_tag], {exact_tag: ""})
        exists, _ = self.latest("single-tag-read", exact_tag)
        check(exists, "single-tag-read: committed tag is absent")

        second = self.commit("exact-match-success", f"{token}-exact", [exact_tag], {exact_tag: first})
        self.expect_conflict(
            "exact-match-conflict",
            self.commit_body(f"{token}-stale", [exact_tag], {exact_tag: first}),
        )
        self.require_latest("exact-match-conflict-no-write", exact_tag, second)

        self.expect_conflict(
            "assert-empty-conflict",
            self.commit_body(f"{token}-empty-stale", [exact_tag], {exact_tag: ""}),
        )
        self.require_latest("assert-empty-conflict-no-write", exact_tag, second)

        multi_tag = self.tag(f"{token}-multi")
        self.expect_conflict(
            "multi-tag-one-conflict",
            self.commit_body(f"{token}-multi-event", [exact_tag, multi_tag], {exact_tag: "", multi_tag: ""}),
        )
        multi_exists, multi_head = self.latest("multi-tag-no-partial-event", multi_tag)
        check(not multi_exists and multi_head == "", "multi-tag conflict wrote the non-conflicting tag")

        retry_head = self.latest("retry-reread", exact_tag)[1]
        retry = self.commit("conflict-retry", f"{token}-retry", [exact_tag], {exact_tag: retry_head})
        check(retry > retry_head, "conflict-retry did not allocate a greater SUID")

        null_tag = self.tag(f"{token}-null")
        self.expect_conflict(
            "null-is-rejected",
            self.commit_body(f"{token}-null-event", [null_tag], {null_tag: None}),
        )
        null_exists, null_head = self.latest("null-is-rejected-no-write", null_tag)
        check(not null_exists and null_head == "", "null expectation caused a write")

        concurrent_tags = [self.tag(f"{token}-concurrent-{index}") for index in range(8)]

        def concurrent_commit(index: int) -> str:
            return self.commit(
                f"concurrent-{index}",
                f"{token}-concurrent-{index}",
                [concurrent_tags[index]],
                {},
            )

        with ThreadPoolExecutor(max_workers=len(concurrent_tags)) as executor:
            concurrent_ids = list(executor.map(concurrent_commit, range(len(concurrent_tags))))
        check(len(set(concurrent_ids)) == len(concurrent_ids), "concurrent commits allocated duplicate SUIDs")
        ordered_ids = sorted(concurrent_ids, key=lambda value: value.encode("utf-8"))
        check(all(left < right for left, right in zip(ordered_ids, ordered_ids[1:])), "concurrent SUIDs are not strictly orderable")
        for index, tag in enumerate(concurrent_tags):
            self.require_latest(f"concurrent-read-{index}", tag, concurrent_ids[index])

        self.tag_state("tag-state", token, retry)
        self.queries("query", wait_for=retry)

        state = {
            "token": token,
            "tag": exact_tag,
            "head": retry,
            "maxObservedHead": max([retry, *concurrent_ids], key=lambda value: value.encode("utf-8")),
            "createdAt": now_text(),
            "concurrentIds": concurrent_ids,
            "httpRequestCount": len(self.client.records),
        }
        state_path.parent.mkdir(parents=True, exist_ok=True)
        state_path.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
        self.scenarios["before_restart"] = "PASS"
        print(f"BEFORE_RESTART=PASS tag={exact_tag} head={retry}")

    def after_restart(self, state_path: Path) -> None:
        state = load_json(state_path)
        tag = str(state["tag"])
        old_head = str(state["head"])
        max_observed_head = str(state.get("maxObservedHead", old_head))
        exists, recovered_head = self.latest("restart-recovery-read", tag)
        check(exists and recovered_head == old_head, "restart did not recover the persisted tag head")
        new_head = self.commit("restart-recovery-write", f"{state['token']}-restart", [tag], {tag: recovered_head})
        check(
            new_head.encode("utf-8") > max_observed_head.encode("utf-8"),
            f"post-restart SUID regressed: max-observed={max_observed_head} new={new_head}",
        )
        self.require_latest("restart-recovery-final-read", tag, new_head)
        self.tag_state("restart-tag-state", str(state["token"]), new_head)
        self.queries("restart-query", wait_for=new_head)
        state["headAfterRestart"] = new_head
        state_path.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
        self.scenarios["after_restart"] = "PASS"
        print(f"AFTER_RESTART=PASS tag={tag} old={old_head} new={new_head}")

    def broken_tag_negative(self, state_path: Path) -> None:
        state = load_json(state_path)
        tag = str(state["tag"])
        check("headAfterRestart" in state, "broken-tag negative requires the after-restart state")
        stale_head = str(state["head"])
        current_head = str(state["headAfterRestart"])
        check(stale_head != current_head, "broken-tag negative did not receive a stale exact-match head")
        body = self.commit_body(
            f"{state['token']}-broken-commit",
            [tag],
            {tag: stale_head},
        )
        try:
            self.expect_conflict("broken-commit-exact-conflict", body)
        except CheckFailure as error:
            print(f"BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE detail={error}")
            self.scenarios["broken_tag_negative"] = "PASS"
            raise
        raise CheckFailure("deliberately broken target still rejected a stale exact-match commit")


def write_report(path: Path | None, conformance: Conformance, result: str, error: str | None = None) -> None:
    if path is None:
        return
    report = {
        "suite": "serialized-dcb-v1",
        "result": result,
        "httpOnly": True,
        "invocationCwd": os.getcwd(),
        "suiteRoot": str(Path(__file__).resolve().parent),
        "scenarios": conformance.scenarios,
        "requests": conformance.client.records,
    }
    if error:
        report["error"] = error
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Run serialized DCB V1 over HTTP.")
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--fixture", required=True, type=Path)
    parser.add_argument("--phase", choices=("before-restart", "after-restart", "broken-tag"), required=True)
    parser.add_argument("--state-file", required=True, type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()

    conformance = Conformance(args.base_url, load_json(args.fixture))
    try:
        if args.phase == "before-restart":
            conformance.before_restart(args.state_file)
        elif args.phase == "after-restart":
            conformance.after_restart(args.state_file)
        else:
            conformance.broken_tag_negative(args.state_file)
    except CheckFailure as error:
        write_report(args.report, conformance, "FAIL", str(error))
        print(f"CONFORMANCE_RESULT=FAIL detail={error}", file=sys.stderr)
        return 1

    write_report(args.report, conformance, "PASS")
    print(f"CONFORMANCE_RESULT=PASS phase={args.phase} requests={len(conformance.client.records)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
