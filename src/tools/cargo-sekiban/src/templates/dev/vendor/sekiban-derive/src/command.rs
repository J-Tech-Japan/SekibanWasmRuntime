use proc_macro::TokenStream;
use quote::quote;
use syn::{parse_macro_input, DeriveInput, LitStr};

pub fn derive_command(input: TokenStream) -> TokenStream {
    let input = parse_macro_input!(input as DeriveInput);
    let name = &input.ident;
    let name_str = name.to_string();
    let name_lit = LitStr::new(&name_str, proc_macro2::Span::call_site());

    let expanded = quote! {
        impl ::sekiban_core::command::CommandMeta for #name {
            const COMMAND_TYPE: &'static str = #name_lit;
        }
    };

    TokenStream::from(expanded)
}
