let event_weather_created = "WeatherForecastCreated"
let event_weather_location_updated = "WeatherForecastLocationUpdated"
let event_weather_deleted = "WeatherForecastDeleted"
let event_student_created = "StudentCreated"
let event_classroom_created = "ClassRoomCreated"
let event_student_enrolled = "StudentEnrolledInClassRoom"
let event_student_dropped = "StudentDroppedFromClassRoom"
let event_user_registered = "UserRegistered"
let event_user_profile_updated = "UserProfileUpdated"
let event_user_access_granted = "UserAccessGranted"
let event_user_role_granted = "UserRoleGranted"
let event_room_created = "RoomCreated"
let event_room_updated = "RoomUpdated"
let event_reservation_draft_created = "ReservationDraftCreated"
let event_reservation_hold_committed = "ReservationHoldCommitted"
let event_reservation_confirmed = "ReservationConfirmed"
let event_reservation_cancelled = "ReservationCancelled"
let event_reservation_rejected = "ReservationRejected"
let event_approval_flow_started = "ApprovalFlowStarted"
let event_approval_decision_recorded = "ApprovalDecisionRecorded"

let query_weather_list = "GetWeatherForecastListQuery"
let query_weather_count = "GetWeatherForecastCountQuery"
let query_student_list = "GetStudentListQuery"
let query_classroom_list = "GetClassRoomListQuery"
let query_user_directory_list = "GetUserDirectoryListQuery"
let query_user_access_list = "GetUserAccessListQuery"
let query_room_list = "GetRoomListQuery"
let query_reservation_list = "GetReservationListQuery"
let query_approval_inbox = "GetApprovalInboxQuery"

let tag_group_weather = "weather"
let tag_group_student = "Student"
let tag_group_classroom = "ClassRoom"
let tag_group_user = "User"
let tag_group_user_access = "UserAccess"
let tag_group_room = "Room"
let tag_group_reservation = "Reservation"
let tag_group_approval_request = "ApprovalRequest"

struct EventPayload {
  eventType : String
  payload : String
} derive(ToJson, FromJson, Show, Eq)

struct CommandOutput {
  events : Array[EventPayload]
  tags : Array[String]
  consistencyTags : Array[String]
  expectedVersions : Map[String, Int]
} derive(ToJson, FromJson, Show, Eq)

struct WasmResult {
  ok : Bool
  status : Int
  error : String?
  message : String?
  output : CommandOutput?
} derive(ToJson, FromJson, Show, Eq)

