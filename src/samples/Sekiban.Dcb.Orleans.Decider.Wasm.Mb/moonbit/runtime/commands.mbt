fn handle_create_weather(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_weather_state())
  if state.forecastId != "" {
    return err(409, "AlreadyExists", "forecast already exists")
  }

  let fallback : CreateWeatherReq = {
    forecastId: "",
    location: "",
    date: "",
    temperatureC: 0,
    summary: None,
    nowIso: "",
  }
  let req = parse_json(req_json, fallback)
  if req.forecastId == "" {
    return err(400, "Validation", "forecastId is required")
  }

  let ev : WeatherForecastCreated = {
    forecastId: req.forecastId,
    location: req.location,
    date: req.date,
    temperatureC: req.temperatureC,
    summary: weather_summary(req.summary),
    createdAt: req.nowIso,
  }
  ok(new_command_output(event_weather_created, json_stringify(ev.to_json()), [tag_string(tag_group_weather, req.forecastId)]))
}

fn handle_update_weather_location(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_weather_state())
  if state.forecastId == "" {
    return err(404, "NotFound", "forecast not found")
  }
  if state.isDeleted {
    return err(404, "Deleted", "forecast already deleted")
  }

  let fallback : UpdateLocationReq = { forecastId: "", newLocation: "", nowIso: "" }
  let req = parse_json(req_json, fallback)
  if req.forecastId == "" {
    return err(400, "Validation", "forecastId is required")
  }
  if state.location == req.newLocation {
    return ok_noop()
  }

  let ev : WeatherForecastLocationUpdated = {
    forecastId: req.forecastId,
    newLocation: req.newLocation,
    updatedAt: req.nowIso,
  }
  ok(new_command_output(event_weather_location_updated, json_stringify(ev.to_json()), [tag_string(tag_group_weather, req.forecastId)]))
}

fn handle_delete_weather(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_weather_state())
  if state.forecastId == "" {
    return err(404, "NotFound", "forecast not found")
  }
  if state.isDeleted {
    return ok_noop()
  }

  let fallback : DeleteWeatherReq = { forecastId: "", nowIso: "" }
  let req = parse_json(req_json, fallback)
  if req.forecastId == "" {
    return err(400, "Validation", "forecastId is required")
  }

  let ev : WeatherForecastDeleted = { forecastId: req.forecastId, deletedAt: req.nowIso }
  ok(new_command_output(event_weather_deleted, json_stringify(ev.to_json()), [tag_string(tag_group_weather, req.forecastId)]))
}

fn handle_create_student(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_student_state())
  if state.studentId != "" {
    return err(409, "AlreadyExists", "student already exists")
  }

  let fallback : CreateStudentReq = { studentId: "", name: "", maxClassCount: 0 }
  let req = parse_json(req_json, fallback)
  if req.studentId == "" {
    return err(400, "Validation", "studentId is required")
  }

  let ev : StudentCreated = {
    studentId: req.studentId,
    name: req.name,
    maxClassCount: if req.maxClassCount > 0 { req.maxClassCount } else { 1 },
  }
  ok(new_command_output(event_student_created, json_stringify(ev.to_json()), [tag_string(tag_group_student, req.studentId)]))
}

fn handle_create_classroom(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_classroom_state())
  if state.classRoomId != "" {
    return err(409, "AlreadyExists", "classroom already exists")
  }

  let fallback : CreateClassRoomReq = { classRoomId: "", name: "", maxStudents: 0 }
  let req = parse_json(req_json, fallback)
  if req.classRoomId == "" {
    return err(400, "Validation", "classRoomId is required")
  }

  let ev : ClassRoomCreated = {
    classRoomId: req.classRoomId,
    name: req.name,
    maxStudents: if req.maxStudents > 0 { req.maxStudents } else { 1 },
  }
  ok(new_command_output(event_classroom_created, json_stringify(ev.to_json()), [tag_string(tag_group_classroom, req.classRoomId)]))
}

