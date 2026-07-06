#!/usr/bin/env node
import fs from "node:fs/promises";
import path from "node:path";
import { createInterface } from "node:readline/promises";
import {
  isKnownLanguage,
  LANGUAGE_IDS,
  loadLanguages,
  type LanguageId,
  type Mode,
} from "./languages.js";
import { generateProject } from "./copy-project.js";

const HELP = `create-sekiban-wasm - scaffold a Sekiban WASM project

Usage:
  npx create-sekiban-wasm --language <rust|ts|go|swift|moonbit|all> [options]

Options:
  --language <id>   Language to scaffold. One of: ${LANGUAGE_IDS.join("|")}|all.
                     Prompted interactively when omitted (if attached to a
                     terminal); required otherwise.
  --mode <mode>     registry (default) or dev. registry generates an
                     external-consumer sample that depends on the published
                     Sekiban package for that language only; dev is not
                     bundled in 0.1.0 (see docs/tools/create-sekiban-wasm.md).
  --dir <path>      Output directory. Defaults to ./<language>-sekiban-wasm
                     (./sekiban-wasm-all for --language all, with one
                     subdirectory per language).
  --force           Allow generating into a non-empty directory.
  --help            Show this help text.

Examples:
  npx create-sekiban-wasm --language rust
  npx create-sekiban-wasm --language ts --dir ./my-weather-app
  npx create-sekiban-wasm --language all
`;

interface ParsedArgs {
  language?: string;
  mode: Mode;
  dir?: string;
  force: boolean;
  help: boolean;
}

function parseArgs(argv: string[]): ParsedArgs {
  const result: ParsedArgs = { mode: "registry", force: false, help: false };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    switch (arg) {
      case "--language":
      case "-l":
        result.language = argv[++i];
        break;
      case "--mode":
      case "-m":
        result.mode = argv[++i] as Mode;
        break;
      case "--dir":
      case "-d":
        result.dir = argv[++i];
        break;
      case "--force":
        result.force = true;
        break;
      case "--help":
      case "-h":
        result.help = true;
        break;
      default:
        console.error(`Unknown argument: ${arg}`);
        console.error(HELP);
        process.exit(1);
    }
  }
  return result;
}

async function promptForLanguage(): Promise<string> {
  if (!process.stdin.isTTY) {
    console.error("--language is required (not attached to a terminal, cannot prompt).");
    console.error(HELP);
    process.exit(1);
  }
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  try {
    console.log(`Available languages: ${LANGUAGE_IDS.join(", ")}, all`);
    const answer = await rl.question("Which language would you like to scaffold? ");
    return answer.trim();
  } finally {
    rl.close();
  }
}

async function dirIsEmpty(dir: string): Promise<boolean> {
  try {
    const entries = await fs.readdir(dir);
    return entries.length === 0;
  } catch {
    return true; // doesn't exist yet
  }
}

async function generateOne(
  language: LanguageId,
  mode: Mode,
  targetDir: string,
  force: boolean,
): Promise<boolean> {
  const languages = loadLanguages();
  const info = languages[language];
  const modeInfo = mode === "registry" ? info.registry : info.dev;

  if (!modeInfo.available) {
    console.error(
      `[${language}] ${mode} mode is not available: ${modeInfo.reason ?? "not yet available"}`,
    );
    return false;
  }

  if (!force && !(await dirIsEmpty(targetDir))) {
    console.error(`[${language}] refusing to generate into non-empty directory: ${targetDir} (use --force)`);
    return false;
  }

  await generateProject(language, mode, info, targetDir);
  console.log(`[${language}] generated at ${targetDir}`);
  return true;
}

async function main(): Promise<void> {
  const argv = parseArgs(process.argv.slice(2));

  if (argv.help) {
    console.log(HELP);
    return;
  }

  if (argv.mode !== "registry" && argv.mode !== "dev") {
    console.error(`Unknown --mode value: ${argv.mode} (expected registry or dev)`);
    process.exit(1);
  }

  let languageArg = argv.language;
  if (!languageArg) {
    languageArg = await promptForLanguage();
  }

  if (languageArg !== "all" && !isKnownLanguage(languageArg)) {
    console.error(
      `Unknown --language value: '${languageArg}' (expected one of ${LANGUAGE_IDS.join(", ")}, all)`,
    );
    process.exit(1);
  }

  if (languageArg === "all") {
    const baseDir = path.resolve(argv.dir ?? "./sekiban-wasm-all");
    let anySucceeded = false;
    for (const language of LANGUAGE_IDS) {
      const targetDir = path.join(baseDir, language);
      const ok = await generateOne(language, argv.mode, targetDir, argv.force);
      anySucceeded = anySucceeded || ok;
    }
    if (!anySucceeded) {
      console.error(`No language could be generated in ${argv.mode} mode.`);
      process.exit(1);
    }
    return;
  }

  const language = languageArg as LanguageId;
  const targetDir = path.resolve(argv.dir ?? `./${language}-sekiban-wasm`);
  const ok = await generateOne(language, argv.mode, targetDir, argv.force);
  if (!ok) {
    process.exit(1);
  }
}

main().catch((err) => {
  console.error(err instanceof Error ? (err.stack ?? err.message) : String(err));
  process.exit(1);
});
