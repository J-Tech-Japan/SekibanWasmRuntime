use serde::{de::DeserializeOwned, Serialize};

/// Query trait.
pub trait Query: Serialize + DeserializeOwned + Send + Sync {
    const QUERY_TYPE: &'static str;

    fn wait_for_sortable_id(&self) -> Option<&str> {
        None
    }
}

/// List query trait.
pub trait ListQuery: Serialize + DeserializeOwned + Send + Sync {
    const QUERY_TYPE: &'static str;

    fn wait_for_sortable_id(&self) -> Option<&str> {
        None
    }
}
