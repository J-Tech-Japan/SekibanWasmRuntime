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
// src/samples/, and it only runs at pack/test time (npm lifecycle: pretest,
// prepack), never inside the generated project.
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
  return text
    .replace(
      /ROOT="\$\(cd "\$\(dirname "\$\{BASH_SOURCE\[0\]\}"\)\/\.\.\/\.\.\/\.\.\/\.\." && pwd\)"/g,
      'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"',
    )
    .replace(/^SAMPLE_DIR="src\/samples\/[^"]+"$/m, 'SAMPLE_DIR="."')
    .split(`src/samples/${sampleDirName}/`)
    .join("");
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

async function walkAndPortabilize(dir, sampleDirName) {
  const entries = await fs.readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    const entryPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      await walkAndPortabilize(entryPath, sampleDirName);
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
      await fs.writeFile(entryPath, portabilizeReadme(text, sampleDirName));
    }
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
      if (!/\.(sh|cs)$/.test(entry.name)) continue;
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
      await walkAndPortabilize(destDir, info.sampleDirName);
    }
  }

  await assertNoResidue(TEMPLATES_ROOT);
  console.log("[sync-templates] OK");
}

main().catch((err) => {
  console.error(err.stack || String(err));
  process.exit(1);
});
