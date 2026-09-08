use crate::ffi;
use crate::instance::{
    apply_event_to_instance_with_tags, create_instance_by_name, get_instance_state,
    restore_instance_state,
};
use sekiban_core::registry::{DomainDefinition, DomainProjectorRegistration};

/// Backward-compatible tag projection.
pub fn project_tag_compat<D: DomainDefinition + DomainProjectorRegistration>(
    state_ptr: i32,
    state_len: i32,
    event_type_ptr: i32,
    event_type_len: i32,
    payload_ptr: i32,
    payload_len: i32,
) -> i64 {
    let state_json = unsafe { ffi::read_string(state_ptr, state_len) };
    let event_type = unsafe { ffi::read_string(event_type_ptr, event_type_len) };
    let payload = unsafe { ffi::read_string(payload_ptr, payload_len) };

    let domain_types = D::domain_types();
    let projector_name = match domain_types.tag_projectors.first() {
        Some(info) => info.name,
        None => return ffi::write_error("no tag projectors registered"),
    };

    let instance_id = create_instance_by_name::<D>(projector_name);
    if instance_id < 0 {
        return ffi::write_error("failed to create tag projector instance");
    }

    restore_instance_state(instance_id, &state_json);
    apply_event_to_instance_with_tags(instance_id, &event_type, &payload, &[]);
    let new_state = get_instance_state(instance_id);
    ffi::write_string(&new_state)
}

/// Backward-compatible multi projection.
pub fn project_multi_compat<D: DomainDefinition + DomainProjectorRegistration>(
    state_ptr: i32,
    state_len: i32,
    event_type_ptr: i32,
    event_type_len: i32,
    payload_ptr: i32,
    payload_len: i32,
    tags_ptr: i32,
    tags_len: i32,
) -> i64 {
    let state_json = unsafe { ffi::read_string(state_ptr, state_len) };
    let event_type = unsafe { ffi::read_string(event_type_ptr, event_type_len) };
    let payload = unsafe { ffi::read_string(payload_ptr, payload_len) };
    let tags_json = unsafe { ffi::read_string(tags_ptr, tags_len) };

    let tags: Vec<String> = serde_json::from_str(&tags_json).unwrap_or_default();

    let domain_types = D::domain_types();
    let projector_name = match domain_types.multi_projectors.first() {
        Some(info) => info.name,
        None => return ffi::write_error("no multi projectors registered"),
    };

    let instance_id = create_instance_by_name::<D>(projector_name);
    if instance_id < 0 {
        return ffi::write_error("failed to create multi projector instance");
    }

    restore_instance_state(instance_id, &state_json);
    apply_event_to_instance_with_tags(instance_id, &event_type, &payload, &tags);
    let new_state = get_instance_state(instance_id);
    ffi::write_string(&new_state)
}
