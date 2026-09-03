import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "../..");
const fixturePath = resolve(
  repositoryRoot,
  "src/wasm-projectors/typescript/fixtures/domain-pin.json",
);
const domainPath = resolve(
  repositoryRoot,
  "src/wasm-projectors/typescript/src/domain.ts",
);

const fixture = JSON.parse(await readFile(fixturePath, "utf8"));
const bytes = await readFile(domainPath);
const sha256 = createHash("sha256").update(bytes).digest("hex");

if (bytes.length !== fixture.bytes || sha256 !== fixture.sha256) {
  throw new Error(
    [
      "Pinned upstream domain drift detected.",
      `path=${domainPath}`,
      `expectedBytes=${fixture.bytes}`,
      `actualBytes=${bytes.length}`,
      `expectedSha256=${fixture.sha256}`,
      `actualSha256=${sha256}`,
      `upstream=${fixture.repository}@${fixture.commit}:${fixture.domainPath}`,
      `gitBlob=${fixture.gitBlob}`,
    ].join("\n"),
  );
}

console.log(
  JSON.stringify({
    status: "ok",
    repository: fixture.repository,
    commit: fixture.commit,
    domainPath: fixture.domainPath,
    gitBlob: fixture.gitBlob,
    bytes: bytes.length,
    sha256,
  }),
);
