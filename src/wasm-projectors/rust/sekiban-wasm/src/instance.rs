use sekiban_core::event::Event;
use sekiban_core::projector::{MultiProjector, MultiProjectorQuery, TagProjector};
use sekiban_core::registry::{DomainProjectorRegistration, ProjectorRegistrar};
use sekiban_core::state::StatePayload;
use std::cell::RefCell;
use std::collections::HashMap;

/// Tag projector instance trait.
pub trait TagProjectorInstance: Send {
    fn projector_name(&self) -> &'static str;
    fn apply_event(&mut self, event_type: &str, payload: &str);
    fn serialize_state(&self) -> String;
    fn restore_state(&mut self, state_json: &str);
}

/// Multi projector instance trait.
pub trait MultiProjectorInstance: Send {
    fn projector_name(&self) -> &'static str;
    fn apply_event(&mut self, event_type: &str, payload: &str, tags: &[String]);
    fn serialize_state(&self) -> String;
    fn restore_state(&mut self, state_json: &str);
    fn execute_query(&self, query_type: &str, params: &str) -> String;
    fn execute_list_query(&self, query_type: &str, params: &str) -> String;
}

enum InstanceSlot {
    Tag(Box<dyn TagProjectorInstance>),
    Multi(Box<dyn MultiProjectorInstance>),
}

impl InstanceSlot {
    fn apply_event(&mut self, event_type: &str, payload: &str) {
        match self {
            InstanceSlot::Tag(inst) => inst.apply_event(event_type, payload),
            InstanceSlot::Multi(inst) => inst.apply_event(event_type, payload, &[]),
        }
    }

    fn apply_event_with_tags(&mut self, event_type: &str, payload: &str, tags: &[String]) {
        match self {
            InstanceSlot::Tag(inst) => inst.apply_event(event_type, payload),
            InstanceSlot::Multi(inst) => inst.apply_event(event_type, payload, tags),
        }
    }

    fn serialize_state(&self) -> String {
        match self {
            InstanceSlot::Tag(inst) => inst.serialize_state(),
            InstanceSlot::Multi(inst) => inst.serialize_state(),
        }
    }

    fn restore_state(&mut self, state_json: &str) {
        match self {
            InstanceSlot::Tag(inst) => inst.restore_state(state_json),
            InstanceSlot::Multi(inst) => inst.restore_state(state_json),
        }
    }

    fn execute_query(&self, query_type: &str, params: &str) -> String {
        match self {
            InstanceSlot::Multi(inst) => inst.execute_query(query_type, params),
            InstanceSlot::Tag(_) => serde_json::json!({"error": "query not supported for tag projectors"}).to_string(),
        }
    }

    fn execute_list_query(&self, query_type: &str, params: &str) -> String {
        match self {
            InstanceSlot::Multi(inst) => inst.execute_list_query(query_type, params),
            InstanceSlot::Tag(_) => serde_json::json!({"error": "list query not supported for tag projectors"}).to_string(),
        }
    }
}

struct InstanceManager {
    instances: Vec<InstanceSlot>,
}

impl InstanceManager {
    fn new() -> Self {
        Self {
            instances: Vec::new(),
        }
    }

    fn push(&mut self, instance: InstanceSlot) -> i32 {
        let id = self.instances.len() as i32;
        self.instances.push(instance);
        id
    }

    fn get_mut(&mut self, id: i32) -> Option<&mut InstanceSlot> {
        self.instances.get_mut(id as usize)
    }

    fn get(&self, id: i32) -> Option<&InstanceSlot> {
        self.instances.get(id as usize)
    }
}

thread_local! {
    static INSTANCES: RefCell<InstanceManager> = RefCell::new(InstanceManager::new());
}

/// Tag projector wrapper.
pub struct TagProjectorWrapper<P: TagProjector> {
    state: P::State,
    _phantom: std::marker::PhantomData<P>,
}

impl<P: TagProjector> TagProjectorWrapper<P> {
    pub fn new() -> Self {
        Self {
            state: P::State::default(),
            _phantom: std::marker::PhantomData,
        }
    }
}

impl<P: TagProjector> TagProjectorInstance for TagProjectorWrapper<P> {
    fn projector_name(&self) -> &'static str {
        P::PROJECTOR_NAME
    }

    fn apply_event(&mut self, event_type: &str, payload: &str) {
        let event = Event {
            event_type: event_type.to_string(),
            payload_json: payload.to_string(),
            sortable_unique_id: None,
        };

        self.state = P::project(std::mem::take(&mut self.state), &event);
    }

    fn serialize_state(&self) -> String {
        // Sekiban semantics: empty tag state should serialize as "{}" so hosts can treat it
        // as EmptyTagStatePayload and allow the payload type to "become" the concrete state type
        // only after the first event is applied.
        if self.state.is_empty() {
            return "{}".to_string();
        }
        serde_json::to_string(&self.state).unwrap_or_else(|_| "{}".to_string())
    }

    fn restore_state(&mut self, state_json: &str) {
        let trimmed = state_json.trim();
        if trimmed.is_empty() || trimmed == "{}" {
            self.state = P::State::default();
            return;
        }
        if let Ok(state) = serde_json::from_str(trimmed) {
            self.state = state;
        }
    }
}

/// Multi projector wrapper.
pub struct MultiProjectorWrapper<P: MultiProjector + MultiProjectorQuery> {
    state: P::State,
    _phantom: std::marker::PhantomData<P>,
}

impl<P: MultiProjector + MultiProjectorQuery> MultiProjectorWrapper<P> {
    pub fn new() -> Self {
        Self {
            state: P::State::default(),
            _phantom: std::marker::PhantomData,
        }
    }
}

