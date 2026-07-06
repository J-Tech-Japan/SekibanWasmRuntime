// Generation tests for create-sekiban-wasm. Run via `npm test`
// (node --test test/), after `npm run sync-templates && npm run build`
// (wired as the `pretest` lifecycle script).
//
// For every language: generates a registry-mode project into a temp
// directory, validates the expected file tree, and runs the project's own
// bundled scripts/verify-no-local-sekiban-paths.sh guard from inside the
// generated directory (proving the bundled scripts are portable outside the
// monorepo). If the language's toolchain (cargo/go/swift/moon) is not
// installed, the guard run is skipped and the language is reported as
// tree-verified only, per the packet's escape hatch. rust and ts are
// additionally REQUIRED to have their guard PASS (rust's guard live-compiles
// against already-published crates.io crates; ts's guard is a static check);
// go/swift/moonbit guard failures are logged but do not fail the suite,
// since those languages' published packages/tags are still pending
// (SWR-G058/G061/G063/G065 follow-ups).
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PKG_ROOT = path.resolve(__dirname, "..");
const CLI = path.join(PKG_ROOT, "dist", "cli.js");

function runCli(args) {
  return spawnSync(process.execPath, [CLI, ...args], {
    encoding: "utf8",
    cwd: PKG_ROOT,
  });
}

function hasTool(name) {
  const res = spawnSync("bash", ["-lc", `command -v ${name}`], { stdio: "ignore" });
  return res.status === 0;
}

function mkTempDir(prefix) {
  return fs.mkdtempSync(path.join(os.tmpdir(), prefix));
}

const EXPECTED_TOP_LEVEL = {
  rust: ["Cargo.toml", "Domain", "Wasm", "Client", "AppHost", "scripts", "README.md"],
  ts: ["Wasm", "Client", "AppHost", "scripts", "README.md"],
  go: ["go.mod", "domain", "wasm", "client", "AppHost", "scripts", "README.md"],
  swift: ["Package.swift", "Sources", "AppHost", "scripts", "README.md"],
  moonbit: ["moon.work.json", "wasm", "client", "AppHost", "scripts", "README.md"],
};

// rust: live cargo check against published crates.io crates -- required PASS.
// ts/swift/moonbit: static guards -- required PASS regardless of publish status.
// go: live `go build`/`go vet` against the not-yet-published module -- best
// effort only (expected to fail until the src/lib/sekiban-go tag publishes).
const REQUIRED_GUARD_PASS = new Set(["rust", "ts", "swift", "moonbit"]);
const GUARD_TOOL = { rust: "cargo", ts: "node", go: "go", swift: "swift", moonbit: "moon" };

for (const language of Object.keys(EXPECTED_TOP_LEVEL)) {
  test(`generates and file-tree-validates ${language} (registry mode)`, () => {
    const dir = mkTempDir(`csw-${language}-`);
    const targetDir = path.join(dir, "out");
    const result = runCli(["--language", language, "--mode", "registry", "--dir", targetDir]);
    assert.equal(result.status, 0, `cli exited non-zero: ${result.stderr}`);
    for (const entry of EXPECTED_TOP_LEVEL[language]) {
      assert.ok(
        fs.existsSync(path.join(targetDir, entry)),
        `expected ${entry} to exist in generated ${language} project`,
      );
    }
    assert.ok(
      fs.existsSync(path.join(targetDir, "scripts", "verify-no-local-sekiban-paths.sh")),
      `expected scripts/verify-no-local-sekiban-paths.sh to exist in generated ${language} project`,
    );
  });

  test(`bundled guard is portable for ${language} (registry mode)`, () => {
    const tool = GUARD_TOOL[language];
    if (!hasTool(tool)) {
      console.log(`[generate.test] SKIP guard run for ${language}: ${tool} not found (tree-validated only)`);
      return;
    }

    const dir = mkTempDir(`csw-${language}-guard-`);
    const targetDir = path.join(dir, "out");
    const genResult = runCli(["--language", language, "--mode", "registry", "--dir", targetDir]);
    assert.equal(genResult.status, 0, `cli exited non-zero: ${genResult.stderr}`);

    const guardResult = spawnSync("bash", ["scripts/verify-no-local-sekiban-paths.sh"], {
      cwd: targetDir,
      encoding: "utf8",
    });

    if (REQUIRED_GUARD_PASS.has(language)) {
      assert.equal(
        guardResult.status,
        0,
        `${language} guard must pass in the generated project: ${guardResult.stdout}\n${guardResult.stderr}`,
      );
    } else if (guardResult.status !== 0) {
      console.log(
        `[generate.test] ${language} guard did not pass standalone (expected until its package/tag is published): ${guardResult.stderr.trim().slice(0, 300)}`,
      );
    }
  });
}