fn handle_enroll(
  student_state_json : String,
  _student_version : Int,
  class_state_json : String,
  _class_version : Int,
  req_json : String
) -> WasmResult {
  let student = parse_json(student_state_json, empty_student_state())
  let class_room = parse_json(class_state_json, empty_classroom_state())
  let fallback : EnrollmentReq = { studentId: "", classRoomId: "" }
  let req = parse_json(req_json, fallback)

  if req.studentId == "" || req.classRoomId == "" {
    return err(400, "Validation", "studentId and classRoomId are required")
  }
  if student.studentId == "" {
    return err(404, "NotFound", "student not found")
  }
  if class_room.classRoomId == "" {
    return err(404, "NotFound", "classroom not found")
  }
  if contains(student.enrolledClassRoomIds, req.classRoomId) || contains(class_room.enrolledStudentIds, req.studentId) {
    return ok_noop()
  }
  if student.maxClassCount > 0 && student.enrolledClassRoomIds.length() >= student.maxClassCount {
    return err(400, "Validation", "Student reached max class count")
  }
  if class_room.maxStudents > 0 && class_room.enrolledStudentIds.length() >= class_room.maxStudents {
    return err(400, "Validation", "Class room is full")
  }

  let ev : StudentEnrolledInClassRoom = { studentId: req.studentId, classRoomId: req.classRoomId }
  ok(new_command_output(
    event_student_enrolled,
    json_stringify(ev.to_json()),
    [tag_string(tag_group_student, req.studentId), tag_string(tag_group_classroom, req.classRoomId)]))
}

fn handle_drop(
  student_state_json : String,
  _student_version : Int,
  class_state_json : String,
  _class_version : Int,
  req_json : String
) -> WasmResult {
  let student = parse_json(student_state_json, empty_student_state())
  let class_room = parse_json(class_state_json, empty_classroom_state())
  let fallback : EnrollmentReq = { studentId: "", classRoomId: "" }
  let req = parse_json(req_json, fallback)

  if req.studentId == "" || req.classRoomId == "" {
    return err(400, "Validation", "studentId and classRoomId are required")
  }
  if student.studentId == "" || class_room.classRoomId == "" {
    return err(404, "NotFound", "Enrollment not found")
  }
  if !contains(student.enrolledClassRoomIds, req.classRoomId) {
    return ok_noop()
  }

  let ev : StudentDroppedFromClassRoom = { studentId: req.studentId, classRoomId: req.classRoomId }
  ok(new_command_output(
    event_student_dropped,
    json_stringify(ev.to_json()),
    [tag_string(tag_group_student, req.studentId), tag_string(tag_group_classroom, req.classRoomId)]))
}

fn handle_register_user(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_user_directory_state())
  if state.userId != "" {
    return err(409, "AlreadyExists", "user already exists")
  }

  let fallback : RegisterUserReq = {
    userId: "",
    displayName: "",
    email: "",
    department: None,
    monthlyReservationLimit: 1,
    nowIso: "",
  }
  let req = parse_json(req_json, fallback)
  if req.userId == "" {
    return err(400, "Validation", "userId is required")
  }

  let ev : UserRegistered = {
    userId: req.userId,
    displayName: req.displayName,
    email: req.email,
    department: req.department,
    registeredAt: req.nowIso,
    monthlyReservationLimit: if req.monthlyReservationLimit > 0 { req.monthlyReservationLimit } else { 1 },
  }
  ok(new_command_output(event_user_registered, json_stringify(ev.to_json()), [tag_string(tag_group_user, req.userId)]))
}

