use sekiban_core::projector::ProjectorKind;
use sekiban_core::registry::DomainDefinition;
use serde::Serialize;

/// WASM manifest.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WasmManifest {
    pub version: &'static str,
    pub projectors: Vec<ProjectorDescriptor>,
    pub event_types: Vec<&'static str>,
    pub command_types: Vec<&'static str>,
    pub query_types: Vec<QueryDescriptor>,
}

/// Projector descriptor.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProjectorDescriptor {
    pub name: &'static str,
    pub version: &'static str,
    pub kind: ProjectorKind,
    pub state_type: &'static str,
    pub event_types: Vec<&'static str>,
}

/// Query descriptor.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct QueryDescriptor {
    pub query_type: &'static str,
    pub is_list: bool,
}

/// Generate manifest from a domain definition.
pub fn generate_manifest<D: DomainDefinition>() -> WasmManifest {
    let domain_types = D::domain_types();

    let mut projectors = Vec::new();
    for proj in &domain_types.tag_projectors {
        projectors.push(ProjectorDescriptor {
            name: proj.name,
            version: proj.version,
            kind: proj.kind,
            state_type: proj.state_type,
            event_types: proj.event_types.clone(),
        });
    }
    for proj in &domain_types.multi_projectors {
        projectors.push(ProjectorDescriptor {
            name: proj.name,
            version: proj.version,
            kind: proj.kind,
            state_type: proj.state_type,
            event_types: proj.event_types.clone(),
        });
    }

    let event_types = domain_types.events.iter().map(|e| e.event_type).collect();
    let command_types = domain_types.commands.iter().map(|c| c.command_type).collect();
    let query_types = domain_types
        .queries
        .iter()
        .map(|q| QueryDescriptor {
            query_type: q.query_type,
            is_list: q.is_list,
        })
        .collect();

    WasmManifest {
        version: "2.0.0",
        projectors,
        event_types,
        command_types,
        query_types,
    }
}
