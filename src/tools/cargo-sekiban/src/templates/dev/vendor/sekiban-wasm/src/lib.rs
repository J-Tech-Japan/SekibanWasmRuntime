//! WASM export boundary helpers for Sekiban Rust projection modules.
//!
//! This crate is a preview, repo-local release candidate. Its public boundary is the
//! WASM ABI helper surface, manifest/instance/memory DTOs, compatibility helpers, and
//! macro exports used by Rust projection crates compiled to `wasm32-wasip2`. Low-level
//! memory and FFI details may still change before the first crates.io release.

pub mod compat;
pub mod exports;
pub mod ffi;
pub mod instance;
pub mod manifest;
pub mod memory;
pub mod prelude;

// Re-export dependencies used in exported macro expansions, so consuming crates don't need to
// depend on them directly.
pub use serde_json;
