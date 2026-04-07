// SekibanRuntimeClient provides HTTP communication with the Sekiban WASM runtime server.
// Uses moonbitlang/async/http Client API.

pub struct SekibanRuntimeClient {
  host : Bytes
  port : Int
  protocol : @ahttp.Protocol
}

pub fn SekibanRuntimeClient::new(base_url : String) -> SekibanRuntimeClient {
  let (stripped, protocol) = if base_url.has_prefix("http://") {
    (base_url[7:].to_string(), @ahttp.Http)
  } else if base_url.has_prefix("https://") {
    (base_url[8:].to_string(), @ahttp.Https)
  } else {
    (base_url, @ahttp.Http)
  }
  let authority = match first_separator_index(stripped) {
    -1 => stripped
    i => stripped[:i].to_string()
  }
  let default_port = protocol.default_port()
  let (host_str, port) = if authority.has_prefix("[") {
    parse_ipv6_authority(authority, default_port)
  } else {
    parse_host_port(authority, default_port)
  }
  let host_bytes = @encoding/utf8.encode(host_str[:])
  { host: host_bytes, port, protocol }
}

fn find_char(s : String, ch : Char) -> Int {
  let target = ch.to_uint().to_uint16()
  for i = 0; i < s.length(); i = i + 1 {
    if s[i] == target {
      return i
    }
  }
  -1
}

fn find_last_char(s : String, ch : Char) -> Int {
  let target = ch.to_uint().to_uint16()
  for i = s.length() - 1; i >= 0; i = i - 1 {
    if s[i] == target {
      return i
    }
  }
  -1
}

fn first_separator_index(s : String) -> Int {
  let slash_pos = find_char(s, '/')
  let query_pos = find_char(s, '?')
  let hash_pos = find_char(s, '#')
  let result = slash_pos
  let result = if result == -1 || (query_pos != -1 && query_pos < result) {
    query_pos
  } else {
    result
  }
  if result == -1 || (hash_pos != -1 && hash_pos < result) {
    hash_pos
  } else {
    result
  }
}

fn parse_port(value : String, fallback : Int) -> Int {
  try {
    @string.parse_int(value)
  } catch {
    _ => fallback
  }
}

fn parse_host_port(authority : String, default_port : Int) -> (String, Int) {
  let colon_pos = find_last_char(authority, ':')
  if colon_pos == -1 {
    return (authority, default_port)
  }
  let host = authority[:colon_pos].to_string()
  let port = parse_port(authority[colon_pos + 1:].to_string(), default_port)
  (host, port)
}

fn parse_ipv6_authority(authority : String, default_port : Int) -> (String, Int) {
  let closing = find_char(authority, ']')
  if closing == -1 {
    return (authority, default_port)
  }
  let host = authority[:closing + 1].to_string()
  if closing + 1 >= authority.length() || authority[closing + 1] != ':'.to_uint().to_uint16() {
    return (host, default_port)
  }
  let port = parse_port(authority[closing + 2:].to_string(), default_port)
  (host, port)
}

fn[T] ok_result(value : T) -> RequestResult[T] {
  { value: Some(value), error: None }
}

fn[T] error_result(status : Int, message : String, body : String) -> RequestResult[T] {
  { value: None, error: Some({ status, message, body }) }
}

async fn SekibanRuntimeClient::post_json(
  self : SekibanRuntimeClient,
  path : String,
  body_json : String
) -> RequestResult[String] {
  try {
    let body_bytes = @encoding/utf8.encode(body_json[:])
    let content_type_header = @ahttp.Header(
      @encoding/utf8.encode("Content-Type"[:]),
      @encoding/utf8.encode("application/json"[:]),
    )
    let path_bytes = @encoding/utf8.encode(path[:])
    let client = @ahttp.Client::connect(
      self.host,
      headers=[],
      protocol=self.protocol,
      port=self.port,
    )
    let response = client.post(
      path_bytes,
      @ahttp.Fixed(body_bytes),
      extra_headers=[content_type_header],
    )
    let result_text = @encoding/utf8.decode_lossy(client.read_all())
    client.close()
    if response.code >= 200 && response.code <= 299 {
      ok_result(result_text)
    } else {
      let message = if result_text == "" {
        "HTTP \{response.code} \{response.reason}"
      } else {
        "HTTP \{response.code}"
      }
      error_result(response.code, message, result_text)
    }
  } catch {
    _ => error_result(500, "HTTP request failed", "")
  }
}

pub async fn SekibanRuntimeClient::get_tag_state(
  self : SekibanRuntimeClient,
  tag_group : String,
  tag_content : String,
  projector_name : String
) -> RequestResult[TagStateResponse] {
  let tag_state_id = "\{tag_group}:\{tag_content}:\{projector_name}"
  let request_body : TagStateRequest = { tagStateId: tag_state_id }
  match self.post_json("/api/sekiban/serialized/tag-state", request_body.to_json().stringify()) {
    { value: Some(result), error: None } => {
      try {
        let j = @json.parse(result[:])
        ok_result(@json.from_json(j))
      } catch {
        _ => error_result(500, "Failed to parse tag-state response", result)
      }
    }
    { value: None, error: Some(error) } => { value: None, error: Some(error) }
    _ => error_result(500, "tag-state request failed", "")
  }
}

pub async fn SekibanRuntimeClient::commit(
  self : SekibanRuntimeClient,
  request : CommitRequest
) -> RequestResult[CommitResponse] {
  match self.post_json("/api/sekiban/serialized/commit", request.to_json().stringify()) {
    { value: Some(result), error: None } => {
      try {
        let j = @json.parse(result[:])
        ok_result(@json.from_json(j))
      } catch {
        _ => error_result(500, "Failed to parse commit response", result)
      }
    }
    { value: None, error: Some(error) } => { value: None, error: Some(error) }
    _ => error_result(500, "commit request failed", "")
  }
}

pub async fn SekibanRuntimeClient::execute_query(
  self : SekibanRuntimeClient,
  query_type : String,
  query_params_json : String,
  wait_for : String?
) -> RequestResult[String] {
  let request : QueryRequest = {
    queryType: query_type,
    queryParamsJson: query_params_json,
    waitForSortableUniqueId: wait_for,
  }
  self.post_json("/api/sekiban/serialized/query", request.to_json().stringify())
}

pub async fn SekibanRuntimeClient::execute_list_query(
  self : SekibanRuntimeClient,
  query_type : String,
  query_params_json : String,
  wait_for : String?
) -> RequestResult[String] {
  let request : QueryRequest = {
    queryType: query_type,
    queryParamsJson: query_params_json,
    waitForSortableUniqueId: wait_for,
  }
  self.post_json("/api/sekiban/serialized/list-query", request.to_json().stringify())
}
