#!/usr/bin/env python3
"""Deliberately broken HTTP target used only for the negative conformance proof."""

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
            headers = {
                key: value
                for key, value in self.headers.items()
                if key.lower() not in {"host", "content-length", "connection"}
            }
            connection.request(self.command, self.path, body=body, headers=headers)
            response = connection.getresponse()
            response_body = response.read()
            if self.path.split("?", 1)[0] == "/api/sekiban/serialized/tag-latest-sortable" and response.status == 200:
                decoded = json.loads(response_body.decode("utf-8"))
                if decoded.get("exists") and isinstance(decoded.get("lastSortableUniqueId"), str):
                    decoded["lastSortableUniqueId"] += "-BROKEN"
                    response_body = json.dumps(decoded, separators=(",", ":")).encode("utf-8")

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
    parser = argparse.ArgumentParser(description="Run a deliberately broken tag-latest HTTP proxy.")
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
