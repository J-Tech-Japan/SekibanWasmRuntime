fn pack_ptr_len(ptr : Int, len : Int) -> Int64 {
  ((ptr.to_int64()) << 32) | (len.to_int64())
}

fn read_utf8(ptr : Int, len : Int) -> String {
  @ffi.ptr2str(ptr, len)
}

fn write_utf8(s : String) -> Int64 {
  if s == "" {
    return pack_ptr_len(0, 0)
  }
  let bytes = @encoding/utf8.encode(s[:])
  let len = bytes.length()
  if len <= 0 {
    return pack_ptr_len(0, 0)
  }
  let ptr = @ffi.malloc(len)
  for i in 0..<len {
    @ffi.store8(ptr + i, bytes[i].to_int())
  }
  pack_ptr_len(ptr, len)
}

pub fn alloc(size : UInt) -> UInt {
  if size == 0 {
    return 0
  }
  (@ffi.malloc(size.reinterpret_as_int())).reinterpret_as_uint()
}

pub fn dealloc(ptr : UInt, _len : UInt) -> Unit {
  if ptr == 0 {
    return
  }
  @ffi.free(ptr.reinterpret_as_int())
}

pub fn create_instance(projector_type_ptr : UInt, projector_type_len : UInt) -> UInt {
  let projector_type = read_utf8(projector_type_ptr.reinterpret_as_int(), projector_type_len.reinterpret_as_int())
  let kind = resolve_kind(projector_type)
  if kind == Unknown {
    return 0
  }
  let id = take_next_id()
  put_instance(id, instance_new(kind))
  id
}

pub fn apply_event(instance_id : UInt, event_type_ptr : UInt, event_type_len : UInt, payload_ptr : UInt, payload_len : UInt) -> Unit {
  let event_type = read_utf8(event_type_ptr.reinterpret_as_int(), event_type_len.reinterpret_as_int())
  let payload_json = read_utf8(payload_ptr.reinterpret_as_int(), payload_len.reinterpret_as_int())
  match get_instance(instance_id) {
    None => ()
    Some(inst) => put_instance(instance_id, apply_event_to_instance(inst, event_type, payload_json))
  }
}

pub fn apply_event_with_metadata(
  instance_id : UInt,
  event_type_ptr : UInt,
  event_type_len : UInt,
  payload_ptr : UInt,
  payload_len : UInt,
  _metadata_ptr : UInt,
  _metadata_len : UInt
) -> Unit {
  apply_event(instance_id, event_type_ptr, event_type_len, payload_ptr, payload_len)
}

pub fn execute_query(instance_id : UInt, query_type_ptr : UInt, query_type_len : UInt, params_ptr : UInt, params_len : UInt) -> Int64 {
  let query_type = read_utf8(query_type_ptr.reinterpret_as_int(), query_type_len.reinterpret_as_int())
  let params_json = read_utf8(params_ptr.reinterpret_as_int(), params_len.reinterpret_as_int())
  match get_instance(instance_id) {
    None => write_utf8("null")
    Some(inst) => write_utf8(execute_query_json(inst, query_type, params_json))
  }
}

pub fn execute_list_query(instance_id : UInt, query_type_ptr : UInt, query_type_len : UInt, params_ptr : UInt, params_len : UInt) -> Int64 {
  let query_type = read_utf8(query_type_ptr.reinterpret_as_int(), query_type_len.reinterpret_as_int())
  let params_json = read_utf8(params_ptr.reinterpret_as_int(), params_len.reinterpret_as_int())
  match get_instance(instance_id) {
    None => write_utf8("[]")
    Some(inst) => write_utf8(execute_list_query_json(inst, query_type, params_json))
  }
}

pub fn serialize_state(instance_id : UInt) -> Int64 {
  match get_instance(instance_id) {
    None => write_utf8("{}")
    Some(inst) => write_utf8(serialize_instance_state(inst))
  }
}

pub fn restore_state(instance_id : UInt, state_ptr : UInt, state_len : UInt) -> Unit {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  match get_instance(instance_id) {
    None => ()
    Some(inst) => put_instance(instance_id, restore_instance_state(inst, state_json))
  }
}
