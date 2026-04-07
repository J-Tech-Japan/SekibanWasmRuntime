pub fn ok(output : CommandOutput) -> WasmResult {
  { ok: true, status: 200, error: None, message: None, output: Some(output) }
}

pub fn ok_noop() -> WasmResult {
  ok({ events: [], tags: [], consistencyTags: [], expectedVersions: Map::new() })
}

pub fn err(status : Int, code : String, message : String) -> WasmResult {
  { ok: false, status, error: Some(code), message: Some(message), output: None }
}

pub fn new_command_output(event_type : String, payload_json : String, tags : Array[String]) -> CommandOutput {
  {
    events: [{ eventType: event_type, payload: payload_json }],
    tags,
    consistencyTags: tags,
    expectedVersions: Map::new(),
  }
}
