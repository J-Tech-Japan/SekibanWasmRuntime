// Typed MoonBit client smoke for the mooncakes.io external-consumer proof
// (SWR-G065). Uses the public sekiban/sekiban-client package to drive the
// public runtime container: commit WeatherForecastCreated +
// WeatherForecastLocationUpdated, read the tag state back, and confirm the
// in-memory list and count queries — mirroring the Rust/Go sample clients.

let tag_group_weather = "weather"

let projector_weather = "WeatherForecastProjector"

fn env_or(key : String, fallback : String) -> String {
  match @sys.get_env_vars().get(key) {
    Some(value) => if value == "" { fallback } else { value }
    None => fallback
  }
}

fn b64(payload : String) -> String {
  let bytes = @encoding/utf8.encode(payload[:])
  @base64.encode(bytes[:])
}

fn fail(message : String) -> Unit {
  println("MB_CLIENT_FAIL: " + message)
  @sys.exit(1)
}

struct TagStatePayload {
  forecastId : String
  location : String
} derive(ToJson, @json.FromJson, Show)

struct ListItem {
  forecastId : String
  location : String
} derive(ToJson, @json.FromJson, Show)

struct CountResult {
  count : Int
} derive(ToJson, @json.FromJson, Show)

async fn run_smoke() -> Unit raise {
  let base_url = env_or("RUNTIME_URL", "http://localhost:8080")
  let forecast_id = env_or("SAMPLE_FORECAST_ID", "00000000-0000-0000-0000-0000000000aa")
  let original_location = env_or("SAMPLE_FORECAST_LOCATION", "Kyoto")
  let updated_location = env_or("SAMPLE_UPDATED_LOCATION", "Osaka")
  let created_at = env_or("SAMPLE_CREATED_AT", "2026-07-03T00:00:00Z")
  let tag = tag_group_weather + ":" + forecast_id

  let client = @http.SekibanRuntimeClient::new(base_url)

  // 1. Command execution: create + update through the serialized commit flow.
  let created_payload : Json = {
    "forecastId": forecast_id.to_json(),
    "location": original_location.to_json(),
    "temperatureC": Json::number(24),
    "summary": "mooncakes mb sample".to_json(),
    "createdAt": created_at.to_json(),
  }
  let create_request : @http.CommitRequest = {
    eventCandidates: [
      {
        eventPayloadName: "WeatherForecastCreated",
        payload: b64(created_payload.stringify()),
        tags: [tag],
      },
    ],
    consistencyTags: [],
  }
  let create_response = client.commit(create_request)
  match create_response.value {
    Some(_) => ()
    None => {
      let detail = match create_response.error {
        Some(err) => "\{err.status} \{err.message} \{err.body}"
        None => "unknown"
      }
      fail("create commit failed: " + detail)
    }
  }

  let updated_payload : Json = {
    "forecastId": forecast_id.to_json(),
    "newLocation": updated_location.to_json(),
    "updatedAt": created_at.to_json(),
  }
  let update_request : @http.CommitRequest = {
    eventCandidates: [
      {
        eventPayloadName: "WeatherForecastLocationUpdated",
        payload: b64(updated_payload.stringify()),
        tags: [tag],
      },
    ],
    consistencyTags: [],
  }
  let update_response = client.commit(update_request)
  match update_response.value {
    Some(_) => ()
    None => {
      let detail = match update_response.error {
        Some(err) => "\{err.status} \{err.message} \{err.body}"
        None => "unknown"
      }
      fail("update commit failed: " + detail)
    }
  }

  // 2. Tag-state readback: the projected state must show the updated location.
  let mut tag_state_location = ""
  let mut tag_state_version = 0
  let mut attempts = 0
  while attempts < 30 {
    let state_response = client.get_tag_state(tag_group_weather, forecast_id, projector_weather)
    match state_response.value {
      Some(state) => {
        tag_state_version = state.version
        if state.payload != "" {
          match @base64.decode(state.payload) {
            decoded => {
              let state_json = @encoding/utf8.decode_lossy(decoded, ignore_bom=true)
              let parsed : TagStatePayload = try {
                @json.from_json(@json.parse(state_json[:]))
              } catch {
                _ => TagStatePayload::{ forecastId: "", location: "" }
              }
              tag_state_location = parsed.location
            }
          }
        }
      }
      None => ()
    }
    if tag_state_location == updated_location {
      break
    }
    attempts = attempts + 1
    @async.sleep(2000)
  }
  if tag_state_location != updated_location {
    fail("tag-state readback did not reflect the updated location (got '" + tag_state_location + "')")
  }

  // 3. In-memory projection queries.
  let list_params : Json = { "forecastId": forecast_id.to_json() }
  let mut found = false
  let mut list_result = ""
  attempts = 0
  while attempts < 30 && !found {
    let list_response = client.execute_list_query("GetWeatherForecastListQuery", list_params.stringify(), None)
    match list_response.value {
      Some(result) => {
        list_result = result
        let items : Array[ListItem] = try {
          @json.from_json(@json.parse(result[:]))
        } catch {
          _ => []
        }
        for item in items {
          if item.forecastId == forecast_id && item.location == updated_location {
            found = true
            break
          }
        }
      }
      None => ()
    }
    if !found {
      attempts = attempts + 1
      @async.sleep(2000)
    }
  }
  if !found {
    fail("list-query did not return the updated forecast: " + list_result)
  }

  let count_response = client.execute_query("GetWeatherForecastCountQuery", "{}", None)
  let count = match count_response.value {
    Some(result) => {
      let parsed : CountResult = try {
        @json.from_json(@json.parse(result[:]))
      } catch {
        _ => CountResult::{ count: -1 }
      }
      parsed.count
    }
    None => -1
  }
  if count < 1 {
    fail("count-query returned \{count}, expected >= 1")
  }

  let evidence : Json = {
    "forecastId": forecast_id.to_json(),
    "originalLocation": original_location.to_json(),
    "updatedLocation": updated_location.to_json(),
    "tagStateVersion": Json::number(tag_state_version.to_double()),
    "tagStateLocation": tag_state_location.to_json(),
    "countQueryCount": Json::number(count.to_double()),
    "foundInListQuery": Json::boolean(true),
  }
  println(evidence.stringify())
}

fn main {
  try {
    @async.with_event_loop(async fn(_group) raise {
      run_smoke()
    })
  } catch {
    _ => fail("client crashed")
  }
}
