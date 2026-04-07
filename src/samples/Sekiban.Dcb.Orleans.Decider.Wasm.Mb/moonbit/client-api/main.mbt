// MoonBit native ClientAPI server for Sekiban DCB Decider.
// Replaces the JavaScript server.mjs with a native MoonBit HTTP server.
//
// Architecture:
//   Browser/Client -> [This Server] -> [Sekiban WASM Runtime Server (C#)]
//
// This server:
//   1. Receives domain HTTP requests (create weather, etc.)
//   2. Fetches tag state from the WASM runtime server
//   3. Calls WASM command locally (via WASM bridge) or serialized execution
//   4. Commits events back to the WASM runtime server
//   5. Returns results to the client

// Tag group constants
let tag_weather = "weather"
let tag_student = "Student"
let tag_classroom = "ClassRoom"
let tag_user = "User"
let tag_user_access = "UserAccess"
let tag_room = "Room"
let tag_reservation = "Reservation"
let tag_approval = "ApprovalRequest"

// Projector names
let proj_weather = "WeatherForecastProjector"
let proj_weather_list = "WeatherForecastMultiProjection"
let proj_student = "StudentProjector"
let proj_student_list = "StudentListProjection"
let proj_classroom = "ClassRoomProjector"
let proj_classroom_list = "ClassRoomListProjection"
let proj_user_dir = "UserDirectoryProjector"
let proj_user_dir_list = "UserDirectoryListProjection"
let proj_user_access = "UserAccessProjector"
let proj_user_access_list = "UserAccessListProjection"
let proj_room = "RoomProjector"
let proj_room_list = "RoomListProjection"
let proj_reservation = "ReservationProjector"
let proj_reservation_list = "ReservationListProjection"
let proj_approval = "ApprovalRequestProjector"
let proj_approval_list = "ApprovalRequestListProjection"

fn resolve_wasm_server_base() -> String {
  // Try environment variables in order, similar to JS version
  // For now, use a default
  "http://localhost:5000"
}

fn main {
  println("MoonBit ClientApi starting...")
  let _base_url = resolve_wasm_server_base()
  let _client = @http.SekibanRuntimeClient::new(_base_url)
  let _resolver = @executor.StaticTagProjectorResolver::new()
    .with_tag_group(tag_weather, proj_weather)
    .with_tag_group(tag_student, proj_student)
    .with_tag_group(tag_classroom, proj_classroom)
    .with_tag_group(tag_user, proj_user_dir)
    .with_tag_group(tag_user_access, proj_user_access)
    .with_tag_group(tag_room, proj_room)
    .with_tag_group(tag_reservation, proj_reservation)
    .with_tag_group(tag_approval, proj_approval)

  println("MoonBit ClientApi initialized. WASM server: \{_base_url}")
  println("Note: HTTP server startup requires moonbitlang/async runtime.")
  println("Run with: moon run --target native")
}
