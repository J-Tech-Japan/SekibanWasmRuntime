use serde::{de::DeserializeOwned, Serialize};

/// State payload trait.
pub trait StatePayload: Serialize + DeserializeOwned + Clone + Default + Send + Sync + 'static {
    /// State type name for debugging.
    const STATE_TYPE: &'static str;

    /// Whether the state is considered empty.
    fn is_empty(&self) -> bool;
}

/// Optional helper trait for updating common fields.
pub trait StateUpdate: StatePayload {
    fn with_deleted(self, deleted: bool) -> Self;
}
