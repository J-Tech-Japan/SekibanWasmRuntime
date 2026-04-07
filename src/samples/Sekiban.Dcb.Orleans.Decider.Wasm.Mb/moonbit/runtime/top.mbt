// Memory management and WASM exports are now provided by
// sekiban/sekiban-wasm-runtime shared library.
// This file contains re-exported WASM functions and type aliases.

// Type aliases for shared library types used throughout domain code
pub type WasmResult = @core.WasmResult
pub type CommandOutput = @core.CommandOutput
pub type EventPayload = @core.EventPayload
pub type WasmEventMetadata = @core.WasmEventMetadata

// Delegate local helpers for commands.mbt convenience
fn read_utf8(ptr : Int, len : Int) -> String { @core.read_utf8(ptr, len) }
fn write_utf8(s : String) -> Int64 { @core.write_utf8(s) }

// Re-export WASM runtime functions so they appear in this package's link exports
pub fn alloc(size : UInt) -> UInt { @core.alloc(size) }
pub fn dealloc(ptr : UInt, len : UInt) -> Unit { @core.dealloc(ptr, len) }

pub fn create_instance(pt_ptr : UInt, pt_len : UInt) -> UInt {
  @projector.create_instance(pt_ptr, pt_len)
}

pub fn apply_event(id : UInt, et_ptr : UInt, et_len : UInt, p_ptr : UInt, p_len : UInt) -> Unit {
  @projector.apply_event(id, et_ptr, et_len, p_ptr, p_len)
}

pub fn apply_event_with_metadata(id : UInt, et_ptr : UInt, et_len : UInt, p_ptr : UInt, p_len : UInt, m_ptr : UInt, m_len : UInt) -> Unit {
  @projector.apply_event_with_metadata(id, et_ptr, et_len, p_ptr, p_len, m_ptr, m_len)
}

pub fn execute_query(id : UInt, qt_ptr : UInt, qt_len : UInt, p_ptr : UInt, p_len : UInt) -> Int64 {
  @projector.execute_query(id, qt_ptr, qt_len, p_ptr, p_len)
}

pub fn execute_list_query(id : UInt, qt_ptr : UInt, qt_len : UInt, p_ptr : UInt, p_len : UInt) -> Int64 {
  @projector.execute_list_query(id, qt_ptr, qt_len, p_ptr, p_len)
}

pub fn serialize_state(id : UInt) -> Int64 {
  @projector.serialize_state(id)
}

pub fn restore_state(id : UInt, s_ptr : UInt, s_len : UInt) -> Unit {
  @projector.restore_state(id, s_ptr, s_len)
}