test("--language all generates every language under one directory", () => {
  const dir = mkTempDir("csw-all-");
  const targetDir = path.join(dir, "out");
  const result = runCli(["--language", "all", "--dir", targetDir]);
  assert.equal(result.status, 0, `cli exited non-zero: ${result.stderr}`);
  for (const language of Object.keys(EXPECTED_TOP_LEVEL)) {
    assert.ok(
      fs.existsSync(path.join(targetDir, language, "README.md")),
      `expected ${language} subdirectory to be generated under --language all`,
    );
  }
});

test("unknown --language value is rejected with a clear message", () => {
  const result = runCli(["--language", "cobol"]);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /Unknown --language value/);
});

test("--mode dev reports unavailable rather than generating broken output (ts: no dev-mode sample exists)", () => {
  const dir = mkTempDir("csw-dev-");
  const targetDir = path.join(dir, "out");
  const result = runCli(["--language", "ts", "--mode", "dev", "--dir", targetDir]);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /not available/);
  assert.equal(fs.existsSync(targetDir), false, "dev mode must not create output when unavailable");
});

test("rust --mode dev generates a standalone, cargo-buildable project (vendored wasm-projectors/rust crates)", () => {
  const dir = mkTempDir("csw-rust-dev-");
  const targetDir = path.join(dir, "out");
  const result = runCli(["--language", "rust", "--mode", "dev", "--dir", targetDir]);
  assert.equal(result.status, 0, `cli exited non-zero: ${result.stderr}`);
  for (const entry of ["Cargo.toml", "Wasm", "Client", "AppHost", "scripts", "vendor", "README.md"]) {
    assert.ok(fs.existsSync(path.join(targetDir, entry)), `expected ${entry} in generated rust dev project`);
  }
  for (const crate of ["sekiban-core", "sekiban-derive", "sekiban-mv", "sekiban-wasm", "sekiban-executor", "domain"]) {
    assert.ok(
      fs.existsSync(path.join(targetDir, "vendor", crate, "Cargo.toml")),
      `expected vendored crate ${crate} in generated rust dev project`,
    );
  }

  if (!hasTool("cargo")) {
    console.log("[generate.test] SKIP cargo check for rust dev mode: cargo not found (tree-validated only)");
    return;
  }
  const buildResult = spawnSync("cargo", ["check", "--workspace"], {
    cwd: targetDir,
    encoding: "utf8",
  });
  assert.equal(
    buildResult.status,
    0,
    `rust dev-mode generated project must build standalone: ${buildResult.stdout}\n${buildResult.stderr}`,
  );
});

test("generated ts registry-mode project guards SEKIBAN_NPM_MODE=tarball as unavailable standalone", () => {
  const dir = mkTempDir("csw-ts-guard-");
  const targetDir = path.join(dir, "out");
  const genResult = runCli(["--language", "ts", "--mode", "registry", "--dir", targetDir]);
  assert.equal(genResult.status, 0, `cli exited non-zero: ${genResult.stderr}`);

  const result = spawnSync("bash", ["scripts/build-wasm.sh"], {
    cwd: targetDir,
    encoding: "utf8",
    env: { ...process.env, SEKIBAN_NPM_MODE: "tarball" },
  });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /requires a full monorepo checkout/);
});

test("generated go registry-mode project guards --local-module as unavailable standalone", () => {
  const dir = mkTempDir("csw-go-guard-");
  const targetDir = path.join(dir, "out");
  const genResult = runCli(["--language", "go", "--mode", "registry", "--dir", targetDir]);
  assert.equal(genResult.status, 0, `cli exited non-zero: ${genResult.stderr}`);

  const result = spawnSync("bash", ["scripts/smoke.sh", "--local-module"], {
    cwd: targetDir,
    encoding: "utf8",
  });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /requires a full monorepo checkout/);
});

test("refuses to generate into a non-empty directory without --force", () => {
  const dir = mkTempDir("csw-nonempty-");
  const targetDir = path.join(dir, "out");
  fs.mkdirSync(targetDir, { recursive: true });
  fs.writeFileSync(path.join(targetDir, "existing.txt"), "hi");

  const result = runCli(["--language", "rust", "--dir", targetDir]);
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /non-empty directory/);

  const forced = runCli(["--language", "rust", "--dir", targetDir, "--force"]);
  assert.equal(forced.status, 0, `cli exited non-zero with --force: ${forced.stderr}`);
});
