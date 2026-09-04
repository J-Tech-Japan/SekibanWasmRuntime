#!/usr/bin/env python3
"""Kill regressions that parse conformance headers without forwarding them."""

from __future__ import annotations

import http.server
import json
import subprocess
import sys
import tempfile
import threading
import uuid
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
SUITE_PATH = REPO_ROOT / "conformance" / "serialized-dcb-v1" / "suite.py"
FIXTURE_PATH = REPO_ROOT / "conformance" / "serialized-dcb-v1" / "fixture-weather.json"


def fail(message: str) -> None:
    raise SystemExit(f"HEADER_KILLING_TEST=FAIL {message}")


def require(condition: bool, message: str) -> None:
    if not condition:
        fail(message)


class HeaderRequiredServer(http.server.ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, required_headers: dict[str, str]) -> None:
        super().__init__(("127.0.0.1", 0), HeaderRequiredHandler)
        self.required_headers = required_headers
        self.requests: list[dict[str, Any]] = []
        self.requests_lock = threading.Lock()


class HeaderRequiredHandler(http.server.BaseHTTPRequestHandler):
    server: HeaderRequiredServer

    def _json(self, status: int, value: dict[str, Any]) -> None:
        body = json.dumps(value, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        received = {name.casefold(): value for name, value in self.headers.items()}
        expected = self.server.required_headers
        authorized = all(received.get(name.casefold()) == value for name, value in expected.items())
        with self.server.requests_lock:
            self.server.requests.append(
                {
                    "path": self.path,
                    "body": body,
                    "authorized": authorized,
                    "headers": {
                        name.casefold(): received.get(name.casefold())
                        for name in expected
                    },
                }
            )

        if not authorized:
            self._json(428, {"error": "required header missing"})
        else:
            self._json(
                200,
                {"error": "conflict " + " ".join(f"{name}={value}" for name, value in expected.items())},
            )

    def log_message(self, format: str, *args: object) -> None:
        return


def run_suite(
    report_path: Path,
    state_path: Path,
    base_url: str,
    fixture_path: Path,
    headers: list[str] | None = None,
) -> subprocess.CompletedProcess[str]:
    command = [
        sys.executable,
        str(SUITE_PATH),
        "--base-url",
        base_url,
        "--fixture",
        str(fixture_path),
        "--phase",
        "broken-tag",
        "--state-file",
        str(state_path),
        "--report",
        str(report_path),
    ]
    for header in headers or []:
        command.extend(("--header", header))
    return subprocess.run(command, capture_output=True, text=True, check=False)


def scan_secret_free(paths: list[Path], outputs: list[str], tokens: list[str]) -> None:
    folded_tokens = [token.casefold() for token in tokens if token]
    for output in outputs:
        folded_output = output.casefold()
        require(not any(token in folded_output for token in folded_tokens), "header metadata leaked to output")
    for path in paths:
        folded_content = path.read_text(encoding="utf-8").casefold()
        require(not any(token in folded_content for token in folded_tokens), "header metadata leaked to artifact")


def main() -> int:
    header_name = "X-SWR-Header-Probe"
    second_header_name = "X-SWR-Second-Probe"
    sentinel = f"swr-header-sentinel-{uuid.uuid4().hex}"
    header_value = f"{sentinel}=part=with=equals"
    second_header_value = "repeatable-value"
    headers = [
        f"{header_name}={header_value}",
        f"{second_header_name}={second_header_value}",
    ]

    with tempfile.TemporaryDirectory(prefix="serialized-dcb-v1-headers.") as temporary:
        temporary_root = Path(temporary)
        state_path = temporary_root / "state.json"
        state_path.write_text(
            json.dumps(
                {
                    "token": "header-probe",
                    "tag": "header-probe-tag",
                    "head": "A",
                    "headAfterRestart": "B",
                }
            )
            + "\n",
            encoding="utf-8",
        )
        fixture_path = temporary_root / "fixture.json"
        fixture = json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))
        fixture["payloadTemplate"] = str(fixture["payloadTemplate"]).replace(
            "{timestamp}", "fixed-timestamp"
        )
        fixture_path.write_text(json.dumps(fixture) + "\n", encoding="utf-8")

        with HeaderRequiredServer(
            {
                header_name: header_value,
                second_header_name: second_header_value,
            }
        ) as server:
            server_thread = threading.Thread(target=server.serve_forever, daemon=True)
            server_thread.start()
            base_url = f"http://127.0.0.1:{server.server_address[1]}"

            no_header = run_suite(temporary_root / "no-header.json", state_path, base_url, fixture_path)
            require(no_header.returncode != 0, "required-header stub accepted the no-header invocation")
            require("got 428" in no_header.stderr, "missing header was not required")

            with_header = run_suite(temporary_root / "with-header.json", state_path, base_url, fixture_path, headers)
            require(with_header.returncode != 0, "header-enabled killing path did not reach the expected negative proof")
            require(
                "BROKEN_TAG_NEGATIVE=EXPECTED_FAILURE" in with_header.stdout,
                "required-header stub did not accept the header-enabled invocation",
            )

            with server.requests_lock:
                requests = list(server.requests)
            require(len(requests) == 2, "header killing invocations made an unexpected number of requests")
            require(not requests[0]["authorized"], "missing-header request was authorized")
            require(requests[1]["authorized"], "configured headers were not forwarded")
            require(
                requests[1]["body"] == requests[0]["body"],
                "custom headers changed request body bytes",
            )
            require(
                requests[1]["headers"]
                == {
                    header_name.casefold(): header_value,
                    second_header_name.casefold(): second_header_value,
                },
                "configured header values did not arrive intact",
            )

            before_invalid = len(requests)
            invalid = run_suite(
                temporary_root / "invalid.json",
                state_path,
                base_url,
                fixture_path,
                [f"{header_name}{sentinel}"],
            )
            with server.requests_lock:
                require(len(server.requests) == before_invalid, "invalid header syntax reached the network")
            invalid_output = invalid.stdout + invalid.stderr
            require(invalid.returncode != 0, "invalid header syntax unexpectedly succeeded")
            require("HTTP transport failed" not in invalid_output, "invalid syntax was rejected after network activity")

            server.shutdown()
            server_thread.join(timeout=5)

        artifacts = [path for path in temporary_root.rglob("*") if path.is_file()]
        scan_secret_free(
            artifacts,
            [no_header.stdout, no_header.stderr, with_header.stdout, with_header.stderr, invalid_output],
            [header_name, second_header_name, sentinel, header_value, second_header_value],
        )

    print("HEADER_KILLING_TEST=PASS missing-header-rejected header-enabled-accepted first-equals-preserved artifacts=secret-free")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
