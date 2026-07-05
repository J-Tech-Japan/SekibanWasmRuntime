# create-sekiban-wasm

`npx create-sekiban-wasm` scaffolds a per-language Sekiban WASM project from
the maintained monorepo sample shapes (Rust, TypeScript, Go, Swift,
MoonBit). It never fetches templates over the network -- everything it
generates ships inside the npm tarball.

## Usage

```bash
npx create-sekiban-wasm --language rust
npx create-sekiban-wasm --language ts --dir ./my-weather-app
npx create-sekiban-wasm --language all
```

Run with no `--language` in an interactive terminal and it prompts for one.

| Option | Default | Effect |
| --- | --- | --- |
| `--language <id>` | prompted | `rust`, `ts`, `go`, `swift`, `moonbit`, or `all`. |
| `--mode <mode>` | `registry` | `registry` (published-package consumer) or `dev` (not bundled in 0.1.0). |
| `--dir <path>` | `./<language>-sekiban-wasm` | Output directory (`./sekiban-wasm-all/<language>` for `--language all`). |
| `--force` | off | Allow generating into a non-empty directory. |

See [`docs/tools/create-sekiban-wasm.md`](../../../docs/tools/create-sekiban-wasm.md)
for per-language availability, the registry-vs-dev mode boundary, and the
runtime image tag convention.

## Development

```bash
npm install
npm run sync-templates   # copies + portabilizes src/samples/* into templates/
npm run build            # tsc -> dist/
npm test                 # node --test (generation + guard verification)
npm pack --dry-run
bash scripts/generate-smoke.sh
```

`templates/` and `dist/` are generated (gitignored), never committed;
`npm run sync-templates` and `npm run build` run automatically before `npm
test` and `npm pack` (via the `pretest`/`prepack` lifecycle scripts).

## License

Elastic License 2.0 -- see the packaged LICENSE file.
