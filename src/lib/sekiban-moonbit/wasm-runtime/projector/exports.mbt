pub fn create_instance(projector_type_ptr : UInt, projector_type_len : UInt) -> UInt {
  let projector_type = @core.read_utf8(projector_type_ptr.reinterpret_as_int(), projector_type_len.reinterpret_as_int())
  let cb = get_callbacks()
  let kind_id = (cb.resolve_kind)(projector_type)
  if kind_id == 0 {
    return 0
  }
  (cb.create_instance)(kind_id)
}

pub fn apply_event(instance_id : UInt, event_type_ptr : UInt, event_type_len : UInt, payload_ptr : UInt, payload_len : UInt) -> Unit {
  let event_type = @core.read_utf8(event_type_ptr.reinterpret_as_int(), event_type_len.reinterpret_as_int())
  let payload_json = @core.read_utf8(payload_ptr.reinterpret_as_int(), payload_len.reinterpret_as_int())
  let cb = get_callbacks()
  (cb.apply_event)(instance_id, event_type, payload_json)
}

pub fn apply_event_with_metadata(
  instance_id : UInt,
  event_type_ptr : UInt,
  event_type_len : UInt,
  payload_ptr : UInt,
  payload_len : UInt,
  metadata_ptr : UInt,
  metadata_len : UInt
) -> Unit {
  let event_type = @core.read_utf8(event_type_ptr.reinterpret_as_int(), event_type_len.reinterpret_as_int())
  let payload_json = @core.read_utf8(payload_ptr.reinterpret_as_int(), payload_len.reinterpret_as_int())
  let metadata_json = @core.read_utf8(metadata_ptr.reinterpret_as_int(), metadata_len.reinterpret_as_int())
  let cb = get_callbacks()
  (cb.apply_event_with_metadata)(instance_id, event_type, payload_json, metadata_json)
}

pub fn execute_query(instance_id : UInt, query_type_ptr : UInt, query_type_len : UInt, params_ptr : UInt, params_len : UInt) -> Int64 {
  let query_type = @core.read_utf8(query_type_ptr.reinterpret_as_int(), query_type_len.reinterpret_as_int())
  let params_json = @core.read_utf8(params_ptr.reinterpret_as_int(), params_len.reinterpret_as_int())
  let cb = get_callbacks()
  @core.write_utf8((cb.execute_query)(instance_id, query_type, params_json))
}

pub fn execute_list_query(instance_id : UInt, query_type_ptr : UInt, query_type_len : UInt, params_ptr : UInt, params_len : UInt) -> Int64 {
  let query_type = @core.read_utf8(query_type_ptr.reinterpret_as_int(), query_type_len.reinterpret_as_int())
  let params_json = @core.read_utf8(params_ptr.reinterpret_as_int(), params_len.reinterpret_as_int())
  let cb = get_callbacks()
  @core.write_utf8((cb.execute_list_query)(instance_id, query_type, params_json))
}

pub fn serialize_state(instance_id : UInt) -> Int64 {
  let cb = get_callbacks()
  @core.write_utf8((cb.serialize_state)(instance_id))
}

pub fn restore_state(instance_id : UInt, state_ptr : UInt, state_len : UInt) -> Unit {
  let state_json = @core.read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let cb = get_callbacks()
  (cb.restore_state)(instance_id, state_json)
}
