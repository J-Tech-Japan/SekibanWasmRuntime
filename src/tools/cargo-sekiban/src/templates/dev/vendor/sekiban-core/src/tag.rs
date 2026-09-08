use serde::{de::DeserializeOwned, Serialize};

/// Tag trait.
pub trait Tag: Serialize + DeserializeOwned + Clone + Send + Sync + 'static {
    /// Tag group name.
    const TAG_GROUP: &'static str;

    /// Tag id string.
    fn tag_id(&self) -> String;

    /// Full tag string.
    fn to_tag_string(&self) -> String {
        format!("{}:{}", Self::TAG_GROUP, self.tag_id())
    }

    /// Whether this tag should be treated as a consistency tag.
    fn is_consistency_tag(&self) -> bool {
        true
    }
}

/// Parse a tag string into (group, id).
pub fn parse_tag(tag_string: &str) -> Option<(String, String)> {
    let parts: Vec<&str> = tag_string.splitn(2, ':').collect();
    if parts.len() == 2 {
        Some((parts[0].to_string(), parts[1].to_string()))
    } else {
        None
    }
}
