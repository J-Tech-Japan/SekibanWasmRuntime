use proc_macro::TokenStream;
use quote::quote;
use syn::{parse_macro_input, Data, DeriveInput, Fields, LitStr};

pub fn derive_tag(input: TokenStream) -> TokenStream {
    let input = parse_macro_input!(input as DeriveInput);
    let name = &input.ident;

    let tag_group = match extract_tag_group(&input.attrs) {
        Some(group) => group,
        None => return compile_error("#[tag(group = \"...\")] attribute is required"),
    };
    let group_lit = LitStr::new(&tag_group, proc_macro2::Span::call_site());

    let tag_id_expr = match extract_id_field(&input.data) {
        Ok(expr) => expr,
        Err(msg) => return compile_error(&msg),
    };

    let expanded = quote! {
        impl ::sekiban_core::tag::Tag for #name {
            const TAG_GROUP: &'static str = #group_lit;

            fn tag_id(&self) -> String {
                #tag_id_expr.to_string()
            }
        }
    };

    TokenStream::from(expanded)
}

fn extract_tag_group(attrs: &[syn::Attribute]) -> Option<String> {
    for attr in attrs {
        if attr.path().is_ident("tag") {
            let mut value: Option<String> = None;
            let _ = attr.parse_nested_meta(|meta| {
                if meta.path.is_ident("group") {
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

fn extract_id_field(data: &Data) -> Result<proc_macro2::TokenStream, String> {
    match data {
        Data::Struct(s) => match &s.fields {
            Fields::Named(fields) => {
                let mut tagged: Option<&syn::Field> = None;
                for field in &fields.named {
                    if field.attrs.iter().any(|a| a.path().is_ident("tag_id")) {
                        tagged = Some(field);
                        break;
                    }
                }
                let field = tagged.unwrap_or_else(|| fields.named.first().expect("struct has no fields"));
                let ident = field
                    .ident
                    .as_ref()
                    .ok_or_else(|| "failed to resolve tag id field".to_string())?;
                Ok(quote! { self.#ident })
            }
            Fields::Unnamed(fields) => {
                let mut index: Option<usize> = None;
                for (idx, field) in fields.unnamed.iter().enumerate() {
                    if field.attrs.iter().any(|a| a.path().is_ident("tag_id")) {
                        index = Some(idx);
                        break;
                    }
                }
                let idx = index.unwrap_or(0);
                let index = syn::Index::from(idx);
                Ok(quote! { self.#index })
            }
            Fields::Unit => Err("Tag must have at least one field".to_string()),
        },
        _ => Err("Tag can only be derived for structs".to_string()),
    }
}

fn compile_error(message: &str) -> TokenStream {
    TokenStream::from(quote! { compile_error!(#message); })
}
