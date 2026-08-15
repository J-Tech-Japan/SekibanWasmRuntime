#!/usr/bin/env python3
"""Deliberately broken commit-consistency target for the negative proof."""

from __future__ import annotations

import argparse
import http.client
import http.server
import json
from urllib.parse import urlsplit


class ForwardingHandler(http.server.BaseHTTPRequestHandler):
    upstream: tuple[str, int]

    def _forward(self) -> None:
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        host, port = self.upstream
        connection = http.client.HTTPConnection(host, port, timeout=60)
        try:
            path = self.path.split("?", 1)[0]
            if path == "/api/sekiban/serialized/commit":
                decoded = json.loads(body.decode("utf-8"))
                consistency_tags = decoded.get("consistencyTags")
                if isinstance(consistency_tags, list) and consistency_tags:
                    # Deliberately broken implementation: drop the caller's
                    # consistency assertions, so stale exact/empty writes are
                    # accepted instead of rejected by the target.
                    decoded["consistencyTags"] = []
                    body = json.dumps(decoded, separators=(",", ":")).encode("utf-8")
            headers = {
                key: value
                for key, value in self.headers.items()
                if key.lower() not in {"host", "content-length", "connection"}
            }
            connection.request(self.command, self.path, body=body, headers=headers)
            response = connection.getresponse()
            response_body = response.read()

            self.send_response(response.status, response.reason)
            for key, value in response.getheaders():
                if key.lower() not in {"content-length", "connection", "transfer-encoding"}:
                    self.send_header(key, value)
            self.send_header("Content-Length", str(len(response_body)))
            self.end_headers()
            self.wfile.write(response_body)
        finally:
            connection.close()

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        self._forward()

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        self._forward()

    def log_message(self, format: str, *args: object) -> None:
        return


def main() -> int:
    parser = argparse.ArgumentParser(description="Run a deliberately broken commit-consistency HTTP proxy.")
    parser.add_argument("--upstream", required=True)
    parser.add_argument("--port", required=True, type=int)
    args = parser.parse_args()
    upstream = urlsplit(args.upstream)
    if upstream.scheme != "http" or upstream.hostname is None:
        raise SystemExit("--upstream must be an http:// URL")
    ForwardingHandler.upstream = (upstream.hostname, upstream.port or 80)
    server = http.server.ThreadingHTTPServer(("127.0.0.1", args.port), ForwardingHandler)
    try:
        server.serve_forever()
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
