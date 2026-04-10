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

pub fn new_multi_command_output(
  events : Array[EventPayload],
  tags : Array[String],
  consistency_tags : Array[String],
  expected_versions : Map[String, Int]
) -> CommandOutput {
  {
    events,
    tags,
    consistencyTags: consistency_tags,
    expectedVersions: expected_versions,
  }
}

pub fn new_multi_json_command_output(
  event_types : Array[String],
  payload_jsons : Array[String],
  tags : Array[String],
  consistency_tags : Array[String],
  expected_versions : Map[String, Int]
) -> CommandOutput {
  let events : Array[EventPayload] = []
  let count = if event_types.length() < payload_jsons.length() { event_types.length() } else { payload_jsons.length() }
  for i = 0; i < count; i = i + 1 {
    events.push({
      eventType: event_types[i],
      payload: payload_jsons[i],
    })
  }
  new_multi_command_output(events, tags, consistency_tags, expected_versions)
}
