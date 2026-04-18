//! Wire DTOs crossing the WASM <-> host boundary for the materialized view runtime.
//!
//! Shape is intentionally byte-for-byte aligned with the C# DTOs declared in
//! `Sekiban.Dcb.WasmRuntime.Host.MaterializedView.WasmMvBoundaryContracts` so the host can
//! deserialize Rust-produced JSON without a separate Rust-specific type. Field casing is camelCase
//! to match the host's `JsonSerializerDefaults.Web` (= camelCase) options.

use serde::{Deserialize, Serialize};

/// Kinds of scalar parameter values supported across the WASM boundary.
///
/// Numeric values MUST match `WasmMvParamKind` on the host side (0..9) so we can serialize the
/// discriminant as an integer and have Sekiban's Dapper bridge interpret it correctly. Serde's
/// default for unit-variant enums is to emit the variant name (e.g. `"String"`), so we round-trip
/// through `u8` explicitly to get `{"kind": 1, ...}` on the wire.
#[repr(u8)]
#[derive(Copy, Clone, Debug, PartialEq, Eq)]
pub enum MvParamKind {
    Null = 0,
    String = 1,
    Int32 = 2,
    Int64 = 3,
    Boolean = 4,
    Guid = 5,
    DateTimeOffset = 6,
    Decimal = 7,
    Double = 8,
    Bytes = 9,
}

impl serde::Serialize for MvParamKind {
    fn serialize<S: serde::Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_u8(*self as u8)
    }
}

impl<'de> serde::Deserialize<'de> for MvParamKind {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let value = u8::deserialize(deserializer)?;
        Ok(match value {
            0 => Self::Null,
            1 => Self::String,
            2 => Self::Int32,
            3 => Self::Int64,
            4 => Self::Boolean,
            5 => Self::Guid,
            6 => Self::DateTimeOffset,
            7 => Self::Decimal,
            8 => Self::Double,
            9 => Self::Bytes,
            other => return Err(serde::de::Error::custom(format!("unknown MvParamKind: {other}"))),
        })
    }
}

/// Named SQL parameter. `value_json` is a raw JSON token whose type depends on `kind`.
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MvParam {
    pub name: String,
    pub kind: MvParamKind,
    /// Raw JSON token for the value. `None` iff `kind == Null`.
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub value_json: Option<String>,
}

/// A SQL statement produced by a projector, ready for the host to run inside the apply tx.
#[derive(Clone, Debug, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MvSqlStatementDto {
    pub sql: String,
    #[serde(default)]
    pub parameters: Vec<MvParam>,
}

/// Response envelope returned by `mv_initialize` / `mv_apply_event`.
#[derive(Clone, Debug, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MvStatementBatchDto {
    #[serde(default)]
    pub statements: Vec<MvSqlStatementDto>,
}

/// Logical → physical table mapping passed by the host to every `mv_*` call.
#[derive(Clone, Debug, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MvTableBindingsDto {
    #[serde(default)]
    pub bindings: Vec<MvTableBindingEntry>,
}

impl MvTableBindingsDto {
    pub fn get_physical_name(&self, logical: &str) -> String {
        self.bindings
            .iter()
            .find(|b| b.logical == logical)
            .map(|b| b.physical.clone())
            .unwrap_or_else(|| {
                // Match C# behaviour: missing binding is a bug in the host manifest; surfacing it
                // as a panic-like string makes the SQL fail fast with a debuggable message.
                format!("__missing_binding_{}__", logical)
            })
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MvTableBindingEntry {
    pub logical: String,
    pub physical: String,
}

/// Projector metadata returned by `mv_metadata` so the host can enumerate what views the module
/// owns at startup (currently not called by the MV runtime, but kept for symmetry with C#).
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WasmMvMetadata {
    pub view_name: String,
    pub view_version: i32,
    pub logical_tables: Vec<String>,
}

/// Host-provided event payload that `mv_apply_event` receives.
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MvSerializableEventDto {
    pub event_type: String,
    pub payload_json: String,
    pub sortable_unique_id: String,
    #[serde(default)]
    pub tags: Vec<String>,
}

/// Single row returned by a host-import query. Columns are stringified JSON so projectors can
/// cast as needed (MvQueryRowReader on the host side expects string-or-null per column).
#[derive(Clone, Debug, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MvQueryRowDto {
    #[serde(default)]
    pub columns: std::collections::BTreeMap<String, Option<String>>,
}

#[derive(Clone, Debug, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct MvQueryResultDto {
    #[serde(default)]
    pub rows: Vec<MvQueryRowDto>,
}
