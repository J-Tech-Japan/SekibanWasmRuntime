// SekibanExecutor orchestrates the command execution flow:
//   1. Fetch tag state(s) from WASM server
//   2. (Caller executes WASM command with the state)
//   3. Commit resulting events back to the server
//
// This is the MoonBit equivalent of:
//   - Rust RemoteSekibanExecutor
//   - JavaScript server.mjs finalizeCommand()

pub struct CommandOutput {
  events : Array[EventEntry]
  tags : Array[String]
  consistencyTags : Array[String]
} derive(ToJson, @json.FromJson, Show)

pub struct EventEntry {
  eventType : String
  payload : String
} derive(ToJson, @json.FromJson, Show)

pub struct CommandResult {
  ok : Bool
  status : Int
  error : String?
  message : String?
  output : CommandOutput?
} derive(ToJson, @json.FromJson, Show)

pub struct FinalizeResult {
  success : Bool
  eventId : String
  sortableUniqueId : String
  statusCode : Int
  error : String?
} derive(ToJson, @json.FromJson, Show)

// Finalize a command execution by committing events to the server.
// loaded_states contains the tag states that were fetched before command execution.
pub async fn finalize_command(
  client : @http.SekibanRuntimeClient,
  wasm_result : CommandResult,
  loaded_states : Array[@http.LoadedTagState]
) -> FinalizeResult {
  if !wasm_result.ok {
    return {
      success: false,
      eventId: "",
      sortableUniqueId: "",
      statusCode: wasm_result.status,
      error: wasm_result.message,
    }
  }
  let output = match wasm_result.output {
    None => return {
      success: true,
      eventId: "",
      sortableUniqueId: "",
      statusCode: 200,
      error: None,
    }
    Some(o) => o
  }
  if output.events.length() == 0 {
    return {
      success: true,
      eventId: "",
      sortableUniqueId: "",
      statusCode: 200,
      error: None,
    }
  }
  // Build commit request
  let event_candidates : Array[@http.CommitEventCandidate] = []
  for ev in output.events {
    event_candidates.push({
      eventPayloadName: ev.eventType,
      payloadBase64: ev.payload,  // Note: payload may need base64 encoding depending on server contract
      tags: output.tags,
    })
  }
  let consistency_tags : Array[@http.ConsistencyTagEntry] = []
  for state in loaded_states {
    if state.lastSortableUniqueId != "" {
      consistency_tags.push({
        tag: "\{state.tagGroup}:\{state.tagContent}",
        lastSortableUniqueId: state.lastSortableUniqueId,
      })
    }
  }
  let commit_request : @http.CommitRequest = {
    eventCandidates: event_candidates,
    consistencyTags: consistency_tags,
  }
  let commit_response = client.commit(commit_request)
  {
    success: true,
    eventId: commit_response.eventId,
    sortableUniqueId: commit_response.sortableUniqueId,
    statusCode: 200,
    error: None,
  }
}
