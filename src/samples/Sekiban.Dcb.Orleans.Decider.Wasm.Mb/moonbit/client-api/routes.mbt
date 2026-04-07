// Domain-specific route handlers for the Sekiban DCB Decider ClientAPI.
// Each handler follows the pattern:
//   1. Parse request body (for POST) or query params (for GET)
//   2. Fetch tag state from WASM runtime server
//   3. Execute serialized command or query
//   4. Return JSON response

// ──── Weather Forecast ────

struct CreateWeatherRequest {
  forecastId : String?
  location : String
  date : String
  temperatureC : Int
  summary : String?
} derive(ToJson, @json.FromJson, Show)

struct UpdateLocationRequest {
  forecastId : String
  newLocation : String
} derive(ToJson, @json.FromJson, Show)

struct DeleteWeatherRequest {
  forecastId : String
} derive(ToJson, @json.FromJson, Show)

// GET /api/weatherforecast - List weather forecasts
pub async fn handle_get_weather_forecasts(
  req : @server.Request,
  client : @http.SekibanRuntimeClient
) -> @server.Response {
  let (_path, query) = @server.split_path_query(req.path)
  let location_filter = @server.get_query_param(query, "location")
  let forecast_id = @server.get_query_param(query, "forecastId")
  let query_params : Json = {
    "locationFilter": match location_filter {
      None => (None : String?).to_json()
      Some(v) => v.to_json()
    },
    "forecastId": match forecast_id {
      None => (None : String?).to_json()
      Some(v) => v.to_json()
    },
  }
  let result = client.execute_list_query(
    "GetWeatherForecastListQuery",
    query_params.stringify(),
    None,
  )
  @server.Response::{ status: 200, content_type: "application/json", body: result }
}

// GET /api/weatherforecast/count
pub async fn handle_get_weather_count(
  req : @server.Request,
  client : @http.SekibanRuntimeClient
) -> @server.Response {
  let (_path, query) = @server.split_path_query(req.path)
  let location_filter = @server.get_query_param(query, "location")
  let query_params : Json = {
    "locationFilter": match location_filter {
      None => (None : String?).to_json()
      Some(v) => v.to_json()
    },
  }
  let result = client.execute_query(
    "GetWeatherForecastCountQuery",
    query_params.stringify(),
    None,
  )
  @server.Response::{ status: 200, content_type: "application/json", body: result }
}

// POST /api/weatherforecast - Create weather forecast
pub async fn handle_create_weather(
  req : @server.Request,
  client : @http.SekibanRuntimeClient
) -> @server.Response {
  let body : CreateWeatherRequest = @server.parse_request_body(
    req.body,
    { forecastId: None, location: "", date: "", temperatureC: 0, summary: None },
  )
  let forecast_id = match body.forecastId {
    Some(id) => id
    None => @server.generate_id()
  }
  // Execute serialized command via WASM server
  let command_json : Json = {
    "forecastId": forecast_id.to_json(),
    "location": body.location.to_json(),
    "date": body.date.to_json(),
    "temperatureC": body.temperatureC.to_json(),
    "summary": match body.summary {
      None => (None : String?).to_json()
      Some(v) => v.to_json()
    },
    "nowIso": @server.now_iso().to_json(),
  }
  // For now, return the command that would be executed
  // Full integration requires WASM bridge or serialized command endpoint
  @server.json_ok({
    "status": "command_prepared".to_json(),
    "forecastId": forecast_id.to_json(),
    "command": command_json,
  })
}

// ──── Students ────

struct CreateStudentRequest {
  studentId : String?
  name : String
  maxClassCount : Int
} derive(ToJson, @json.FromJson, Show)

// GET /api/students
pub async fn handle_get_students(
  _req : @server.Request,
  client : @http.SekibanRuntimeClient
) -> @server.Response {
  let result = client.execute_list_query(
    "GetStudentListQuery",
    "{}",
    None,
  )
  @server.Response::{ status: 200, content_type: "application/json", body: result }
}

// POST /api/students
pub async fn handle_create_student(
  req : @server.Request,
  client : @http.SekibanRuntimeClient
) -> @server.Response {
  let body : CreateStudentRequest = @server.parse_request_body(
    req.body,
    { studentId: None, name: "", maxClassCount: 3 },
  )
  let student_id = match body.studentId {
    Some(id) => id
    None => @server.generate_id()
  }
  let command_json : Json = {
    "studentId": student_id.to_json(),
    "name": body.name.to_json(),
    "maxClassCount": body.maxClassCount.to_json(),
  }
  ignore(client)
  @server.json_ok({
    "status": "command_prepared".to_json(),
    "studentId": student_id.to_json(),
    "command": command_json,
  })
}

// ──── Health Check ────

pub async fn handle_health(
  _req : @server.Request,
  _client : @http.SekibanRuntimeClient
) -> @server.Response {
  @server.json_ok({ "message": "Sekiban decider MoonBit ClientApi is running".to_json() })
}
