//! Core Rust contracts for Sekiban WASM projection domains.
//!
//! This crate is a preview, repo-local release candidate. Its public boundary is the
//! domain-facing trait and DTO surface re-exported from [`prelude`] and the crate root:
//! commands, events, state payloads, tags, projectors, queries, registry helpers, and
//! serializable runtime payloads. Module internals remain subject to change until the
//! first crates.io release is approved.

pub mod command;
pub mod event;
pub mod macros;
pub mod projector;
pub mod query;
pub mod registry;
pub mod state;
pub mod tag;

pub mod prelude;

pub use command::*;
pub use event::*;
pub use projector::*;
pub use query::*;
pub use registry::*;
pub use state::*;
pub use tag::*;
