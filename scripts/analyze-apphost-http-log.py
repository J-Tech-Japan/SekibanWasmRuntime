#!/usr/bin/env python3
import argparse
import json
import math
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Dict, List, Optional


@dataclass(frozen=True)
class PhaseWindow:
    name: str
    start: datetime
    end: datetime
    total_operations: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Summarize ASP.NET apphost request counts by benchmark phase and overall latency drift."
    )
    parser.add_argument("--benchmark-json", required=True, type=Path)
    parser.add_argument("--apphost-log", required=True, type=Path)
    parser.add_argument(
        "--endpoint",
        action="append",
        default=[],
        help="Endpoint mapping in label=url form. May be provided multiple times.",
    )
    parser.add_argument("--output", type=Path)
    return parser.parse_args()


def parse_datetime(value: str) -> datetime:
    value = value.rstrip("Z")
    if "." in value:
        head, frac = value.split(".", 1)
        frac = frac[:6]
        value = f"{head}.{frac}+00:00"
    else:
        value = value + "+00:00"
    return datetime.fromisoformat(value)


def load_phase_windows(path: Path) -> List[PhaseWindow]:
    payload = json.loads(path.read_text())
    current = parse_datetime(payload["StartedAtUtc"])
    windows: List[PhaseWindow] = []
    for phase in payload["Phases"]:
        duration = timedelta(seconds=phase["DurationSeconds"])
        windows.append(
            PhaseWindow(
                name=phase["Name"],
                start=current,
                end=current + duration,
                total_operations=int(phase["TotalOperations"]),
            )
        )
        current += duration
    return windows


def percentile(sorted_values: List[float], fraction: float) -> float:
    if not sorted_values:
        return 0.0
    index = max(0, math.ceil(len(sorted_values) * fraction) - 1)
    return sorted_values[index]


def summarize_latencies(values: List[float]) -> Dict[str, float]:
    if not values:
        return {
            "count": 0,
            "first5000_avg_ms": 0.0,
            "last5000_avg_ms": 0.0,
            "p50_ms": 0.0,
            "p95_ms": 0.0,
            "max_ms": 0.0,
        }

    sample_count = min(5000, len(values))
    sorted_values = sorted(values)
    first_values = values[:sample_count]
    last_values = values[-sample_count:]
    return {
        "count": len(values),
        "first5000_avg_ms": round(sum(first_values) / len(first_values), 3),
        "last5000_avg_ms": round(sum(last_values) / len(last_values), 3),
        "p50_ms": round(percentile(sorted_values, 0.50), 3),
        "p95_ms": round(percentile(sorted_values, 0.95), 3),
        "max_ms": round(sorted_values[-1], 3),
    }


def extract_timestamp_token(line: str) -> Optional[str]:
    for token in line.split():
        if token.startswith("20") and token.endswith("Z") and "T" in token:
            return token
    return None


def extract_duration_ms(line: str) -> Optional[float]:
    suffix = line.rsplit(" ", 1)[-1].strip()
    if not suffix.endswith("ms"):
        return None
    try:
        return float(suffix[:-2])
    except ValueError:
        return None


def parse_endpoints(endpoint_args: List[str]) -> Dict[str, str]:
    if not endpoint_args:
        raise SystemExit("At least one --endpoint value must be provided.")

    endpoints: Dict[str, str] = {}
    for item in endpoint_args:
        if "=" not in item:
            raise SystemExit(f"Invalid --endpoint value: {item}")

        label, url = item.split("=", 1)
        label = label.strip()
        url = url.strip()

        if not label:
            raise SystemExit(f"Invalid --endpoint value with empty label: {item}")
        if not url:
            raise SystemExit(f"Invalid --endpoint value with empty url: {item}")
        if label in endpoints:
            raise SystemExit(f"Duplicate --endpoint label: {label}")

        endpoints[label] = url

    return endpoints


def find_phase_name(timestamp: datetime, phase_windows: List[PhaseWindow]) -> Optional[str]:
    for phase in phase_windows:
        if phase.start <= timestamp < phase.end:
            return phase.name
    return None


def analyze_log(
    log_path: Path,
    phase_windows: List[PhaseWindow],
    endpoints: Dict[str, str],
) -> Dict[str, object]:
    phase_counts = {
        label: {phase.name: 0 for phase in phase_windows}
        for label in endpoints
    }
    finish_counts = {
        label: {phase.name: 0 for phase in phase_windows}
        for label in endpoints
    }
    latencies = {label: [] for label in endpoints}
    start_marker = "Request starting HTTP/1.1"
    finish_marker = "Request finished HTTP/1.1"

    with log_path.open(errors="ignore") as handle:
        for line in handle:
            if start_marker in line:
                timestamp_token = extract_timestamp_token(line)
                if timestamp_token is None:
                    continue
                timestamp = parse_datetime(timestamp_token)
                phase_name = find_phase_name(timestamp, phase_windows)
                if phase_name is None:
                    continue
                for label, url in endpoints.items():
                    if url not in line:
                        continue
                    phase_counts[label][phase_name] += 1
                    break
                continue

            if finish_marker in line:
                timestamp_token = extract_timestamp_token(line)
                if timestamp_token is None:
                    continue
                timestamp = parse_datetime(timestamp_token)
                phase_name = find_phase_name(timestamp, phase_windows)
                if phase_name is None:
                    continue
                duration_ms = extract_duration_ms(line)
                if duration_ms is None:
                    continue
                for label, url in endpoints.items():
                    if url not in line:
                        continue
                    finish_counts[label][phase_name] += 1
                    latencies[label].append(duration_ms)
                    break

    return {
        "phase_windows": [
            {
                "name": phase.name,
                "start_utc": phase.start.astimezone(timezone.utc).isoformat().replace("+00:00", "Z"),
                "end_utc": phase.end.astimezone(timezone.utc).isoformat().replace("+00:00", "Z"),
                "total_operations": phase.total_operations,
            }
            for phase in phase_windows
        ],
        "endpoints": {
            label: {
                "url": endpoints[label],
                "request_starts_by_phase": phase_counts[label],
                "request_finishes_by_phase": finish_counts[label],
                "latency_ms": summarize_latencies(latencies[label]),
            }
            for label in endpoints
        },
    }


def main() -> int:
    args = parse_args()
    endpoints = parse_endpoints(args.endpoint)
    phase_windows = load_phase_windows(args.benchmark_json)
    result = analyze_log(args.apphost_log, phase_windows, endpoints)
    output = json.dumps(result, indent=2)

    if args.output:
        args.output.write_text(output + "\n")
    else:
        print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
