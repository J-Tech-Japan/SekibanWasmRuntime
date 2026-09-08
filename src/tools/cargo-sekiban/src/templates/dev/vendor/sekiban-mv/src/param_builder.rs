//! Fluent builder that mirrors the C# `MvParamBuilder` so Rust projectors can write
//! `MvParamBuilder::new().guid("Id", id).string("Name", name).build()` just like C#.

use serde_json::{json, Value};
use uuid::Uuid;

use crate::dto::{MvParam, MvParamKind};

pub struct MvParamBuilder {
    params: Vec<MvParam>,
}

impl MvParamBuilder {
    pub fn new() -> Self {
        Self { params: Vec::new() }
    }

    pub fn build(self) -> Vec<MvParam> {
        self.params
    }

    pub fn null(mut self, name: &str) -> Self {
        self.params.push(MvParam { name: name.to_string(), kind: MvParamKind::Null, value_json: None });
        self
    }

    pub fn string(mut self, name: &str, value: impl Into<String>) -> Self {
        let raw = Value::String(value.into());
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::String,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn guid(mut self, name: &str, value: Uuid) -> Self {
        let raw = Value::String(value.to_string());
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::Guid,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn int32(mut self, name: &str, value: i32) -> Self {
        let raw = json!(value);
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::Int32,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn int64(mut self, name: &str, value: i64) -> Self {
        let raw = json!(value);
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::Int64,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn bool(mut self, name: &str, value: bool) -> Self {
        let raw = json!(value);
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::Boolean,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn decimal(mut self, name: &str, value: &str) -> Self {
        // Decimals ride as JSON strings to avoid f64 drift; the host-side bridge parses via Decimal.Parse.
        let raw = Value::String(value.to_string());
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::Decimal,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn double(mut self, name: &str, value: f64) -> Self {
        let raw = json!(value);
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::Double,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn datetime_offset(mut self, name: &str, iso8601: &str) -> Self {
        let raw = Value::String(iso8601.to_string());
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::DateTimeOffset,
            value_json: Some(raw.to_string()),
        });
        self
    }

    pub fn bytes_base64(mut self, name: &str, base64_encoded: &str) -> Self {
        let raw = Value::String(base64_encoded.to_string());
        self.params.push(MvParam {
            name: name.to_string(),
            kind: MvParamKind::Bytes,
            value_json: Some(raw.to_string()),
        });
        self
    }
}

impl Default for MvParamBuilder {
    fn default() -> Self {
        Self::new()
    }
}
