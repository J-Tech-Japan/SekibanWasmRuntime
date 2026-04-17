//! Macro that emits the three C-ABI materialized view exports (`mv_metadata` / `mv_initialize` /
//! `mv_apply_event`).
//!
//! This macro does **not** emit the `alloc` / `dealloc` / `memory` exports that the host relies
//! on when writing host-import response buffers into linear memory (see
//! [`crate::query_port::HostBackedMvQueryPort`]). Those come from `sekiban_wasm::export_domain!`
//! in the same crate. If you ship an MV-only WASM module (no primitive projections), add those
//! exports manually — the simplest way is still to call `export_domain!` with a dummy domain,
//! or to copy the `#[no_mangle] pub extern "C" fn alloc` / `dealloc` pair from
//! `sekiban-wasm/src/exports.rs`.
//!
//! Usage (in a `cdylib` crate that already calls `sekiban_wasm::export_domain!`):
//! ```ignore
//! sekiban_mv::export_mv!(vec![
//!     std::sync::Arc::new(MyProjectorV1) as std::sync::Arc<dyn sekiban_mv::WasmMvProjector>,
//!     std::sync::Arc::new(AnotherProjectorV2) as std::sync::Arc<dyn sekiban_mv::WasmMvProjector>,
//! ]);
//! ```
//!
//! The macro dispatches on `(view_name, view_version)` at call time so a single WASM module can
//! ship multiple materialized views — same story as the C# `WasmMvRegistry`.

#[macro_export]
macro_rules! export_mv {
    ($projectors:expr) => {
        // Registry is built lazily so static initializers don't run until first use. Using
        // OnceCell via std::sync::OnceLock keeps us off crate deps.
        fn __sekiban_mv_registry() -> &'static ::std::collections::HashMap<
            (String, i32),
            ::std::sync::Arc<dyn $crate::projector::WasmMvProjector>,
        > {
            use ::std::sync::OnceLock;
            static CELL: OnceLock<
                ::std::collections::HashMap<
                    (String, i32),
                    ::std::sync::Arc<dyn $crate::projector::WasmMvProjector>,
                >,
            > = OnceLock::new();
            CELL.get_or_init(|| {
                let projectors: ::std::vec::Vec<
                    ::std::sync::Arc<dyn $crate::projector::WasmMvProjector>,
                > = $projectors;
                let mut map = ::std::collections::HashMap::new();
                for p in projectors {
                    map.insert((p.view_name().to_string(), p.view_version()), p);
                }
                map
            })
        }

        #[no_mangle]
        pub extern "C" fn mv_metadata() -> i64 {
            let metadata: ::std::vec::Vec<$crate::dto::WasmMvMetadata> = __sekiban_mv_registry()
                .values()
                .map(|p| $crate::dto::WasmMvMetadata {
                    view_name: p.view_name().to_string(),
                    view_version: p.view_version(),
                    logical_tables: p.logical_tables().iter().map(|s| s.to_string()).collect(),
                })
                .collect();
            let json = match ::serde_json::to_string(&metadata) {
                Ok(s) => s,
                Err(e) => $crate::export::__error_json(&format!("serialize metadata: {e}")),
            };
            $crate::export::__write_string_to_memory(&json)
        }

        #[no_mangle]
        pub extern "C" fn mv_initialize(
            view_name_ptr: i32,
            view_name_len: i32,
            view_version: i32,
            bindings_ptr: i32,
            bindings_len: i32,
        ) -> i64 {
            let view_name = unsafe { $crate::export::__read_string(view_name_ptr, view_name_len) };
            let bindings_json = unsafe { $crate::export::__read_string(bindings_ptr, bindings_len) };
            let result = (|| -> ::std::result::Result<String, String> {
                let bindings: $crate::dto::MvTableBindingsDto = ::serde_json::from_str(&bindings_json)
                    .map_err(|e| format!("parse bindings: {e}"))?;
                let projector = __sekiban_mv_registry()
                    .get(&(view_name.clone(), view_version))
                    .ok_or_else(|| format!("unknown view {view_name}/{view_version}"))?;
                let statements = projector.initialize(&bindings);
                let batch = $crate::dto::MvStatementBatchDto { statements };
                ::serde_json::to_string(&batch).map_err(|e| format!("serialize batch: {e}"))
            })();
            match result {
                Ok(json) => $crate::export::__write_string_to_memory(&json),
                Err(e) => $crate::export::__write_string_to_memory(&$crate::export::__error_json(&e)),
            }
        }

        #[no_mangle]
        pub extern "C" fn mv_apply_event(
            view_name_ptr: i32,
            view_name_len: i32,
            view_version: i32,
            bindings_ptr: i32,
            bindings_len: i32,
            event_ptr: i32,
            event_len: i32,
        ) -> i64 {
            let view_name = unsafe { $crate::export::__read_string(view_name_ptr, view_name_len) };
            let bindings_json = unsafe { $crate::export::__read_string(bindings_ptr, bindings_len) };
            let event_json = unsafe { $crate::export::__read_string(event_ptr, event_len) };
            let result = (|| -> ::std::result::Result<String, String> {
                let bindings: $crate::dto::MvTableBindingsDto = ::serde_json::from_str(&bindings_json)
                    .map_err(|e| format!("parse bindings: {e}"))?;
                let event: $crate::dto::MvSerializableEventDto = ::serde_json::from_str(&event_json)
                    .map_err(|e| format!("parse event: {e}"))?;
                let projector = __sekiban_mv_registry()
                    .get(&(view_name.clone(), view_version))
                    .ok_or_else(|| format!("unknown view {view_name}/{view_version}"))?;
                let port = $crate::query_port::HostBackedMvQueryPort::new();
                let statements = projector.apply_event(&bindings, &event, &port);
                let batch = $crate::dto::MvStatementBatchDto { statements };
                ::serde_json::to_string(&batch).map_err(|e| format!("serialize batch: {e}"))
            })();
            match result {
                Ok(json) => $crate::export::__write_string_to_memory(&json),
                Err(e) => $crate::export::__write_string_to_memory(&$crate::export::__error_json(&e)),
            }
        }
    };
}

// -----------------------------------------------------------------------------
// Internal helpers invoked by the macro expansion. Marked as `__` to discourage direct use.
// -----------------------------------------------------------------------------

/// Read a UTF-8 string from linear memory. Safe as long as the host wrote valid UTF-8.
///
/// # Safety
/// `ptr` and `len` must describe valid, initialized, aligned UTF-8 bytes.
pub unsafe fn __read_string(ptr: i32, len: i32) -> String {
    if ptr == 0 || len <= 0 {
        return String::new();
    }
    let bytes = std::slice::from_raw_parts(ptr as *const u8, len as usize);
    std::str::from_utf8(bytes).map(|s| s.to_string()).unwrap_or_default()
}

/// Write a string into linear memory via `alloc` and return the packed `(ptr<<32) | len`.
pub fn __write_string_to_memory(s: &str) -> i64 {
    use std::alloc::{alloc, Layout};
    let bytes = s.as_bytes();
    let len = bytes.len();
    if len == 0 {
        return 0;
    }
    let layout = Layout::from_size_align(len, 1).expect("invalid layout");
    let ptr = unsafe { alloc(layout) };
    if ptr.is_null() {
        return 0;
    }
    unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), ptr, len) };
    ((ptr as i64) << 32) | (len as i64 & 0xFFFF_FFFF)
}

pub fn __error_json(message: &str) -> String {
    ::serde_json::json!({ "error": message }).to_string()
}
