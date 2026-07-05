import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
// Compiled to dist/languages.js; the package root is one level up.
export const PKG_ROOT = path.resolve(__dirname, "..");

export type Mode = "registry" | "dev";

export interface ModeInfo {
  available: boolean;
  sourceDir?: string;
  sampleDirName?: string;
  note?: string;
  reason?: string;
}

export interface LanguageInfo {
  displayName: string;
  registry: ModeInfo;
  dev: ModeInfo;
}

export type LanguageId = "rust" | "ts" | "go" | "swift" | "moonbit";

export const LANGUAGE_IDS: LanguageId[] = ["rust", "ts", "go", "swift", "moonbit"];

let cache: Record<LanguageId, LanguageInfo> | null = null;

export function loadLanguages(): Record<LanguageId, LanguageInfo> {
  if (cache) return cache;
  const raw = fs.readFileSync(path.join(PKG_ROOT, "samples.json"), "utf8");
  cache = JSON.parse(raw) as Record<LanguageId, LanguageInfo>;
  return cache;
}

export function templateDir(language: LanguageId, mode: Mode): string {
  return path.join(PKG_ROOT, "templates", language, mode);
}

export function isKnownLanguage(value: string): value is LanguageId {
  return (LANGUAGE_IDS as string[]).includes(value);
}
