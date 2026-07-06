#!/usr/bin/env node
// Pack-time template bundling for create-sekiban-wasm.
//
// Copies each available language/mode's monorepo sample into templates/, and
// rewrites the small set of monorepo-relative path assumptions the sample
// scripts/AppHosts share (ROOT resolved 4 directories up to the monorepo
// root, a SAMPLE_DIR="src/samples/<Name>" prefix, and the AppHost's matching
// 4-level-up repoRoot) so the bundled copy is a standalone project rooted at
// itself instead of at a monorepo checkout. The CLI never fetches templates
// over the network -- this script is the only thing that touches
// src/samples/ and src/wasm-projectors/, and it only runs at pack/test time
// (npm lifecycle: pretest, prepack), never inside the generated project.
//
// rust/dev additionally vendors the monorepo-internal wasm-projectors/rust
// crates its reference sample (PublicContainer.RsDecider) depends on via
// local path, and rewrites those paths to point at the vendored copies, so
// the generated dev-mode project is fully self-contained.
//
// Every bundled shell script also gets a small standalone-mode guard
// injected right after its `cd "$ROOT"` line: several of the source samples
// have an opt-in monorepo-only pre-publish dry-run mode (TypeScript's
// SEKIBAN_NPM_MODE=tarball, Go's --local-module, Swift's --local-package,
// MoonBit's --local-packages) that references monorepo-only source and
// cannot work in a generated standalone project. Rather than leaving that
// mode to fail with a confusing missing-path error if an npx consumer tries
// it, the guard detects "not running inside a monorepo checkout" (no
// src/lib directory) and exits with a clear message before the mode's own
// logic runs.
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PKG_ROOT = path.resolve(__dirname, "..");
const TEMPLATES_ROOT = path.join(PKG_ROOT, "templates");

const EXCLUDE_DIRS = new Set([
  "node_modules",
  "bin",
  "obj",
  "target",
  "artifacts",
  "reports",
  "build",
  "dist",
  ".aspire",
  ".build",
  ".swiftpm",
  "_build",
  ".git",
]);

// Dev-time-only overlay files that let a sample build against monorepo-local
// source before its published tag/registry entry exists (e.g. Go's go.work
// workspace `replace` directive). These must never ship in a generated
// standalone project: outside the monorepo the path they point at does not
// exist, and the generated project should instead attempt real registry
// resolution (which fails cleanly with a not-found error until publish,
// exactly like every other not-yet-published language).
const EXCLUDE_FILES = new Set(["go.work", "go.work.sum", "package-lock.json", ".codegen-hash"]);

const STANDALONE_MODE_GUARD = [
  "",
  "# create-sekiban-wasm: this generated project does not bundle src/lib (the",
  "# monorepo-only SDK source tree), so any monorepo-local pre-publish dry-run",
  "# mode (--local-module / --local-package / --local-packages /",
  "# SEKIBAN_NPM_MODE=tarball) cannot work here. Fail with a clear message",
  "# instead of a confusing missing-path error; the default registry/",
  "# published-package path is unaffected.",
  'if [[ ! -d "$ROOT/src/lib" ]]; then',
  '  for __csw_arg in "$@"; do',
  "    case \"$__csw_arg\" in",
  "      --local-module|--local-package|--local-packages)",
  '        echo "[$(basename "$0" .sh)] \'$__csw_arg\' requires a full monorepo checkout (this generated project does not bundle src/lib). Omit the flag to use the default registry/published-package path." >&2',
  "        exit 1",
  "        ;;",
  "    esac",
  "  done",
  '  if [[ "${SEKIBAN_NPM_MODE:-registry}" == "tarball" ]]; then',
  '    echo "[$(basename "$0" .sh)] SEKIBAN_NPM_MODE=tarball requires a full monorepo checkout (this generated project does not bundle src/lib). Unset SEKIBAN_NPM_MODE (or set it to registry) to use the default path." >&2',
  "    exit 1",
  "  fi",
  "fi",
  "",
].join("\n");

async function copyDir(src, dest) {
  await fs.mkdir(dest, { recursive: true });
  const entries = await fs.readdir(src, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (EXCLUDE_DIRS.has(entry.name)) continue;
      await copyDir(path.join(src, entry.name), path.join(dest, entry.name));
      continue;
    }
    if (EXCLUDE_FILES.has(entry.name)) continue;
    if (entry.isSymbolicLink()) continue; // none expected in these sample trees
    await fs.copyFile(path.join(src, entry.name), path.join(dest, entry.name));
  }
}

