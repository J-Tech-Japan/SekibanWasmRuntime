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
