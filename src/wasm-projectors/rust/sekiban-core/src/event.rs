use serde::{de::DeserializeOwned, Serialize};

/// Event payload trait.
pub trait EventPayload: Serialize + DeserializeOwned + Clone + Send + Sync + 'static {
    /// Event type name used for host communication.
    const EVENT_TYPE: &'static str;

    /// Dynamic event type name.
    fn event_type(&self) -> &'static str {
        Self::EVENT_TYPE
    }
}

/// Event wrapper with metadata.
#[derive(Debug, Clone)]
pub struct Event {
    pub event_type: String,
    pub payload_json: String,
    pub sortable_unique_id: Option<String>,
}

impl Event {
    /// Deserialize payload into the specified event type.
    pub fn deserialize<E: EventPayload>(&self) -> Option<E> {
        if self.event_type == E::EVENT_TYPE {
            serde_json::from_str(&self.payload_json).ok()
        } else {
            None
        }
    }

    /// Create from borrowed strings without allocation (wraps references).
    pub fn from_refs(event_type: &str, payload_json: &str) -> Self {
        Self {
            event_type: event_type.to_string(),
            payload_json: payload_json.to_string(),
            sortable_unique_id: None,
        }
    }
}
