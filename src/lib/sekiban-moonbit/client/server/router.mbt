// HTTP server routing framework for Sekiban MoonBit client APIs.
// Provides method+path matching and JSON request/response helpers.

pub(all) struct Route {
  method : String    // "GET", "POST", etc.
  path : String      // exact path match (e.g., "/api/weatherforecast")
  handler : async (Request, @http.SekibanRuntimeClient) -> Response
}

pub(all) struct Request {
  method : String
  path : String
  query : String
  body : String
  headers : Map[String, String]
}

pub(all) struct Response {
  status : Int
  content_type : String
  body : String
}

pub fn json_response(status : Int, body : Json) -> Response {
  { status, content_type: "application/json", body: body.stringify() }
}

pub fn json_ok(body : Json) -> Response {
  json_response(200, body)
}

pub fn error_response(status : Int, code : String, message : String) -> Response {
  json_response(status, { "error": code.to_json(), "message": message.to_json() })
}

pub fn not_found() -> Response {
  error_response(404, "NotFound", "endpoint not found")
}

pub fn method_not_allowed() -> Response {
  error_response(405, "MethodNotAllowed", "method not allowed")
}

// Match a route from the route table.
pub fn match_route(routes : Array[Route], method : String, path : String) -> Route? {
  for route in routes {
    if route.method == method && route.path == path {
      return Some(route)
    }
  }
  None
}

// Parse a query parameter from a query string.
// Example: get_query_param("location=Tokyo&page=1", "location") => Some("Tokyo")
pub fn get_query_param(query : String, name : String) -> String? {
  if query == "" {
    return None
  }
  let parts = query.split("&")
  for part in parts {
    let item = part.to_string()
    let eq_pos = find_char_in_string(item, '=')
    if eq_pos == -1 {
      if item == name {
        return Some("")
      }
      continue
    }
    let key = item[:eq_pos].to_string()
    if key == name {
      return Some(item[eq_pos + 1:].to_string())
    }
  }
  None
}

// Split path and query string from a full URL path.
// Example: split_path_query("/api/test?a=1") => ("/api/test", "a=1")
pub fn split_path_query(full_path : String) -> (String, String) {
  let qpos = find_char_in_string(full_path, '?')
  if qpos == -1 {
    (full_path, "")
  } else {
    (full_path[:qpos].to_string(), full_path[qpos + 1:].to_string())
  }
}

fn find_char_in_string(s : String, ch : Char) -> Int {
  let target = ch.to_uint().to_uint16()
  for i = 0; i < s.length(); i = i + 1 {
    if s[i] == target {
      return i
    }
  }
  -1
}