function portabilizeShellScript(text, sampleDirName) {
  let result = text
    .replace(
      /ROOT="\$\(cd "\$\(dirname "\$\{BASH_SOURCE\[0\]\}"\)\/\.\.\/\.\.\/\.\.\/\.\." && pwd\)"/g,
      'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"',
    )
    .replace(/^SAMPLE_DIR="src\/samples\/[^"]+"$/m, 'SAMPLE_DIR="."')
    .split(`src/samples/${sampleDirName}/`)
    .join("");

  if (/^cd "\$ROOT"$/m.test(result)) {
    result = result.replace(/^cd "\$ROOT"$/m, `cd "$ROOT"\n${STANDALONE_MODE_GUARD}`);
  }
  return result;
}

function portabilizeProgramCs(text, sampleDirName) {
  return text
    .replace(
      /Path\.Combine\(builder\.AppHostDirectory, "\.\.", "\.\.", "\.\.", "\.\."\)/g,
      'Path.Combine(builder.AppHostDirectory, "..")',
    )
    .split(`src/samples/${sampleDirName}/`)
    .join("");
}

function portabilizeReadme(text, sampleDirName) {
  return text.split(`src/samples/${sampleDirName}/`).join("");
}

// Language-specific README content fixes for text that survives the generic
// sampleDirName strip: cross-references to a *different* monorepo sample by
// path, and explanations of a monorepo-only mode (like TS's tarball
// packing) that no longer apply once that mode is guarded off standalone
// (see STANDALONE_MODE_GUARD above).
function applyReadmeContentFixes(text, language, mode) {
  if (language === "ts" && mode === "registry") {
    return text
      .replace(
        "(`src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider`, SWR-G056)",
        "(the crates.io Rust sample, SWR-G056)",
      )
      .replace(
        "- `tarball` (works today): packs `@sekiban/as-wasm` and `@sekiban/ts` from\n" +
          "  `src/lib/sekiban-as-wasm` / `src/lib/sekiban-ts` with `npm pack`, and\n" +
          "  installs each from its packed tarball in a scratch build directory. The\n" +
          "  committed `package.json` files are never rewritten; the tarball path is\n" +
          "  substituted only in the scratch copy, with a guard asserting the installed\n" +
          "  package actually resolved from the `.tgz` (never `src/lib`).",
        "- `tarball`: packs `@sekiban/as-wasm` and `@sekiban/ts` from a monorepo\n" +
          "  checkout (`src/lib/sekiban-as-wasm` / `src/lib/sekiban-ts`) with `npm pack`.\n" +
          "  **Not available in a project generated by `create-sekiban-wasm`** -- there is\n" +
          "  no monorepo checkout to pack from here, and `scripts/build-wasm.sh`/\n" +
          "  `scripts/smoke.sh` detect that and fail with a clear message rather than a\n" +
          "  confusing missing-path error.",
      );
  }
  return text;
}

async function walkAndPortabilize(dir, sampleDirName, language, mode) {
  const entries = await fs.readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    const entryPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      await walkAndPortabilize(entryPath, sampleDirName, language, mode);
      continue;
    }
    if (entry.name.endsWith(".sh")) {
      const text = await fs.readFile(entryPath, "utf8");
      await fs.writeFile(entryPath, portabilizeShellScript(text, sampleDirName));
    } else if (entry.name === "Program.cs") {
      const text = await fs.readFile(entryPath, "utf8");
      await fs.writeFile(entryPath, portabilizeProgramCs(text, sampleDirName));
    } else if (entry.name === "README.md") {
      const text = await fs.readFile(entryPath, "utf8");
      const portabilized = portabilizeReadme(text, sampleDirName);
      await fs.writeFile(entryPath, applyReadmeContentFixes(portabilized, language, mode));
    }
  }
}

