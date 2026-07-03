module sekiban-wasm-runtime-gomodule-godecider

go 1.22

// External-consumer proof: this sample requires the published Go subdirectory
// module exactly as an external user would. It must never gain a replace
// directive or a local Sekiban path (scripts/verify-no-local-sekiban-paths.sh
// guards this). Pre-publish dry-runs use the repo-committed go.work overlay
// instead; the published-module smoke runs with GOWORK=off.
//
// NOTE: do not run plain `go mod tidy` before the src/lib/sekiban-go/v0.1.0
// tag exists — it drops the unresolvable require below. After the tag is
// published, run `go mod tidy` once (GOWORK=off) and commit the go.sum update.
require (
	github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go v0.1.0
	github.com/google/uuid v1.6.0
)
