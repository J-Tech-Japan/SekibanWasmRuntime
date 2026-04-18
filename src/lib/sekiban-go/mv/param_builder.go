package mv

import (
	"encoding/json"
	"fmt"
	"strings"
)

// ParamBuilder mirrors the Rust `MvParamBuilder` / C# `MvParamBuilder` so Go projectors can
// write `mv.NewParams().Guid("Id", id).String("Name", name).Int32("Max", max).Build()`.
// Each typed method encodes the value as a JSON token and stores the literal JSON on the
// parameter; the host-side parameter bridge parses that token according to the declared Kind.
type ParamBuilder struct {
	params []MvParam
}

func NewParams() *ParamBuilder {
	return &ParamBuilder{}
}

func (b *ParamBuilder) Build() []MvParam {
	return b.params
}

func (b *ParamBuilder) Null(name string) *ParamBuilder {
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindNull, ValueJSON: nil})
	return b
}

func (b *ParamBuilder) String(name, value string) *ParamBuilder {
	literal := encodeJSONString(value)
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindString, ValueJSON: &literal})
	return b
}

// Guid emits the lowercase 8-4-4-4-12 string form, matching the canonical Postgres uuid
// representation. Callers should pass an already-hyphenated UUID string.
func (b *ParamBuilder) Guid(name, value string) *ParamBuilder {
	literal := encodeJSONString(strings.ToLower(value))
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindGuid, ValueJSON: &literal})
	return b
}

func (b *ParamBuilder) Int32(name string, value int32) *ParamBuilder {
	literal := fmt.Sprintf("%d", value)
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindInt32, ValueJSON: &literal})
	return b
}

func (b *ParamBuilder) Int64(name string, value int64) *ParamBuilder {
	literal := fmt.Sprintf("%d", value)
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindInt64, ValueJSON: &literal})
	return b
}

func (b *ParamBuilder) Bool(name string, value bool) *ParamBuilder {
	literal := "false"
	if value {
		literal = "true"
	}
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindBoolean, ValueJSON: &literal})
	return b
}

// Decimal rides as a JSON string to avoid float drift; host parses via System.Decimal.Parse.
func (b *ParamBuilder) Decimal(name, value string) *ParamBuilder {
	literal := encodeJSONString(value)
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindDecimal, ValueJSON: &literal})
	return b
}

func (b *ParamBuilder) Double(name string, value float64) *ParamBuilder {
	literal := fmt.Sprintf("%g", value)
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindDouble, ValueJSON: &literal})
	return b
}

func (b *ParamBuilder) DateTimeOffset(name, iso8601 string) *ParamBuilder {
	literal := encodeJSONString(iso8601)
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindDateTimeOffset, ValueJSON: &literal})
	return b
}

func (b *ParamBuilder) BytesBase64(name, base64 string) *ParamBuilder {
	literal := encodeJSONString(base64)
	b.params = append(b.params, MvParam{Name: name, Kind: ParamKindBytes, ValueJSON: &literal})
	return b
}

// encodeJSONString emits a Go string literal as a JSON string with correct escaping. Uses
// encoding/json so we don't hand-roll unicode escapes.
func encodeJSONString(s string) string {
	data, err := json.Marshal(s)
	if err != nil {
		return "\"\""
	}
	return string(data)
}