fn handle_update_user_monthly_limit(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_user_directory_state())
  if state.userId == "" || !state.isActive {
    return err(404, "NotFound", "user not found")
  }

  let fallback : UpdateUserMonthlyLimitReq = { userId: "", monthlyReservationLimit: 1 }
  let req = parse_json(req_json, fallback)
  if req.userId == "" {
    return err(400, "Validation", "userId is required")
  }

  let ev : UserProfileUpdated = {
    userId: req.userId,
    displayName: state.displayName,
    email: state.email,
    department: state.department,
    monthlyReservationLimit: if req.monthlyReservationLimit > 0 { req.monthlyReservationLimit } else { 1 },
  }
  ok(new_command_output(event_user_profile_updated, json_stringify(ev.to_json()), [tag_string(tag_group_user, req.userId)]))
}

fn handle_grant_user_access(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_user_access_state())
  if state.userId != "" {
    return err(409, "AlreadyExists", "user access already exists")
  }

  let fallback : GrantUserAccessReq = { userId: "", initialRole: "", nowIso: "" }
  let req = parse_json(req_json, fallback)
  if req.userId == "" {
    return err(400, "Validation", "userId is required")
  }

  let ev : UserAccessGranted = {
    userId: req.userId,
    initialRole: req.initialRole,
    grantedAt: req.nowIso,
  }
  ok(new_command_output(event_user_access_granted, json_stringify(ev.to_json()), [tag_string(tag_group_user_access, req.userId)]))
}

fn handle_grant_user_role(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_user_access_state())
  if state.userId == "" || !state.isActive {
    return err(404, "NotFound", "user access not found")
  }

  let fallback : GrantUserRoleReq = { userId: "", role: "", nowIso: "" }
  let req = parse_json(req_json, fallback)
  if req.userId == "" {
    return err(400, "Validation", "userId is required")
  }
  if contains(state.roles, req.role) {
    return ok_noop()
  }

  let ev : UserRoleGranted = {
    userId: req.userId,
    role: req.role,
    grantedAt: req.nowIso,
  }
  ok(new_command_output(event_user_role_granted, json_stringify(ev.to_json()), [tag_string(tag_group_user_access, req.userId)]))
}

fn handle_create_room(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_room_state())
  if state.roomId != "" {
    return err(409, "AlreadyExists", "room already exists")
  }

  let fallback : CreateRoomReq = {
    roomId: "",
    name: "",
    capacity: 0,
    location: "",
    equipment: [],
    requiresApproval: false,
  }
  let req = parse_json(req_json, fallback)
  if req.roomId == "" {
    return err(400, "Validation", "roomId is required")
  }

  let ev : RoomCreated = {
    roomId: req.roomId,
    name: req.name,
    capacity: req.capacity,
    location: req.location,
    equipment: req.equipment,
    requiresApproval: req.requiresApproval,
  }
  ok(new_command_output(event_room_created, json_stringify(ev.to_json()), [tag_string(tag_group_room, req.roomId)]))
}

fn handle_update_room(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_room_state())
  if state.roomId == "" {
    return err(404, "NotFound", "room not found")
  }

  let fallback : UpdateRoomReq = {
    roomId: "",
    name: "",
    capacity: 0,
    location: "",
    equipment: [],
    requiresApproval: false,
  }
  let req = parse_json(req_json, fallback)
  if req.roomId == "" {
    return err(400, "Validation", "roomId is required")
  }

  let ev : RoomUpdated = {
    roomId: req.roomId,
    name: req.name,
    capacity: req.capacity,
    location: req.location,
    equipment: req.equipment,
    requiresApproval: req.requiresApproval,
  }
  ok(new_command_output(event_room_updated, json_stringify(ev.to_json()), [tag_string(tag_group_room, req.roomId)]))
}