impl<P: MultiProjector + MultiProjectorQuery> MultiProjectorInstance for MultiProjectorWrapper<P> {
    fn projector_name(&self) -> &'static str {
        P::PROJECTOR_NAME
    }

    fn apply_event(&mut self, event_type: &str, payload: &str, _tags: &[String]) {
        let event = Event {
            event_type: event_type.to_string(),
            payload_json: payload.to_string(),
            sortable_unique_id: None,
        };

        self.state = P::project(std::mem::take(&mut self.state), &event);
    }

    fn serialize_state(&self) -> String {
        serde_json::to_string(&self.state).unwrap_or_else(|_| "{}".to_string())
    }

    fn restore_state(&mut self, state_json: &str) {
        let trimmed = state_json.trim();
        if trimmed.is_empty() || trimmed == "{}" {
            self.state = P::State::default();
            return;
        }
        if let Ok(state) = serde_json::from_str(trimmed) {
            self.state = state;
        }
    }

    fn execute_query(&self, query_type: &str, params: &str) -> String {
        match P::execute_query(&self.state, query_type, params) {
            Some(result) => result,
            None => serde_json::json!({"error": "query execution not implemented"}).to_string(),
        }
    }

    fn execute_list_query(&self, query_type: &str, params: &str) -> String {
        match P::execute_list_query(&self.state, query_type, params) {
            Some(result) => result,
            None => serde_json::json!({"error": "list query execution not implemented"}).to_string(),
        }
    }
}

enum ProjectorFactory {
    Tag(fn() -> Box<dyn TagProjectorInstance>),
    Multi(fn() -> Box<dyn MultiProjectorInstance>),
}

struct ProjectorFactoryRegistry {
    factories: HashMap<&'static str, ProjectorFactory>,
}

impl ProjectorFactoryRegistry {
    fn new() -> Self {
        Self {
            factories: HashMap::new(),
        }
    }

    fn create_instance(&self, name: &str) -> Option<InstanceSlot> {
        self.factories.get(name).map(|factory| match factory {
            ProjectorFactory::Tag(f) => InstanceSlot::Tag(f()),
            ProjectorFactory::Multi(f) => InstanceSlot::Multi(f()),
        })
    }
}

impl ProjectorRegistrar for ProjectorFactoryRegistry {
    fn register_tag_projector<P: TagProjector>(&mut self) {
        fn build<P: TagProjector>() -> Box<dyn TagProjectorInstance> {
            Box::new(TagProjectorWrapper::<P>::new())
        }

        self.factories
            .insert(P::PROJECTOR_NAME, ProjectorFactory::Tag(build::<P>));
    }

    fn register_multi_projector<P: MultiProjector + MultiProjectorQuery>(&mut self) {
        fn build<P: MultiProjector + MultiProjectorQuery>() -> Box<dyn MultiProjectorInstance> {
            Box::new(MultiProjectorWrapper::<P>::new())
        }

        self.factories
            .insert(P::PROJECTOR_NAME, ProjectorFactory::Multi(build::<P>));
    }
}

fn build_registry<D: DomainProjectorRegistration>() -> ProjectorFactoryRegistry {
    let mut registry = ProjectorFactoryRegistry::new();
    D::register_projectors(&mut registry);
    registry
}

/// Create an instance by projector name.
pub fn create_instance_by_name<D: DomainProjectorRegistration>(name: &str) -> i32 {
    let registry = build_registry::<D>();
    match registry.create_instance(name) {
        Some(instance) => INSTANCES.with(|instances| instances.borrow_mut().push(instance)),
        None => -1,
    }
}

/// Apply event to instance.
pub fn apply_event_to_instance(instance_id: i32, event_type: &str, payload: &str) {
    INSTANCES.with(|instances| {
        if let Some(instance) = instances.borrow_mut().get_mut(instance_id) {
            instance.apply_event(event_type, payload);
        }
    });
}

/// Apply event to instance with tags (compat).
pub fn apply_event_to_instance_with_tags(
    instance_id: i32,
    event_type: &str,
    payload: &str,
    tags: &[String],
) {
    INSTANCES.with(|instances| {
        if let Some(instance) = instances.borrow_mut().get_mut(instance_id) {
            instance.apply_event_with_tags(event_type, payload, tags);
        }
    });
}

/// Get instance state as JSON.
pub fn get_instance_state(instance_id: i32) -> String {
    INSTANCES.with(|instances| {
        instances
            .borrow()
            .get(instance_id)
            .map(|instance| instance.serialize_state())
            .unwrap_or_else(|| "{}".to_string())
    })
}

/// Restore instance state from JSON.
pub fn restore_instance_state(instance_id: i32, state_json: &str) {
    INSTANCES.with(|instances| {
        if let Some(instance) = instances.borrow_mut().get_mut(instance_id) {
            instance.restore_state(state_json);
        }
    });
}

/// Execute query on an instance.
pub fn execute_query(instance_id: i32, query_type: &str, params: &str) -> String {
    INSTANCES.with(|instances| {
        instances
            .borrow()
            .get(instance_id)
            .map(|instance| instance.execute_query(query_type, params))
            .unwrap_or_else(|| serde_json::json!({"error": "instance not found"}).to_string())
    })
}

/// Execute list query on an instance.
pub fn execute_list_query(instance_id: i32, query_type: &str, params: &str) -> String {
    INSTANCES.with(|instances| {
        instances
            .borrow()
            .get(instance_id)
            .map(|instance| instance.execute_list_query(query_type, params))
            .unwrap_or_else(|| serde_json::json!({"error": "instance not found"}).to_string())
    })
}
