// Common HTTP server handler utilities.
// Provides CORS headers, JSON body parsing, and standard response patterns.

// Add CORS headers to a response string for browser clients.
pub fn cors_headers() -> Map[String, String] {
  let headers : Map[String, String] = Map::new()
  headers["Access-Control-Allow-Origin"] = "*"
  headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS"
  headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization"
  headers
}

// Parse a JSON body string into a typed value.
pub fn[T : @json.FromJson] parse_request_body(body : String, fallback : T) -> T? {
  if body == "" {
    return Some(fallback)
  }
  try {
    let j = @json.parse(body[:])
    let v : T = @json.from_json(j)
    Some(v)
  } catch {
    _ => None
  }
}

// Normalize a UUID-style ID (trim whitespace, lowercase).
pub fn normalize_id(id : String) -> String {
  id.trim().to_lower().to_string()
}

let generated_id_counter : Ref[Int] = @ref.new(0)

// Generate a simple UUID v4-like string.
// Note: MoonBit may not have crypto-random, so this is a placeholder.
// In production, use a proper UUID library or pass from the server.
pub fn generate_id() -> String {
  generated_id_counter.val = generated_id_counter.val + 1
  "generated-\{generated_id_counter.val}"
}

// Get current ISO timestamp string.
// Note: Placeholder - real implementation needs system clock access.
pub fn now_iso() -> String? {
  None
}
