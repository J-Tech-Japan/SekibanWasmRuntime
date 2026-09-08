use crate::{event::Event, state::StatePayload};
use serde::Serialize;

/// Projector kind.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum ProjectorKind {
    Tag,
    Multi,
}

/// Projector metadata for manifest/registry.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProjectorInfo {
    pub name: &'static str,
    pub version: &'static str,
    pub kind: ProjectorKind,
    pub state_type: &'static str,
    pub event_types: Vec<&'static str>,
}

/// Base projector implementation provided by users.
pub trait Projector: Send + Sync + 'static {
    type State: StatePayload;

    fn event_types() -> Vec<&'static str>;
    fn project(state: Self::State, event: &Event) -> Self::State;
}

/// Metadata implemented by #[derive(TagProjector)].
pub trait TagProjectorMeta: Send + Sync + 'static {
    const PROJECTOR_NAME: &'static str;
    const PROJECTOR_VERSION: &'static str;
}

/// Metadata implemented by #[derive(MultiProjector)].
pub trait MultiProjectorMeta: Send + Sync + 'static {
    const PROJECTOR_NAME: &'static str;
    const PROJECTOR_VERSION: &'static str;
}

/// Tag projector trait.
pub trait TagProjector: Projector + TagProjectorMeta {
    fn info() -> ProjectorInfo {
        ProjectorInfo {
            name: Self::PROJECTOR_NAME,
            version: Self::PROJECTOR_VERSION,
            kind: ProjectorKind::Tag,
            state_type: Self::State::STATE_TYPE,
            event_types: Self::event_types(),
        }
    }
}

impl<T> TagProjector for T where T: Projector + TagProjectorMeta {}

/// Multi projector trait.
pub trait MultiProjector: Projector + MultiProjectorMeta {
    fn supported_queries() -> Vec<&'static str> {
        Vec::new()
    }

    fn info() -> ProjectorInfo {
        ProjectorInfo {
            name: Self::PROJECTOR_NAME,
            version: Self::PROJECTOR_VERSION,
            kind: ProjectorKind::Multi,
            state_type: Self::State::STATE_TYPE,
            event_types: Self::event_types(),
        }
    }
}

impl<T> MultiProjector for T where T: Projector + MultiProjectorMeta {}

/// Optional query handlers for multi projectors.
pub trait MultiProjectorQuery: MultiProjector {
    fn execute_query(
        _state: &Self::State,
        _query_type: &str,
        _params: &str,
    ) -> Option<String> {
        None
    }

    fn execute_list_query(
        _state: &Self::State,
        _query_type: &str,
        _params: &str,
    ) -> Option<String> {
        None
    }
}
