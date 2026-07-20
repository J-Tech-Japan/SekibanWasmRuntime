#!/usr/bin/env python3
"""Verify that each release lane only runs for its own tag prefix (SWR-G072).

A published GitHub Release fans out to every workflow listening on
`release: [published]`. Before this guard, the NuGet release `v1.0.0-preview.2`
started `release-rust-crates.yml`, whose crate-version consistency check failed
against the unrelated 0.1.0 crates (run 29709790165). Nothing was published, but
the run was red, and the reverse would happen on the next Rust release.

This script does not re-implement the guard: it reads the `if:` expression from
each job in the committed workflow YAML, evaluates that expression against
synthetic event payloads, and asserts which jobs run. A drift between the
workflow and the intended scoping therefore fails here.

Usage: python3 scripts/release/check-release-lane-tag-scoping.py
Exit: 0 all expectations hold, 1 otherwise.
"""

from __future__ import annotations

import pathlib
import re
import sys

import yaml

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
RUST_LANE = ".github/workflows/release-rust-crates.yml"
NUGET_LANE = ".github/workflows/release-nuget-preview.yml"
REPO = "J-Tech-Japan/SekibanWasmRuntime"

# Every prefix that is NOT the NuGet lane. None of these start with "v", which is
# what lets the NuGet lane scope itself positively instead of enumerating a
# deny-list that a future lane could slip past.
OTHER_LANE_TAGS = [
    "rust-v0.1.1",
    "runtime-host-v1.0.0-preview.3",
    "ts-v0.1.0",
    "swift-v0.1.0",
    "moonbit-v0.1.0",
    "src/lib/sekiban-go/v0.1.0",
]


def evaluate(expression: str, context: dict) -> bool:
    """Evaluate the subset of GitHub Actions expression syntax these guards use."""
    expr = " ".join(expression.split())

    def starts_with(value, prefix) -> bool:
        return str(value).startswith(str(prefix))

    # github.event.release.tag_name is absent (null) on non-release events.
    substitutions = {
        "github.event_name": repr(context["event_name"]),
        "github.event.release.tag_name": repr(context.get("tag_name", "")),
        "github.repository": repr(context.get("repository", REPO)),
        "inputs.publish": repr(context.get("publish", False)),
    }
    for token, value in substitutions.items():
        expr = expr.replace(token, value)

    expr = re.sub(r"\bstartsWith\(", "starts_with(", expr)
    expr = expr.replace("&&", " and ").replace("||", " or ")
    expr = re.sub(r"\btrue\b", "True", expr)
    expr = re.sub(r"\bfalse\b", "False", expr)

    leftover = re.search(r"\bgithub\.[\w.]+|\binputs\.[\w.]+", expr)
    if leftover:
        raise AssertionError(f"unsupported expression context {leftover.group(0)!r} in: {expression}")

    return bool(eval(expr, {"__builtins__": {}}, {"starts_with": starts_with}))  # noqa: S307


def job_runs(workflow: str, job: str, context: dict) -> bool:
    data = yaml.safe_load((REPO_ROOT / workflow).read_text())
    condition = data["jobs"][job].get("if")
    # A job with no `if` always runs for events the workflow listens on.
    return True if condition is None else evaluate(condition, context)


def resolve_crate_version(raw: str) -> str:
    """Mirror of the workflow's "Resolve crate version" shell step."""
    return raw.removeprefix("rust-").removeprefix("v")


def main() -> int:
    failures: list[str] = []
    checks = 0

    def expect(label: str, actual, expected) -> None:
        nonlocal checks
        checks += 1
        status = "ok  " if actual == expected else "FAIL"
        print(f"  [{status}] {label}: {actual!r} (expected {expected!r})")
        if actual != expected:
            failures.append(label)

    release = lambda tag: {"event_name": "release", "tag_name": tag}  # noqa: E731

    print("The incident: a NuGet release must not start the Rust lane")
    incident = release("v1.0.0-preview.2")
    expect("rust check on v1.0.0-preview.2", job_runs(RUST_LANE, "check", incident), False)
    expect("rust publish on v1.0.0-preview.2", job_runs(RUST_LANE, "publish", incident), False)
    expect("nuget readiness on v1.0.0-preview.2", job_runs(NUGET_LANE, "readiness", incident), True)
    expect("nuget publish on v1.0.0-preview.2", job_runs(NUGET_LANE, "publish", incident), True)

    print("\nThe symmetric case: a Rust release must not start the NuGet lane")
    rust = release("rust-v0.1.1")
    expect("rust check on rust-v0.1.1", job_runs(RUST_LANE, "check", rust), True)
    expect("rust publish on rust-v0.1.1", job_runs(RUST_LANE, "publish", rust), True)
    expect("nuget readiness on rust-v0.1.1", job_runs(NUGET_LANE, "readiness", rust), False)
    expect("nuget publish on rust-v0.1.1", job_runs(NUGET_LANE, "publish", rust), False)

    print("\nEvery other lane's prefix starts neither lane")
    for tag in OTHER_LANE_TAGS:
        if tag.startswith("rust-v"):
            continue
        ctx = release(tag)
        expect(f"rust check on {tag}", job_runs(RUST_LANE, "check", ctx), False)
        expect(f"nuget readiness on {tag}", job_runs(NUGET_LANE, "readiness", ctx), False)

    print("\nA release in a fork never publishes")
    fork = {"event_name": "release", "tag_name": "rust-v0.1.1", "repository": "someone/fork"}
    expect("rust publish in fork", job_runs(RUST_LANE, "publish", fork), False)
    expect(
        "nuget publish in fork",
        job_runs(NUGET_LANE, "publish", {**fork, "tag_name": "v1.0.0-preview.2"}),
        False,
    )

    print("\nworkflow_dispatch and pull_request behavior is unchanged")
    dispatch_check = {"event_name": "workflow_dispatch", "publish": False}
    dispatch_publish = {"event_name": "workflow_dispatch", "publish": True}
    expect("rust check on dispatch", job_runs(RUST_LANE, "check", dispatch_check), True)
    expect("rust publish on dispatch publish=false", job_runs(RUST_LANE, "publish", dispatch_check), False)
    expect("rust publish on dispatch publish=true", job_runs(RUST_LANE, "publish", dispatch_publish), True)
    expect("nuget readiness on dispatch", job_runs(NUGET_LANE, "readiness", dispatch_check), True)
    expect("nuget publish on dispatch", job_runs(NUGET_LANE, "publish", dispatch_check), False)
    pull_request = {"event_name": "pull_request"}
    expect("nuget readiness on pull_request", job_runs(NUGET_LANE, "readiness", pull_request), True)
    expect("nuget publish on pull_request", job_runs(NUGET_LANE, "publish", pull_request), False)

    print("\nCrate version resolves from the prefixed tag")
    expect("resolve rust-v0.1.1", resolve_crate_version("rust-v0.1.1"), "0.1.1")
    expect("resolve rust-v1.0.0-preview.2", resolve_crate_version("rust-v1.0.0-preview.2"), "1.0.0-preview.2")
    expect("resolve dispatch input 0.1.0", resolve_crate_version("0.1.0"), "0.1.0")

    print()
    if failures:
        print(f"FAIL: {len(failures)}/{checks} expectations did not hold:", file=sys.stderr)
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1
    print(f"PASS: all {checks} release-lane tag scoping expectations hold.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
