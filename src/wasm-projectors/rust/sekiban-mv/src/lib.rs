//! Rust companion to the C# `Sekiban.Dcb.WasmRuntime.Host.MaterializedView` boundary contracts.
//!
//! This crate is a preview, repo-local release candidate. Its public boundary is the
//! materialized-view DTOs, [`WasmMvProjector`], [`MvParamBuilder`], [`MvQueryPort`], and
//! [`export_mv!`] macro used by Rust WASM modules that expose materialized views. Host
//! ABI details remain preview-stable only within this repository until crates.io
//! publication is explicitly approved.
//!
//! Provides:
//! * [`dto`] — serde DTOs byte-compatible with the host wire format.
//! * [`projector::WasmMvProjector`] — trait every MV projector implements.
//! * [`param_builder::MvParamBuilder`] — fluent builder mirroring the C# helper.
//! * [`query_port::HostBackedMvQueryPort`] — client for the `mv_host_query_rows` host import.
//! * [`export_mv!`] — macro that emits the three C-ABI exports a WASM module needs.
//!
//! The host side (`WasmtimeMaterializedViewExecutor`) calls into this module's exports through
//! Wasmtime. The Rust sample's WASM crate combines this with `sekiban_wasm::export_domain!` so one
//! `.wasm` binary serves both the primitive projection runtime AND materialized views.

pub mod dto;
pub mod export;
pub mod param_builder;
pub mod projector;
pub mod query_port;

pub use dto::*;
pub use param_builder::MvParamBuilder;
pub use projector::WasmMvProjector;
pub use query_port::{HostBackedMvQueryPort, MvQueryPort};

// Re-exported for the macro expansions so downstream crates don't have to add `serde_json`.
pub use serde_json;

/// Trampoline used by `query_port.rs` to free the buffer the host allocated through our `alloc`
/// export during a `mv_host_query_rows` callback. The Rust WASM crate exports `dealloc` via
/// `sekiban_wasm::export_domain!`; we borrow it here by calling it through a weakly-linked extern
/// block. In pure-Rust tests this symbol does not exist, so the function is a no-op there.
#[doc(hidden)]
#[inline]
pub unsafe fn __dealloc_export(ptr: i32, len: i32) {
    #[cfg(target_arch = "wasm32")]
    unsafe {
        extern "C" {
            fn dealloc(ptr: i32, len: i32);
        }
        dealloc(ptr, len);
    }
    #[cfg(not(target_arch = "wasm32"))]
    {
        let _ = (ptr, len);
    }
}
