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
pub fn[T : @json.FromJson] parse_request_body(body : String, fallback : T) -> T {
  if body == "" {
    return fallback
  }
  try {
    let j = @json.parse(body[:])
    let v : T = @json.from_json(j)
    v
  } catch {
    _ => fallback
  }
}

// Normalize a UUID-style ID (trim whitespace, lowercase).
pub fn normalize_id(id : String) -> String {
  id.trim().to_lower().to_string()
}

// Generate a simple UUID v4-like string.
// Note: MoonBit may not have crypto-random, so this is a placeholder.
// In production, use a proper UUID library or pass from the server.
pub fn generate_id() -> String {
  // Placeholder - real implementation needs random bytes
  "00000000-0000-0000-0000-000000000000"
}

// Get current ISO timestamp string.
// Note: Placeholder - real implementation needs system clock access.
pub fn now_iso() -> String {
  // Placeholder - real implementation needs @time or system call
  "1970-01-01T00:00:00.000Z"
}