// rust/dev only: vendor the monorepo-internal wasm-projectors/rust crates
// the PublicContainer.RsDecider sample depends on via local path, and
// rewrite Domain/Wasm/Client Cargo.toml references to point at the vendored
// copies. The vendored crates' own inter-crate path deps are all simple
// `../<crate>` siblings, so preserving that same sibling layout under
// vendor/ means those internal references need no rewriting at all.
async function vendorRustDevCrates(destDir, info) {
  const vendorDir = path.join(destDir, "vendor");
  for (const crate of info.vendorCrates) {
    const srcDir = path.resolve(PKG_ROOT, info.vendorSourceDir, crate);
    if (!existsSync(srcDir)) {
      throw new Error(`sync-templates: vendor crate source not found: ${srcDir}`);
    }
    await copyDir(srcDir, path.join(vendorDir, crate));
  }

  for (const projectDir of ["Wasm", "Client"]) {
    const cargoTomlPath = path.join(destDir, projectDir, "Cargo.toml");
    if (!existsSync(cargoTomlPath)) continue;
    const text = await fs.readFile(cargoTomlPath, "utf8");
    const rewritten = text.replace(
      /path = "\.\.\/\.\.\/\.\.\/wasm-projectors\/rust\/([A-Za-z0-9_-]+)"/g,
      'path = "../vendor/$1"',
    );
    await fs.writeFile(cargoTomlPath, rewritten);
  }

  const rootCargoTomlPath = path.join(destDir, "Cargo.toml");
  if (existsSync(rootCargoTomlPath)) {
    const text = await fs.readFile(rootCargoTomlPath, "utf8");
    const vendorMembers = info.vendorCrates.map((crate) => `    "vendor/${crate}",`).join("\n");
    let rewritten = text.replace(
      /members = \[\n([\s\S]*?)\]/,
      (match, existing) => `members = [\n${existing}${vendorMembers}\n]`,
    );

    // The publishable vendored crates (sekiban-core/derive/mv/wasm/executor)
    // declare authors/edition/license/homepage/repository/keywords as
    // `<field>.workspace = true`, inherited from the source workspace's
    // [workspace.package]. The generated project's own root Cargo.toml has
    // no such section, so copy it in verbatim -- otherwise cargo fails with
    // "workspace.package.edition was not defined".
    const vendorWorkspaceCargoToml = path.resolve(PKG_ROOT, info.vendorSourceDir, "Cargo.toml");
    const vendorWorkspaceText = await fs.readFile(vendorWorkspaceCargoToml, "utf8");
    const workspacePackageMatch = vendorWorkspaceText.match(/\[workspace\.package\][\s\S]*?(?=\n\[|$)/);
    if (workspacePackageMatch) {
      rewritten = `${rewritten.trimEnd()}\n\n${workspacePackageMatch[0].trimEnd()}\n`;
    }

    await fs.writeFile(rootCargoTomlPath, rewritten);
  }
}

async function assertNoResidue(dir) {
  const bad = [];
  async function walk(d) {
    const entries = await fs.readdir(d, { withFileTypes: true });
    for (const entry of entries) {
      const entryPath = path.join(d, entry.name);
      if (entry.isDirectory()) {
        await walk(entryPath);
        continue;
      }
      if (!/\.(sh|cs|md)$/.test(entry.name)) continue;
      const text = await fs.readFile(entryPath, "utf8");
      if (text.includes("../../../..") || /src\/samples\//.test(text)) {
        bad.push(entryPath);
      }
    }
  }
  await walk(dir);
  if (bad.length > 0) {
    throw new Error(
      "sync-templates: monorepo-relative path residue found in: " + bad.join(", "),
    );
  }
}

async function main() {
  const samplesConfigPath = path.join(PKG_ROOT, "samples.json");
  const samples = JSON.parse(await fs.readFile(samplesConfigPath, "utf8"));

  await fs.rm(TEMPLATES_ROOT, { recursive: true, force: true });

  for (const [language, modes] of Object.entries(samples)) {
    for (const mode of ["registry", "dev"]) {
      const info = modes[mode];
      if (!info || !info.available) continue;
      const srcDir = path.resolve(PKG_ROOT, info.sourceDir);
      if (!existsSync(srcDir)) {
        throw new Error(`sync-templates: source sample not found for ${language}/${mode}: ${srcDir}`);
      }
      const destDir = path.join(TEMPLATES_ROOT, language, mode);
      console.log(`[sync-templates] ${language}/${mode} <- ${path.relative(PKG_ROOT, srcDir)}`);
      await copyDir(srcDir, destDir);
      await walkAndPortabilize(destDir, info.sampleDirName, language, mode);

      if (info.vendorCrates) {
        console.log(`[sync-templates] ${language}/${mode} vendoring ${info.vendorCrates.join(", ")}`);
        await vendorRustDevCrates(destDir, info);
      }
    }
  }

  await assertNoResidue(TEMPLATES_ROOT);
  console.log("[sync-templates] OK");
}

main().catch((err) => {
  console.error(err.stack || String(err));
  process.exit(1);
});