struct WasmEventMetadata {
  tags : Array[String]?
  sortableUniqueId : String?
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastCreated {
  forecastId : String
  location : String
  date : String
  temperatureC : Int
  summary : String
  createdAt : String
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastLocationUpdated {
  forecastId : String
  newLocation : String
  updatedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastDeleted {
  forecastId : String
  deletedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct StudentCreated {
  studentId : String
  name : String
  maxClassCount : Int
} derive(ToJson, FromJson, Show, Eq)

struct ClassRoomCreated {
  classRoomId : String
  name : String
  maxStudents : Int
} derive(ToJson, FromJson, Show, Eq)

struct StudentEnrolledInClassRoom {
  studentId : String
  classRoomId : String
} derive(ToJson, FromJson, Show, Eq)

struct StudentDroppedFromClassRoom {
  studentId : String
  classRoomId : String
} derive(ToJson, FromJson, Show, Eq)

struct UserRegistered {
  userId : String
  displayName : String
  email : String
  department : String?
  registeredAt : String
  monthlyReservationLimit : Int
} derive(ToJson, FromJson, Show, Eq)

struct UserProfileUpdated {
  userId : String
  displayName : String
  email : String
  department : String?
  monthlyReservationLimit : Int
} derive(ToJson, FromJson, Show, Eq)

struct UserAccessGranted {
  userId : String
  initialRole : String
  grantedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct UserRoleGranted {
  userId : String
  role : String
  grantedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct RoomCreated {
  roomId : String
  name : String
  capacity : Int
  location : String
  equipment : Array[String]
  requiresApproval : Bool
} derive(ToJson, FromJson, Show, Eq)

struct RoomUpdated {
  roomId : String
  name : String
  capacity : Int
  location : String
  equipment : Array[String]
  requiresApproval : Bool
} derive(ToJson, FromJson, Show, Eq)

struct ReservationDraftCreated {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct ReservationHoldCommitted {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
  requiresApproval : Bool
  approvalRequestId : String?
  approvalRequestComment : String?
} derive(ToJson, FromJson, Show, Eq)

struct ReservationConfirmed {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
  confirmedAt : String
  approvalRequestId : String?
  approvalRequestComment : String?
  approvalDecisionComment : String?
} derive(ToJson, FromJson, Show, Eq)

struct ReservationCancelled {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
  approvalRequestComment : String?
  reason : String
  cancelledAt : String
} derive(ToJson, FromJson, Show, Eq)

struct ReservationRejected {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
  approvalRequestId : String
  approvalRequestComment : String?
  reason : String
  rejectedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct ApprovalFlowStarted {
  approvalRequestId : String
  reservationId : String
  roomId : String
  requesterId : String
  approverIds : Array[String]
  requestedAt : String
  requestComment : String?
} derive(ToJson, FromJson, Show, Eq)

struct ApprovalDecisionRecorded {
  approvalRequestId : String
  reservationId : String
  approverId : String
  decision : String
  comment : String?
  decidedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastState {
  forecastId : String
  location : String
  date : String
  temperatureC : Int
  summary : String
  createdAt : String
  isDeleted : Bool
  deletedAt : String?
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastItem {
  forecastId : String
  location : String
  date : String
  temperatureC : Int
  summary : String
} derive(ToJson, FromJson, Show, Eq)

struct WeatherForecastListState {
  items : Map[String, WeatherForecastItem]
} derive(ToJson, FromJson, Show, Eq)

struct StudentState {
  studentId : String
  name : String
  maxClassCount : Int
  enrolledClassRoomIds : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct StudentListState {
  items : Map[String, StudentState]
} derive(ToJson, FromJson, Show, Eq)

struct ClassRoomState {
  classRoomId : String
  name : String
  maxStudents : Int
  enrolledStudentIds : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct ClassRoomItem {
  classRoomId : String
  name : String
  maxStudents : Int
  enrolledCount : Int
  isFull : Bool
  remainingCapacity : Int
} derive(ToJson, FromJson, Show, Eq)

struct ClassRoomListState {
  items : Map[String, ClassRoomItem]
} derive(ToJson, FromJson, Show, Eq)

struct UserDirectoryState {
  userId : String
  displayName : String
  email : String
  department : String?
  registeredAt : String
  monthlyReservationLimit : Int
  externalProviders : Array[String]
  isActive : Bool
} derive(ToJson, FromJson, Show, Eq)

struct UserDirectoryListItem {
  userId : String
  displayName : String
  email : String
  department : String?
  isActive : Bool
  registeredAt : String
  monthlyReservationLimit : Int
  externalProviders : Array[String]
  roles : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct UserDirectoryListState {
  items : Map[String, UserDirectoryListItem]
} derive(ToJson, FromJson, Show, Eq)

struct UserAccessState {
  userId : String
  roles : Array[String]
  grantedAt : String
  isActive : Bool
} derive(ToJson, FromJson, Show, Eq)

struct UserAccessListItem {
  userId : String
  roles : Array[String]
  isActive : Bool
  grantedAt : String
} derive(ToJson, FromJson, Show, Eq)

struct UserAccessListState {
  items : Map[String, UserAccessListItem]
} derive(ToJson, FromJson, Show, Eq)

struct RoomState {
  roomId : String
  name : String
  capacity : Int
  location : String
  equipment : Array[String]
  requiresApproval : Bool
  isActive : Bool
} derive(ToJson, FromJson, Show, Eq)

struct RoomListItem {
  roomId : String
  name : String
  capacity : Int
  location : String
  equipment : Array[String]
  requiresApproval : Bool
  isActive : Bool
} derive(ToJson, FromJson, Show, Eq)

struct RoomListState {
  items : Map[String, RoomListItem]
} derive(ToJson, FromJson, Show, Eq)

struct ReservationState {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
  status : String
  requiresApproval : Bool
  approvalRequestId : String?
  approvalRequestComment : String?
  approvalDecisionComment : String?
  confirmedAt : String?
  reason : String?
} derive(ToJson, FromJson, Show, Eq)

struct ReservationListItem {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
  status : String
  requiresApproval : Bool
  approvalRequestId : String?
  approvalRequestComment : String?
  approvalDecisionComment : String?
} derive(ToJson, FromJson, Show, Eq)

struct ReservationListState {
  items : Map[String, ReservationListItem]
} derive(ToJson, FromJson, Show, Eq)

struct ApprovalRequestState {
  approvalRequestId : String
  reservationId : String
  roomId : String
  requesterId : String
  approverIds : Array[String]
  requestedAt : String
  requestComment : String?
  status : String
} derive(ToJson, FromJson, Show, Eq)

struct ApprovalInboxItem {
  approvalRequestId : String
  reservationId : String
  roomId : String
  requesterId : String
  requestComment : String?
  approverIds : Array[String]
  requestedAt : String
  status : String
} derive(ToJson, FromJson, Show, Eq)

struct ApprovalInboxState {
  items : Map[String, ApprovalInboxItem]
} derive(ToJson, FromJson, Show, Eq)

struct CountResult {
  count : Int
} derive(ToJson, FromJson, Show, Eq)

struct WeatherListQuery {
  locationFilter : String?
  forecastId : String?
  waitForSortableUniqueId : String?
  pageNumber : Int?
  pageSize : Int?
} derive(ToJson, FromJson, Show, Eq)

struct WeatherCountQuery {
  locationFilter : String?
  forecastId : String?
  waitForSortableUniqueId : String?
} derive(ToJson, FromJson, Show, Eq)

struct PagingQuery {
  pageNumber : Int?
  pageSize : Int?
  waitForSortableUniqueId : String?
} derive(ToJson, FromJson, Show, Eq)

struct ReservationListQuery {
  pageNumber : Int?
  pageSize : Int?
  waitForSortableUniqueId : String?
  roomId : String?
} derive(ToJson, FromJson, Show, Eq)

struct ApprovalListQuery {
  pageNumber : Int?
  pageSize : Int?
  waitForSortableUniqueId : String?
  pendingOnly : Bool
} derive(ToJson, FromJson, Show, Eq)

struct UserDirectoryListQuery {
  pageNumber : Int?
  pageSize : Int?
  waitForSortableUniqueId : String?
  activeOnly : Bool
} derive(ToJson, FromJson, Show, Eq)

struct UserAccessListQuery {
  pageNumber : Int?
  pageSize : Int?
  waitForSortableUniqueId : String?
  activeOnly : Bool
  roleFilter : String?
} derive(ToJson, FromJson, Show, Eq)

struct CreateWeatherReq {
  forecastId : String
  location : String
  date : String
  temperatureC : Int
  summary : String?
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct UpdateLocationReq {
  forecastId : String
  newLocation : String
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct DeleteWeatherReq {
  forecastId : String
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct CreateStudentReq {
  studentId : String
  name : String
  maxClassCount : Int
} derive(ToJson, FromJson, Show, Eq)

struct CreateClassRoomReq {
  classRoomId : String
  name : String
  maxStudents : Int
} derive(ToJson, FromJson, Show, Eq)

struct EnrollmentReq {
  studentId : String
  classRoomId : String
} derive(ToJson, FromJson, Show, Eq)

struct RegisterUserReq {
  userId : String
  displayName : String
  email : String
  department : String?
  monthlyReservationLimit : Int
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct UpdateUserMonthlyLimitReq {
  userId : String
  monthlyReservationLimit : Int
} derive(ToJson, FromJson, Show, Eq)

struct GrantUserAccessReq {
  userId : String
  initialRole : String
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct GrantUserRoleReq {
  userId : String
  role : String
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct CreateRoomReq {
  roomId : String
  name : String
  capacity : Int
  location : String
  equipment : Array[String]
  requiresApproval : Bool
} derive(ToJson, FromJson, Show, Eq)

struct UpdateRoomReq {
  roomId : String
  name : String
  capacity : Int
  location : String
  equipment : Array[String]
  requiresApproval : Bool
} derive(ToJson, FromJson, Show, Eq)

struct CreateReservationDraftReq {
  reservationId : String
  roomId : String
  organizerId : String
  organizerName : String
  startTime : String
  endTime : String
  purpose : String
  selectedEquipment : Array[String]
} derive(ToJson, FromJson, Show, Eq)

struct CommitReservationHoldReq {
  reservationId : String
  roomId : String
  requiresApproval : Bool
  approvalRequestId : String?
  approvalRequestComment : String?
} derive(ToJson, FromJson, Show, Eq)

struct ConfirmReservationReq {
  reservationId : String
  roomId : String
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct CancelReservationReq {
  reservationId : String
  roomId : String
  reason : String
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct RejectReservationReq {
  reservationId : String
  roomId : String
  approvalRequestId : String
  reason : String
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct StartApprovalFlowReq {
  approvalRequestId : String
  reservationId : String
  roomId : String
  requesterId : String
  approverIds : Array[String]
  requestComment : String?
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

struct RecordApprovalDecisionReq {
  approvalRequestId : String
  reservationId : String
  approverId : String
  decision : String
  comment : String?
  nowIso : String
} derive(ToJson, FromJson, Show, Eq)

fn is_empty_json(s : String) -> Bool {
  let t = s.trim()
  t == "" || t == "{}" || t == "null"
}

fn json_stringify(j : Json) -> String {
  j.stringify()
}

fn[T : @json.FromJson] parse_json(s : String, fallback : T) -> T {
  if is_empty_json(s) {
    return fallback
  }
  try {
    let j = @json.parse(s[:])
    let v : T = @json.from_json(j)
    v
  } catch {
    _ => fallback
  }
}

fn contains(items : Array[String], v : String) -> Bool {
  for x in items {
    if x == v {
      return true
    }
  }
  false
}

fn remove(items : Array[String], v : String) -> Array[String] {
  let out : Array[String] = []
  for x in items {
    if x != v {
      out.push(x)
    }
  }
  out
}

fn add_unique(items : Array[String], v : String) -> Array[String] {
  if contains(items, v) {
    return items
  }
  let out : Array[String] = []
  for x in items {
    out.push(x)
  }
  out.push(v)
  out
}

fn option_string(value : String?) -> String {
  match value {
    None => ""
    Some(v) => v
  }
}

fn tag_string(tag_group : String, tag_id : String) -> String {
  tag_group + ":" + tag_id
}

fn empty_weather_state() -> WeatherForecastState {
  {
    forecastId: "",
    location: "",
    date: "",
    temperatureC: 0,
    summary: "",
    createdAt: "",
    isDeleted: false,
    deletedAt: None,
  }
}

fn empty_weather_list_state() -> WeatherForecastListState {
  { items: Map::new() }
}

fn empty_student_state() -> StudentState {
  { studentId: "", name: "", maxClassCount: 0, enrolledClassRoomIds: [] }
}

fn empty_student_list_state() -> StudentListState {
  { items: Map::new() }
}

fn empty_classroom_state() -> ClassRoomState {
  { classRoomId: "", name: "", maxStudents: 0, enrolledStudentIds: [] }
}

fn empty_classroom_list_state() -> ClassRoomListState {
  { items: Map::new() }
}

fn empty_user_directory_state() -> UserDirectoryState {
  {
    userId: "",
    displayName: "",
    email: "",
    department: None,
    registeredAt: "",
    monthlyReservationLimit: 0,
    externalProviders: [],
    isActive: false,
  }
}

fn empty_user_directory_list_state() -> UserDirectoryListState {
  { items: Map::new() }
}

fn empty_user_access_state() -> UserAccessState {
  { userId: "", roles: [], grantedAt: "", isActive: false }
}

fn empty_user_access_list_state() -> UserAccessListState {
  { items: Map::new() }
}

fn empty_room_state() -> RoomState {
  {
    roomId: "",
    name: "",
    capacity: 0,
    location: "",
    equipment: [],
    requiresApproval: false,
    isActive: false,
  }
}

fn empty_room_list_state() -> RoomListState {
  { items: Map::new() }
}

fn empty_reservation_state() -> ReservationState {
  {
    reservationId: "",
    roomId: "",
    organizerId: "",
    organizerName: "",
    startTime: "",
    endTime: "",
    purpose: "",
    selectedEquipment: [],
    status: "",
    requiresApproval: false,
    approvalRequestId: None,
    approvalRequestComment: None,
    approvalDecisionComment: None,
    confirmedAt: None,
    reason: None,
  }
}

fn empty_reservation_list_state() -> ReservationListState {
  { items: Map::new() }
}

fn empty_approval_request_state() -> ApprovalRequestState {
  {
    approvalRequestId: "",
    reservationId: "",
    roomId: "",
    requesterId: "",
    approverIds: [],
    requestedAt: "",
    requestComment: None,
    status: "",
  }
}

fn empty_approval_inbox_state() -> ApprovalInboxState {
  { items: Map::new() }
}

fn ok(output : CommandOutput) -> WasmResult {
  { ok: true, status: 200, error: None, message: None, output: Some(output) }
}

fn ok_noop() -> WasmResult {
  ok({ events: [], tags: [], consistencyTags: [], expectedVersions: Map::new() })
}

fn err(status : Int, code : String, message : String) -> WasmResult {
  { ok: false, status, error: Some(code), message: Some(message), output: None }
}

fn new_command_output(event_type : String, payload_json : String, tags : Array[String]) -> CommandOutput {
  {
    events: [{ eventType: event_type, payload: payload_json }],
    tags,
    consistencyTags: tags,
    expectedVersions: Map::new(),
  }
}
