module sekiban-dcb-decider-go-clientapi

go 1.22

require (
	github.com/J-Tech-Japan/sekiban-go v0.0.0
	github.com/go-chi/chi/v5 v5.0.12
	github.com/google/uuid v1.6.0
	sekiban-dcb-decider-go v0.0.0
)

replace (
	github.com/J-Tech-Japan/sekiban-go => ../../../lib/sekiban-go
	sekiban-dcb-decider-go => ../go-wasm
)