fn handle_create_reservation_draft(
  reservation_state_json : String,
  _reservation_version : Int,
  room_state_json : String,
  _room_version : Int,
  req_json : String
) -> WasmResult {
  let reservation = parse_json(reservation_state_json, empty_reservation_state())
  if reservation.reservationId != "" {
    return err(409, "AlreadyExists", "reservation already exists")
  }
  let room = parse_json(room_state_json, empty_room_state())
  if room.roomId == "" {
    return err(404, "NotFound", "room not found")
  }

  let fallback : CreateReservationDraftReq = {
    reservationId: "",
    roomId: "",
    organizerId: "",
    organizerName: "",
    startTime: "",
    endTime: "",
    purpose: "",
    selectedEquipment: [],
  }
  let req = parse_json(req_json, fallback)
  if req.reservationId == "" || req.roomId == "" {
    return err(400, "Validation", "reservationId and roomId are required")
  }

  let ev : ReservationDraftCreated = {
    reservationId: req.reservationId,
    roomId: req.roomId,
    organizerId: req.organizerId,
    organizerName: req.organizerName,
    startTime: req.startTime,
    endTime: req.endTime,
    purpose: req.purpose,
    selectedEquipment: req.selectedEquipment,
  }
  ok(new_command_output(event_reservation_draft_created, json_stringify(ev.to_json()), [tag_string(tag_group_reservation, req.reservationId)]))
}

fn handle_create_quick_reservation(
  reservation_state_json : String,
  _reservation_version : Int,
  room_state_json : String,
  _room_version : Int,
  room_reservations_state_json : String,
  room_reservations_version : Int,
  req_json : String
) -> WasmResult {
  let reservation = parse_json(reservation_state_json, empty_reservation_state())
  if reservation.reservationId != "" {
    return err(409, "AlreadyExists", "reservation already exists")
  }
  let room = parse_json(room_state_json, empty_room_state())
  if room.roomId == "" {
    return err(404, "NotFound", "room not found")
  }

  let fallback : CreateQuickReservationReq = {
    reservationId: "",
    roomId: "",
    organizerId: "",
    organizerName: "",
    startTime: "",
    endTime: "",
    purpose: "",
    approvalRequestComment: None,
    selectedEquipment: [],
  }
  let req = parse_json(req_json, fallback)
  if req.reservationId == "" || req.roomId == "" {
    return err(400, "Validation", "reservationId and roomId are required")
  }
  if req.startTime >= req.endTime {
    return err(400, "Validation", "startTime must be before endTime")
  }

  let empty_room_reservations : RoomReservationsState = { activeReservations: Map::new() }
  let room_reservations = parse_json(room_reservations_state_json, empty_room_reservations)
  for kv in room_reservations.activeReservations.iter() {
    let (_, slot) = kv
    if req.startTime < slot.endTime && slot.startTime < req.endTime {
      return err(400, "Validation", "Reservation time conflicts with another held or confirmed reservation")
    }
  }

  let draft_ev : ReservationDraftCreated = {
    reservationId: req.reservationId,
    roomId: req.roomId,
    organizerId: req.organizerId,
    organizerName: req.organizerName,
    startTime: req.startTime,
    endTime: req.endTime,
    purpose: req.purpose,
    selectedEquipment: req.selectedEquipment,
  }
  let hold_ev : ReservationHoldCommitted = {
    reservationId: req.reservationId,
    roomId: req.roomId,
    organizerId: req.organizerId,
    organizerName: req.organizerName,
    startTime: req.startTime,
    endTime: req.endTime,
    purpose: req.purpose,
    selectedEquipment: req.selectedEquipment,
    requiresApproval: room.requiresApproval,
    approvalRequestId: None,
    approvalRequestComment: req.approvalRequestComment,
  }

  let reservation_tag = tag_string(tag_group_reservation, req.reservationId)
  let room_reservation_tag = tag_string(tag_group_room_reservation, req.roomId)
  let tags = [reservation_tag, room_reservation_tag]
  let event_types = [event_reservation_draft_created, event_reservation_hold_committed]
  let payload_jsons = [json_stringify(draft_ev.to_json()), json_stringify(hold_ev.to_json())]

  if !room.requiresApproval {
    let confirm_ev : ReservationConfirmed = {
      reservationId: req.reservationId,
      roomId: req.roomId,
      organizerId: req.organizerId,
      organizerName: req.organizerName,
      startTime: req.startTime,
      endTime: req.endTime,
      purpose: req.purpose,
      selectedEquipment: req.selectedEquipment,
      confirmedAt: req.startTime,
      approvalRequestId: None,
      approvalRequestComment: None,
      approvalDecisionComment: None,
    }
    event_types.push(event_reservation_confirmed)
    payload_jsons.push(json_stringify(confirm_ev.to_json()))
  }

  let expected_versions : Map[String, Int] = Map::new()
  expected_versions[room_reservation_tag] = room_reservations_version
  ok(new_multi_json_command_output(
    event_types,
    payload_jsons,
    tags,
    tags,
    expected_versions))
}

