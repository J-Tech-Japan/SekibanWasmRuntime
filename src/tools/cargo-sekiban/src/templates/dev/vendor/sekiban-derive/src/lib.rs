//! Procedural macros for declaring Sekiban Rust domain types.
//!
//! This crate is a preview, repo-local release candidate. Its public boundary is the
//! derive macro set exported from this module: [`Event`], [`State`], [`Tag`],
//! [`TagProjector`], [`MultiProjector`], and [`Command`]. Generated implementation
//! details and helper modules are not stability commitments before the first crates.io
//! release.

mod command;
mod event;
mod projector;
mod state;
mod tag;

use proc_macro::TokenStream;

#[proc_macro_derive(Event, attributes(event))]
pub fn derive_event(input: TokenStream) -> TokenStream {
    event::derive_event(input)
}

#[proc_macro_derive(State, attributes(state))]
pub fn derive_state(input: TokenStream) -> TokenStream {
    state::derive_state(input)
}

#[proc_macro_derive(Tag, attributes(tag, tag_id))]
pub fn derive_tag(input: TokenStream) -> TokenStream {
    tag::derive_tag(input)
}

#[proc_macro_derive(TagProjector, attributes(projector))]
pub fn derive_tag_projector(input: TokenStream) -> TokenStream {
    projector::derive_tag_projector(input)
}

#[proc_macro_derive(MultiProjector, attributes(projector))]
pub fn derive_multi_projector(input: TokenStream) -> TokenStream {
    projector::derive_multi_projector(input)
}

#[proc_macro_derive(Command, attributes(command))]
pub fn derive_command(input: TokenStream) -> TokenStream {
    command::derive_command(input)
}
