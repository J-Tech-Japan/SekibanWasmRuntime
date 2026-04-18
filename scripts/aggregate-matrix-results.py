#!/usr/bin/env python3
"""
Aggregate a benchmark-matrix manifest + per-cell JSON results into a markdown table
suitable for pasting into docs/benchmark-results.md.

Usage:
  scripts/aggregate-matrix-results.py benchmarks/results/matrix/matrix-<timestamp>.txt

The manifest format is the CSV the orchestrator writes:
  runtime,mode,output-json,rss-log,peak-rss-mb,status=N
"""
from __future__ import annotations

import json
import sys
from pathlib import Path


def load_phase(result: dict, name: str) -> dict | None:
    for phase in result.get("Phases", []):
        if phase.get("Name") == name:
            return phase
    return None


def fmt_float(value: float | None, ndigits: int = 1) -> str:
    if value is None:
        return "-"
    return f"{value:,.{ndigits}f}"


def main() -> int:
    if len(sys.argv) != 2:
        sys.stderr.write("usage: aggregate-matrix-results.py <manifest.txt>\n")
        return 1

    manifest_path = Path(sys.argv[1])
    rows: list[tuple] = []
    for line in manifest_path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        parts = line.split(",")
        if len(parts) < 5:
            continue
        runtime, mode, output_json_str, _rss_log, peak_rss_str = parts[:5]
        status = ""
        if len(parts) >= 6:
            status = parts[5]

        output_json = Path(output_json_str)
        if not output_json.is_file():
            rows.append((runtime, mode, None, None, None, None, None, status))
            continue

        try:
            data = json.loads(output_json.read_text(encoding="utf-8"))
        except Exception as exc:  # noqa: BLE001
            sys.stderr.write(f"skip {output_json}: {exc}\n")
            rows.append((runtime, mode, None, None, None, None, None, status))
            continue

        weather = load_phase(data, "WeatherBulk")
        reservation = load_phase(data, "ReservationLifecycle")
        query = load_phase(data, "QueryPerformance")

        rows.append((
            runtime,
            mode,
            weather.get("EventsPerSecond") if weather else None,
            reservation.get("EventsPerSecond") if reservation else None,
            query.get("OperationsPerSecond") if query else None,
            data.get("TotalDurationSeconds"),
            float(peak_rss_str) if peak_rss_str and peak_rss_str != "(n/a)" else None,
            status,
        ))

    headers = [
        "Runtime",
        "Mode",
        "Weather eps",
        "Reservation eps",
        "Query ops/sec",
        "Wall-clock (s)",
        "Peak RSS (MB)",
        "Status",
    ]
    print("| " + " | ".join(headers) + " |")
    print("|" + "|".join(["---"] * len(headers)) + "|")
    for runtime, mode, weather_eps, reservation_eps, query_ops, wall, peak, status in rows:
        cells = [
            runtime,
            mode,
            fmt_float(weather_eps, 0),
            fmt_float(reservation_eps, 0),
            fmt_float(query_ops, 0) if query_ops is not None else "skipped",
            fmt_float(wall, 1),
            fmt_float(peak, 1),
            status or "-",
        ]
        print("| " + " | ".join(str(c) for c in cells) + " |")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