fn handle_commit_reservation_hold(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_reservation_state())
  if state.reservationId == "" {
    return err(404, "NotFound", "reservation not found")
  }
  if state.status != "Draft" {
    return ok_noop()
  }

  let fallback : CommitReservationHoldReq = {
    reservationId: "",
    roomId: "",
    requiresApproval: false,
    approvalRequestId: None,
    approvalRequestComment: None,
  }
  let req = parse_json(req_json, fallback)
  let ev : ReservationHoldCommitted = {
    reservationId: state.reservationId,
    roomId: req.roomId,
    organizerId: state.organizerId,
    organizerName: state.organizerName,
    startTime: state.startTime,
    endTime: state.endTime,
    purpose: state.purpose,
    selectedEquipment: state.selectedEquipment,
    requiresApproval: req.requiresApproval,
    approvalRequestId: req.approvalRequestId,
    approvalRequestComment: req.approvalRequestComment,
  }
  ok(new_command_output(event_reservation_hold_committed, json_stringify(ev.to_json()), [tag_string(tag_group_reservation, state.reservationId)]))
}

fn handle_confirm_reservation(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_reservation_state())
  if state.reservationId == "" {
    return err(404, "NotFound", "reservation not found")
  }
  if state.status == "Confirmed" {
    return ok_noop()
  }

  let fallback : ConfirmReservationReq = { reservationId: "", roomId: "", nowIso: "" }
  let req = parse_json(req_json, fallback)
  let ev : ReservationConfirmed = {
    reservationId: state.reservationId,
    roomId: req.roomId,
    organizerId: state.organizerId,
    organizerName: state.organizerName,
    startTime: state.startTime,
    endTime: state.endTime,
    purpose: state.purpose,
    selectedEquipment: state.selectedEquipment,
    confirmedAt: req.nowIso,
    approvalRequestId: state.approvalRequestId,
    approvalRequestComment: state.approvalRequestComment,
    approvalDecisionComment: state.approvalDecisionComment,
  }
  ok(new_command_output(event_reservation_confirmed, json_stringify(ev.to_json()), [tag_string(tag_group_reservation, state.reservationId)]))
}

fn handle_cancel_reservation(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_reservation_state())
  if state.reservationId == "" {
    return err(404, "NotFound", "reservation not found")
  }

  let fallback : CancelReservationReq = { reservationId: "", roomId: "", reason: "", nowIso: "" }
  let req = parse_json(req_json, fallback)
  let ev : ReservationCancelled = {
    reservationId: state.reservationId,
    roomId: req.roomId,
    organizerId: state.organizerId,
    organizerName: state.organizerName,
    startTime: state.startTime,
    endTime: state.endTime,
    purpose: state.purpose,
    selectedEquipment: state.selectedEquipment,
    approvalRequestComment: state.approvalRequestComment,
    reason: req.reason,
    cancelledAt: req.nowIso,
  }
  ok(new_command_output(event_reservation_cancelled, json_stringify(ev.to_json()), [tag_string(tag_group_reservation, state.reservationId)]))
}

