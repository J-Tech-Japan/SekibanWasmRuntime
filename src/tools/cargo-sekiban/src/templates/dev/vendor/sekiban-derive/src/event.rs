use proc_macro::TokenStream;
use quote::quote;
use syn::{parse_macro_input, DeriveInput, LitStr};

pub fn derive_event(input: TokenStream) -> TokenStream {
    let input = parse_macro_input!(input as DeriveInput);
    let name = &input.ident;
    let name_str = name.to_string();

    let event_type = extract_event_name(&input.attrs).unwrap_or(name_str);
    let event_lit = LitStr::new(&event_type, proc_macro2::Span::call_site());

    let expanded = quote! {
        impl ::sekiban_core::event::EventPayload for #name {
            const EVENT_TYPE: &'static str = #event_lit;
        }
    };

    TokenStream::from(expanded)
}

fn extract_event_name(attrs: &[syn::Attribute]) -> Option<String> {
    for attr in attrs {
        if attr.path().is_ident("event") {
            let mut value: Option<String> = None;
            let _ = attr.parse_nested_meta(|meta| {
                if meta.path.is_ident("name") {
                    let lit: LitStr = meta.value()?.parse()?;
                    value = Some(lit.value());
                }
                Ok(())
            });
            if value.is_some() {
                return value;
            }
        }
    }
    None
}
