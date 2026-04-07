// Register domain-specific projector callbacks with the shared WASM runtime library.
// This init block runs before any WASM export is called.

fn init {
  @projector.register_callbacks(@projector.ProjectorCallbacks::{
    resolve_kind: fn(name) {
      let kind = resolve_kind(name)
      if kind == Unknown { 0 } else { kind_to_int(kind) }
    },
    create_instance: fn(kind_id) {
      let kind = int_to_kind(kind_id)
      let id = take_next_id()
      put_instance(id, instance_new(kind))
      id
    },
    apply_event: fn(id, event_type, payload_json) {
      match get_instance(id) {
        None => ()
        Some(inst) => put_instance(id, apply_event_to_instance(inst, event_type, payload_json))
      }
    },
    apply_event_with_metadata: fn(id, event_type, payload_json, _metadata_json) {
      match get_instance(id) {
        None => ()
        Some(inst) => put_instance(id, apply_event_to_instance(inst, event_type, payload_json))
      }
    },
    serialize_state: fn(id) {
      match get_instance(id) {
        None => "{}"
        Some(inst) => serialize_instance_state(inst)
      }
    },
    restore_state: fn(id, state_json) {
      match get_instance(id) {
        None => ()
        Some(inst) => put_instance(id, restore_instance_state(inst, state_json))
      }
    },
    execute_query: fn(id, query_type, params_json) {
      match get_instance(id) {
        None => "null"
        Some(inst) => execute_query_json(inst, query_type, params_json)
      }
    },
    execute_list_query: fn(id, query_type, params_json) {
      match get_instance(id) {
        None => "[]"
        Some(inst) => execute_list_query_json(inst, query_type, params_json)
      }
    },
  })
}

// Kind <-> Int conversion for callback interface
fn kind_to_int(kind : Kind) -> Int {
  match kind {
    Unknown => 0
    WeatherTag => 1
    StudentTag => 2
    ClassRoomTag => 3
    UserDirectoryTag => 4
    UserAccessTag => 5
    RoomTag => 6
    ReservationTag => 7
    ApprovalRequestTag => 8
    WeatherList => 9
    StudentList => 10
    ClassRoomList => 11
    UserDirectoryList => 12
    UserAccessList => 13
    RoomList => 14
    ReservationList => 15
    ApprovalInboxList => 16
  }
}

fn int_to_kind(id : Int) -> Kind {
  match id {
    1 => WeatherTag
    2 => StudentTag
    3 => ClassRoomTag
    4 => UserDirectoryTag
    5 => UserAccessTag
    6 => RoomTag
    7 => ReservationTag
    8 => ApprovalRequestTag
    9 => WeatherList
    10 => StudentList
    11 => ClassRoomList
    12 => UserDirectoryList
    13 => UserAccessList
    14 => RoomList
    15 => ReservationList
    16 => ApprovalInboxList
    _ => Unknown
  }
}
