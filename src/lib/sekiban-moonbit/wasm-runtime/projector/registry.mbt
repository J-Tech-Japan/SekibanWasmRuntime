pub(all) struct ProjectorCallbacks {
  pub resolve_kind : (String) -> Int
  pub create_instance : (Int) -> UInt
  pub apply_event : (UInt, String, String) -> Unit
  pub apply_event_with_metadata : (UInt, String, String, String) -> Unit
  pub serialize_state : (UInt) -> String
  pub restore_state : (UInt, String) -> Unit
  pub execute_query : (UInt, String, String) -> String
  pub execute_list_query : (UInt, String, String) -> String
}

let callbacks : Ref[ProjectorCallbacks?] = @ref.new(None)

pub fn register_callbacks(cb : ProjectorCallbacks) -> Unit {
  callbacks.val = Some(cb)
}

fn get_callbacks() -> ProjectorCallbacks {
  match callbacks.val {
    Some(cb) => cb
    None => abort("ProjectorCallbacks not registered. Call register_callbacks() in init block.")
  }
}
