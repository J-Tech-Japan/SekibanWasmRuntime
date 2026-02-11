use proc_macro::TokenStream;
use quote::quote;
use syn::{parse_macro_input, Data, DeriveInput, Fields, LitStr};

pub fn derive_state(input: TokenStream) -> TokenStream {
    let input = parse_macro_input!(input as DeriveInput);
    let name = &input.ident;
    let name_str = name.to_string();

    let empty_check_field = extract_empty_check_field(&input.attrs);

    let is_empty_impl = if let Some(field) = empty_check_field {
        let (field_ident, field_ty) = match find_field(&input.data, &field) {
            Some(pair) => pair,
            None => {
                return compile_error(&format!(
                    "field '{}' not found for #[state(empty_check = \"...\")]",
                    field
                ))
            }
        };
        quote! {
            fn is_empty(&self) -> bool {
                self.#field_ident == <#field_ty as Default>::default()
            }
        }
    } else {
        quote! {
            fn is_empty(&self) -> bool {
                *self == Self::default()
            }
        }
    };

    let name_lit = LitStr::new(&name_str, proc_macro2::Span::call_site());

    let expanded = quote! {
        impl ::sekiban_core::state::StatePayload for #name {
            const STATE_TYPE: &'static str = #name_lit;

            #is_empty_impl
        }
    };

    TokenStream::from(expanded)
}

fn extract_empty_check_field(attrs: &[syn::Attribute]) -> Option<String> {
    for attr in attrs {
        if attr.path().is_ident("state") {
            let mut value: Option<String> = None;
            let _ = attr.parse_nested_meta(|meta| {
                if meta.path.is_ident("empty_check") {
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

fn find_field(data: &Data, name: &str) -> Option<(syn::Ident, syn::Type)> {
    match data {
        Data::Struct(s) => match &s.fields {
            Fields::Named(fields) => fields.named.iter().find_map(|field| {
                let ident = field.ident.as_ref()?;
                if ident == name {
                    Some((ident.clone(), field.ty.clone()))
                } else {
                    None
                }
            }),
            _ => None,
        },
        _ => None,
    }
}

fn compile_error(message: &str) -> TokenStream {
    TokenStream::from(quote! { compile_error!(#message); })
}