fn handle_reject_reservation(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_reservation_state())
  if state.reservationId == "" {
    return err(404, "NotFound", "reservation not found")
  }

  let fallback : RejectReservationReq = {
    reservationId: "",
    roomId: "",
    approvalRequestId: "",
    reason: "",
    nowIso: "",
  }
  let req = parse_json(req_json, fallback)
  let ev : ReservationRejected = {
    reservationId: state.reservationId,
    roomId: req.roomId,
    organizerId: state.organizerId,
    organizerName: state.organizerName,
    startTime: state.startTime,
    endTime: state.endTime,
    purpose: state.purpose,
    selectedEquipment: state.selectedEquipment,
    approvalRequestId: req.approvalRequestId,
    approvalRequestComment: state.approvalRequestComment,
    reason: req.reason,
    rejectedAt: req.nowIso,
  }
  ok(new_command_output(event_reservation_rejected, json_stringify(ev.to_json()), [tag_string(tag_group_reservation, state.reservationId)]))
}

fn handle_start_approval_flow(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_approval_request_state())
  if state.approvalRequestId != "" {
    return err(409, "AlreadyExists", "approval request already exists")
  }

  let fallback : StartApprovalFlowReq = {
    approvalRequestId: "",
    reservationId: "",
    roomId: "",
    requesterId: "",
    approverIds: [],
    requestComment: None,
    nowIso: "",
  }
  let req = parse_json(req_json, fallback)
  let ev : ApprovalFlowStarted = {
    approvalRequestId: req.approvalRequestId,
    reservationId: req.reservationId,
    roomId: req.roomId,
    requesterId: req.requesterId,
    approverIds: req.approverIds,
    requestedAt: req.nowIso,
    requestComment: req.requestComment,
  }
  ok(new_command_output(event_approval_flow_started, json_stringify(ev.to_json()), [tag_string(tag_group_approval_request, req.approvalRequestId)]))
}

fn handle_record_approval_decision(state_json : String, _version : Int, req_json : String) -> WasmResult {
  let state = parse_json(state_json, empty_approval_request_state())
  if state.approvalRequestId == "" {
    return err(404, "NotFound", "approval request not found")
  }
  if state.status != "Pending" {
    return ok_noop()
  }

  let fallback : RecordApprovalDecisionReq = {
    approvalRequestId: "",
    reservationId: "",
    approverId: "",
    decision: "",
    comment: None,
    nowIso: "",
  }
  let req = parse_json(req_json, fallback)
  let ev : ApprovalDecisionRecorded = {
    approvalRequestId: req.approvalRequestId,
    reservationId: req.reservationId,
    approverId: req.approverId,
    decision: req.decision,
    comment: req.comment,
    decidedAt: req.nowIso,
  }
  ok(new_command_output(event_approval_decision_recorded, json_stringify(ev.to_json()), [tag_string(tag_group_approval_request, req.approvalRequestId)]))
}

pub fn cmd_create_weather(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_create_weather(state_json, version, req_json).to_json()))
}

pub fn cmd_update_weather_location(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_update_weather_location(state_json, version, req_json).to_json()))
}

pub fn cmd_delete_weather(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_delete_weather(state_json, version, req_json).to_json()))
}

pub fn cmd_create_student(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_create_student(state_json, version, req_json).to_json()))
}

pub fn cmd_create_classroom(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_create_classroom(state_json, version, req_json).to_json()))
}

pub fn cmd_enroll(
  student_state_ptr : UInt,
  student_state_len : UInt,
  student_version : Int,
  class_state_ptr : UInt,
  class_state_len : UInt,
  class_version : Int,
  req_ptr : UInt,
  req_len : UInt
) -> Int64 {
  let student_json = read_utf8(student_state_ptr.reinterpret_as_int(), student_state_len.reinterpret_as_int())
  let class_json = read_utf8(class_state_ptr.reinterpret_as_int(), class_state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_enroll(student_json, student_version, class_json, class_version, req_json).to_json()))
}

