// StaticTagProjectorResolver maps tag groups to projector names.
// Equivalent to Rust's StaticTagProjectorResolver.

pub struct StaticTagProjectorResolver {
  mappings : Map[String, String]
  default_projector : String?
}

pub fn StaticTagProjectorResolver::new() -> StaticTagProjectorResolver {
  { mappings: Map::new(), default_projector: None }
}

pub fn StaticTagProjectorResolver::with_tag_group(
  self : StaticTagProjectorResolver,
  tag_group : String,
  projector_name : String
) -> StaticTagProjectorResolver {
  self.mappings[tag_group] = projector_name
  self
}

pub fn StaticTagProjectorResolver::with_default(
  self : StaticTagProjectorResolver,
  projector_name : String
) -> StaticTagProjectorResolver {
  { ..self, default_projector: Some(projector_name) }
}

pub fn StaticTagProjectorResolver::resolve(
  self : StaticTagProjectorResolver,
  tag_group : String
) -> String? {
  match self.mappings.get(tag_group) {
    Some(name) => Some(name)
    None => self.default_projector
  }
}
