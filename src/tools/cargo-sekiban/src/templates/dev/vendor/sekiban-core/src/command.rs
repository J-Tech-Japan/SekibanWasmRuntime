use crate::{event::EventPayload, state::StatePayload, tag::Tag};
use async_trait::async_trait;
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use std::collections::HashMap;

/// Command output (events + metadata).
#[derive(Debug, Clone)]
pub struct CommandOutput {
    pub events: Vec<EventOutput>,
    pub tags: Vec<String>,
    pub consistency_tags: Vec<String>,
    pub expected_versions: HashMap<String, i32>,
}

/// Event output.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EventOutput {
    pub event_type: String,
    pub payload: String,
}

impl CommandOutput {
    /// Create from a single event and tag.
    pub fn single<E: EventPayload, T: Tag>(event: E, tag: T) -> Result<Self, serde_json::Error> {
        let tag_string = tag.to_tag_string();
        Ok(Self {
            events: vec![EventOutput {
                event_type: E::EVENT_TYPE.to_string(),
                payload: serde_json::to_string(&event)?,
            }],
            tags: vec![tag_string.clone()],
            consistency_tags: if tag.is_consistency_tag() {
                vec![tag_string.clone()]
            } else {
                vec![]
            },
            expected_versions: HashMap::new(),
        })
    }

    /// Add expected version.
    pub fn with_expected_version(mut self, tag: impl Tag, version: i32) -> Self {
        self.expected_versions.insert(tag.to_tag_string(), version);
        self
    }
}

/// Command error.
#[derive(Debug, thiserror::Error)]
pub enum CommandError {
    #[error("entity already exists: {0}")]
    AlreadyExists(String),
    #[error("entity not found: {0}")]
    NotFound(String),
    #[error("entity deleted: {0}")]
    Deleted(String),
    #[error("validation error: {0}")]
    Validation(String),
    #[error("serialization error: {0}")]
    Serialization(String),
}

/// Command context for state access.
#[async_trait]
pub trait CommandContext: Send + Sync {
    async fn get_state<S: StatePayload, T: Tag>(&self, tag: &T) -> Result<(S, i32), CommandError>;
    async fn tag_exists<T: Tag>(&self, tag: &T) -> Result<bool, CommandError>;
}

/// Command handler.
#[async_trait]
pub trait CommandHandler: Serialize + DeserializeOwned + Send + Sync {
    async fn handle<C: CommandContext + ?Sized>(
        &self,
        context: &C,
    ) -> Result<Option<CommandOutput>, CommandError>;
}

/// Command metadata for registration.
pub trait CommandMeta: Send + Sync + 'static {
    const COMMAND_TYPE: &'static str;
}

/// Convenience trait combining metadata + handler.
pub trait Command: CommandMeta + CommandHandler {}

impl<T> Command for T where T: CommandMeta + CommandHandler {}
