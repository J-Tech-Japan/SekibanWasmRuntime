package wasm

import "encoding/json"

// MustJSON marshals a value to JSON string, returning "{}" on error.
func MustJSON(value any) string {
	data, err := json.Marshal(value)
	if err != nil {
		return "{}"
	}
	return string(data)
}
