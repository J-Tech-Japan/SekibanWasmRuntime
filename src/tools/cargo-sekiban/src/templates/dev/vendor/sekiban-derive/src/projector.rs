use proc_macro::TokenStream;
use quote::quote;
use syn::{parse_macro_input, DeriveInput, LitStr};

pub fn derive_tag_projector(input: TokenStream) -> TokenStream {
    derive_projector(input, true)
}

pub fn derive_multi_projector(input: TokenStream) -> TokenStream {
    derive_projector(input, false)
}

fn derive_projector(input: TokenStream, is_tag: bool) -> TokenStream {
    let input = parse_macro_input!(input as DeriveInput);
    let name = &input.ident;
    let name_str = name.to_string();

    let (projector_name, projector_version) = extract_projector_meta(&input.attrs, &name_str);
    let name_lit = LitStr::new(&projector_name, proc_macro2::Span::call_site());
    let version_lit = LitStr::new(&projector_version, proc_macro2::Span::call_site());

    let trait_path = if is_tag {
        quote! { ::sekiban_core::projector::TagProjectorMeta }
    } else {
        quote! { ::sekiban_core::projector::MultiProjectorMeta }
    };

    let expanded = quote! {
        impl #trait_path for #name {
            const PROJECTOR_NAME: &'static str = #name_lit;
            const PROJECTOR_VERSION: &'static str = #version_lit;
        }
    };

    TokenStream::from(expanded)
}

fn extract_projector_meta(attrs: &[syn::Attribute], default_name: &str) -> (String, String) {
    let mut name = None;
    let mut version = None;
    for attr in attrs {
        if attr.path().is_ident("projector") {
            let _ = attr.parse_nested_meta(|meta| {
                if meta.path.is_ident("name") {
                    let lit: LitStr = meta.value()?.parse()?;
                    name = Some(lit.value());
                } else if meta.path.is_ident("version") {
                    let lit: LitStr = meta.value()?.parse()?;
                    version = Some(lit.value());
                }
                Ok(())
            });
        }
    }
    (
        name.unwrap_or_else(|| default_name.to_string()),
        version.unwrap_or_else(|| "1.0.0".to_string()),
    )
}