pub fn cmd_drop(
  student_state_ptr : UInt,
  student_state_len : UInt,
  student_version : Int,
  class_state_ptr : UInt,
  class_state_len : UInt,
  class_version : Int,
  req_ptr : UInt,
  req_len : UInt
) -> Int64 {
  let student_json = read_utf8(student_state_ptr.reinterpret_as_int(), student_state_len.reinterpret_as_int())
  let class_json = read_utf8(class_state_ptr.reinterpret_as_int(), class_state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_drop(student_json, student_version, class_json, class_version, req_json).to_json()))
}

pub fn cmd_register_user(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_register_user(state_json, version, req_json).to_json()))
}

pub fn cmd_update_user_monthly_limit(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_update_user_monthly_limit(state_json, version, req_json).to_json()))
}

pub fn cmd_grant_user_access(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_grant_user_access(state_json, version, req_json).to_json()))
}

pub fn cmd_grant_user_role(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_grant_user_role(state_json, version, req_json).to_json()))
}

pub fn cmd_create_room(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_create_room(state_json, version, req_json).to_json()))
}

pub fn cmd_update_room(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_update_room(state_json, version, req_json).to_json()))
}

pub fn cmd_create_reservation_draft(
  reservation_state_ptr : UInt,
  reservation_state_len : UInt,
  reservation_version : Int,
  room_state_ptr : UInt,
  room_state_len : UInt,
  room_version : Int,
  req_ptr : UInt,
  req_len : UInt
) -> Int64 {
  let reservation_json = read_utf8(reservation_state_ptr.reinterpret_as_int(), reservation_state_len.reinterpret_as_int())
  let room_json = read_utf8(room_state_ptr.reinterpret_as_int(), room_state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_create_reservation_draft(reservation_json, reservation_version, room_json, room_version, req_json).to_json()))
}

pub fn cmd_create_quick_reservation(
  reservation_state_ptr : UInt,
  reservation_state_len : UInt,
  reservation_version : Int,
  room_state_ptr : UInt,
  room_state_len : UInt,
  room_version : Int,
  room_reservations_state_ptr : UInt,
  room_reservations_state_len : UInt,
  room_reservations_version : Int,
  req_ptr : UInt,
  req_len : UInt
) -> Int64 {
  let reservation_json = read_utf8(reservation_state_ptr.reinterpret_as_int(), reservation_state_len.reinterpret_as_int())
  let room_json = read_utf8(room_state_ptr.reinterpret_as_int(), room_state_len.reinterpret_as_int())
  let room_reservations_json = read_utf8(room_reservations_state_ptr.reinterpret_as_int(), room_reservations_state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_create_quick_reservation(
    reservation_json,
    reservation_version,
    room_json,
    room_version,
    room_reservations_json,
    room_reservations_version,
    req_json).to_json()))
}

pub fn cmd_commit_reservation_hold(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_commit_reservation_hold(state_json, version, req_json).to_json()))
}

pub fn cmd_confirm_reservation(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_confirm_reservation(state_json, version, req_json).to_json()))
}

pub fn cmd_cancel_reservation(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_cancel_reservation(state_json, version, req_json).to_json()))
}

pub fn cmd_reject_reservation(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_reject_reservation(state_json, version, req_json).to_json()))
}

pub fn cmd_start_approval_flow(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_start_approval_flow(state_json, version, req_json).to_json()))
}

pub fn cmd_record_approval_decision(state_ptr : UInt, state_len : UInt, version : Int, req_ptr : UInt, req_len : UInt) -> Int64 {
  let state_json = read_utf8(state_ptr.reinterpret_as_int(), state_len.reinterpret_as_int())
  let req_json = read_utf8(req_ptr.reinterpret_as_int(), req_len.reinterpret_as_int())
  write_utf8(json_stringify(handle_record_approval_decision(state_json, version, req_json).to_json()))
}
