//! Host-backed query port used by projectors during `apply_event` to read projected state.
//!
//! Calls the host import `env.mv_host_query_rows(sql_ptr, sql_len, params_ptr, params_len, row_limit)`
//! which returns a packed `(ptr << 32) | len` i64 pointing at a UTF-8 JSON `MvQueryResultDto` (or a
//! `{"error":"..."}` envelope on failure). The host allocates the buffer via this module's `alloc`
//! export; we free it with `dealloc` after reading.

use crate::dto::{MvParam, MvQueryResultDto, MvQueryRowDto};

#[link(wasm_import_module = "env")]
extern "C" {
    /// Host import. Signature mirrors the C# side declared in
    /// `WasmtimeMaterializedViewExecutor.HandleHostQueryRows`.
    #[link_name = "mv_host_query_rows"]
    fn mv_host_query_rows(
        sql_ptr: i32,
        sql_len: i32,
        params_ptr: i32,
        params_len: i32,
        row_limit: i32,
    ) -> i64;
}

pub trait MvQueryPort {
    fn query_rows(&self, sql: &str, params: &[MvParam]) -> Vec<MvQueryRowDto>;
    fn query_single_or_default_row(&self, sql: &str, params: &[MvParam]) -> Option<MvQueryRowDto>;
    fn execute_scalar_json(&self, sql: &str, params: &[MvParam]) -> Option<String>;
}

pub struct HostBackedMvQueryPort;

impl HostBackedMvQueryPort {
    pub const fn new() -> Self {
        Self
    }

    fn invoke(&self, sql: &str, params: &[MvParam], row_limit: i32) -> Result<MvQueryResultDto, String> {
        let params_json = if params.is_empty() {
            String::new()
        } else {
            serde_json::to_string(params).map_err(|e| format!("serialize params: {e}"))?
        };

        let sql_bytes = sql.as_bytes();
        let params_bytes = params_json.as_bytes();
        let sql_ptr = sql_bytes.as_ptr() as i32;
        let sql_len = sql_bytes.len() as i32;
        let params_ptr = if params_bytes.is_empty() {
            0
        } else {
            params_bytes.as_ptr() as i32
        };
        let params_len = params_bytes.len() as i32;

        let packed = unsafe { mv_host_query_rows(sql_ptr, sql_len, params_ptr, params_len, row_limit) };
        let (ptr, len) = unpack_ptr_len(packed);
        if ptr == 0 || len == 0 {
            return Ok(MvQueryResultDto::default());
        }

        // Host wrote the JSON into our linear memory via the `alloc` export. Read it, free it,
        // deserialize, and surface `{"error":"..."}` envelopes as Err so projectors can bail out.
        let bytes = unsafe { std::slice::from_raw_parts(ptr as *const u8, len as usize) };
        let text = match std::str::from_utf8(bytes) {
            Ok(s) => s.to_string(),
            Err(e) => {
                unsafe { crate::__dealloc_export(ptr, len) };
                return Err(format!("mv_host_query_rows returned non-UTF8: {e}"));
            }
        };
        unsafe { crate::__dealloc_export(ptr, len) };

        if let Ok(value) = serde_json::from_str::<serde_json::Value>(&text) {
            if let Some(err) = value.get("error").and_then(|v| v.as_str()) {
                return Err(format!("mv_host_query_rows error: {err}"));
            }
        }
        serde_json::from_str::<MvQueryResultDto>(&text).map_err(|e| format!("parse mv_host_query_rows: {e}"))
    }
}

impl MvQueryPort for HostBackedMvQueryPort {
    fn query_rows(&self, sql: &str, params: &[MvParam]) -> Vec<MvQueryRowDto> {
        self.invoke(sql, params, i32::MAX)
            .map(|r| r.rows)
            .unwrap_or_default()
    }

    fn query_single_or_default_row(&self, sql: &str, params: &[MvParam]) -> Option<MvQueryRowDto> {
        self.invoke(sql, params, 1)
            .ok()
            .and_then(|r| r.rows.into_iter().next())
    }

    fn execute_scalar_json(&self, sql: &str, params: &[MvParam]) -> Option<String> {
        let row = self.query_single_or_default_row(sql, params)?;
        if row.columns.len() != 1 {
            return None;
        }
        row.columns.into_values().next().flatten()
    }
}

#[inline]
fn unpack_ptr_len(packed: i64) -> (i32, i32) {
    ((packed >> 32) as i32, (packed & 0xFFFF_FFFF) as i32)
}
