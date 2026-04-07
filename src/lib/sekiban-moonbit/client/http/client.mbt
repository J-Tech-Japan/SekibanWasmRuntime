// SekibanRuntimeClient provides HTTP communication with the Sekiban WASM runtime server.
// Uses moonbitlang/async/http Client API.

pub struct SekibanRuntimeClient {
  host : Bytes
  port : Int
}

pub fn SekibanRuntimeClient::new(base_url : String) -> SekibanRuntimeClient {
  let stripped = if base_url.has_prefix("http://") {
    base_url[7:].to_string()
  } else if base_url.has_prefix("https://") {
    base_url[8:].to_string()
  } else {
    base_url
  }
  let colon_pos = find_char(stripped, ':')
  let (host_str, p) = match colon_pos {
    -1 => (stripped, 80)
    i => {
      let h = stripped[:i].to_string()
      let p_str = stripped[i + 1:].to_string()
      let p_clean = if p_str.has_suffix("/") {
        p_str[:p_str.length() - 1].to_string()
      } else {
        p_str
      }
      let port_val = try { @string.parse_int(p_clean) } catch { _ => 80 }
      (h, port_val)
    }
  }
  let host_bytes = @encoding/utf8.encode(host_str[:])
  { host: host_bytes, port: p }
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

async fn SekibanRuntimeClient::post_json(
  self : SekibanRuntimeClient,
  path : String,
  body_json : String
) -> String {
  let body_bytes = @encoding/utf8.encode(body_json[:])
  let content_type_header = @ahttp.Header(
    @encoding/utf8.encode("Content-Type"[:]),
    @encoding/utf8.encode("application/json"[:]),
  )
  let path_bytes = @encoding/utf8.encode(path[:])
  let client = @ahttp.Client::connect(
    self.host,
    headers=[],
    protocol=@ahttp.Http,
    port=self.port,
  )
  let response = client.post(
    path_bytes,
    @ahttp.Fixed(body_bytes),
    extra_headers=[content_type_header],
  )
  guard response.code is (200..=299) else {
    client.close()
    abort("HTTP \{response.code}")
  }
  let result_bytes = client.read_all()
  client.close()
  @encoding/utf8.decode_lossy(result_bytes)
}

pub async fn SekibanRuntimeClient::get_tag_state(
  self : SekibanRuntimeClient,
  tag_group : String,
  tag_content : String,
  projector_name : String
) -> TagStateResponse {
  let tag_state_id = "\{tag_group}:\{tag_content}:\{projector_name}"
  let request_body : TagStateRequest = { tagStateId: tag_state_id }
  let result = self.post_json("/api/sekiban/serialized/tag-state", request_body.to_json().stringify())
  let j = @json.parse(result[:])
  @json.from_json(j)
}

pub async fn SekibanRuntimeClient::commit(
  self : SekibanRuntimeClient,
  request : CommitRequest
) -> CommitResponse {
  let result = self.post_json("/api/sekiban/serialized/commit", request.to_json().stringify())
  let j = @json.parse(result[:])
  @json.from_json(j)
}

pub async fn SekibanRuntimeClient::execute_query(
  self : SekibanRuntimeClient,
  query_type : String,
  query_params_json : String,
  wait_for : String?
) -> String {
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
) -> String {
  let request : QueryRequest = {
    queryType: query_type,
    queryParamsJson: query_params_json,
    waitForSortableUniqueId: wait_for,
  }
  self.post_json("/api/sekiban/serialized/list-query", request.to_json().stringify())
}
