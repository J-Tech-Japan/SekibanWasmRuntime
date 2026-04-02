enum Kind {
  Unknown
  WeatherTag
  StudentTag
  ClassRoomTag
  UserDirectoryTag
  UserAccessTag
  RoomTag
  ReservationTag
  ApprovalRequestTag
  WeatherList
  StudentList
  ClassRoomList
  UserDirectoryList
  UserAccessList
  RoomList
  ReservationList
  ApprovalInboxList
} derive(Show, Eq)

struct Instance {
  kind : Kind
  weatherTag : WeatherForecastState
  studentTag : StudentState
  classRoomTag : ClassRoomState
  userDirectoryTag : UserDirectoryState
  userAccessTag : UserAccessState
  roomTag : RoomState
  reservationTag : ReservationState
  approvalRequestTag : ApprovalRequestState
  weatherList : WeatherForecastListState
  studentList : StudentListState
  classRoomList : ClassRoomListState
  userDirectoryList : UserDirectoryListState
  userAccessList : UserAccessListState
  roomList : RoomListState
  reservationList : ReservationListState
  approvalInboxList : ApprovalInboxState
}

fn instance_new(kind : Kind) -> Instance {
  {
    kind,
    weatherTag: empty_weather_state(),
    studentTag: empty_student_state(),
    classRoomTag: empty_classroom_state(),
    userDirectoryTag: empty_user_directory_state(),
    userAccessTag: empty_user_access_state(),
    roomTag: empty_room_state(),
    reservationTag: empty_reservation_state(),
    approvalRequestTag: empty_approval_request_state(),
    weatherList: empty_weather_list_state(),
    studentList: empty_student_list_state(),
    classRoomList: empty_classroom_list_state(),
    userDirectoryList: empty_user_directory_list_state(),
    userAccessList: empty_user_access_list_state(),
    roomList: empty_room_list_state(),
    reservationList: empty_reservation_list_state(),
    approvalInboxList: empty_approval_inbox_state(),
  }
}

let instances : Ref[Map[UInt, Instance]] = @ref.new(Map::new())
let next_id : Ref[UInt] = @ref.new(1)

fn put_instance(id : UInt, inst : Instance) -> Unit {
  instances.val[id] = inst
}

fn get_instance(id : UInt) -> Instance? {
  instances.val.get(id)
}

fn take_next_id() -> UInt {
  let id = next_id.val
  next_id.val = id + 1
  id
}

fn resolve_kind(projector_type : String) -> Kind {
  match projector_type {
    "WeatherForecastProjector" => WeatherTag
    "StudentProjector" => StudentTag
    "ClassRoomProjector" => ClassRoomTag
    "UserDirectoryProjector" => UserDirectoryTag
    "UserAccessProjector" => UserAccessTag
    "RoomProjector" => RoomTag
    "ReservationProjector" => ReservationTag
    "ApprovalRequestProjector" => ApprovalRequestTag
    "WeatherForecastMultiProjection" => WeatherList
    "StudentListProjection" => StudentList
    "ClassRoomListProjection" => ClassRoomList
    "UserDirectoryListProjection" => UserDirectoryList
    "UserAccessListProjection" => UserAccessList
    "RoomListProjection" => RoomList
    "ReservationListProjection" => ReservationList
    "ApprovalRequestListProjection" => ApprovalInboxList
    _ => Unknown
  }
}

fn weather_summary(summary : String?) -> String {
  match summary {
    None => ""
    Some(v) => v
  }
}

fn apply_weather_tag(state : WeatherForecastState, event_type : String, payload : String) -> WeatherForecastState {
  if event_type == event_weather_created {
    let ev = parse_json(payload, WeatherForecastCreated::{
      forecastId: "",
      location: "",
      date: "",
      temperatureC: 0,
      summary: "",
      createdAt: "",
    })
    {
      forecastId: ev.forecastId,
      location: ev.location,
      date: ev.date,
      temperatureC: ev.temperatureC,
      summary: ev.summary,
      createdAt: ev.createdAt,
      isDeleted: false,
      deletedAt: None,
    }
  } else if event_type == event_weather_location_updated {
    let ev = parse_json(payload, WeatherForecastLocationUpdated::{ forecastId: "", newLocation: "", updatedAt: "" })
    { ..state, location: ev.newLocation }
  } else if event_type == event_weather_deleted {
    let ev = parse_json(payload, WeatherForecastDeleted::{ forecastId: "", deletedAt: "" })
    { ..state, isDeleted: true, deletedAt: Some(ev.deletedAt) }
  } else {
    state
  }
}

fn apply_weather_list(state : WeatherForecastListState, event_type : String, payload : String) -> WeatherForecastListState {
  let items = state.items
  if event_type == event_weather_created {
    let ev = parse_json(payload, WeatherForecastCreated::{
      forecastId: "",
      location: "",
      date: "",
      temperatureC: 0,
      summary: "",
      createdAt: "",
    })
    items[ev.forecastId] = {
      forecastId: ev.forecastId,
      location: ev.location,
      date: ev.date,
      temperatureC: ev.temperatureC,
      summary: ev.summary,
    }
  } else if event_type == event_weather_location_updated {
    let ev = parse_json(payload, WeatherForecastLocationUpdated::{ forecastId: "", newLocation: "", updatedAt: "" })
    match items.get(ev.forecastId) {
      None => ()
      Some(cur) => items[ev.forecastId] = { ..cur, location: ev.newLocation }
    }
  } else if event_type == event_weather_deleted {
    let ev = parse_json(payload, WeatherForecastDeleted::{ forecastId: "", deletedAt: "" })
    ignore(items.remove(ev.forecastId))
  }
  { items }
}

fn apply_student_tag(state : StudentState, event_type : String, payload : String) -> StudentState {
  if event_type == event_student_created {
    let ev = parse_json(payload, StudentCreated::{ studentId: "", name: "", maxClassCount: 0 })
    {
      studentId: ev.studentId,
      name: ev.name,
      maxClassCount: ev.maxClassCount,
      enrolledClassRoomIds: [],
    }
  } else if event_type == event_student_enrolled {
    let ev = parse_json(payload, StudentEnrolledInClassRoom::{ studentId: "", classRoomId: "" })
    { ..state, enrolledClassRoomIds: add_unique(state.enrolledClassRoomIds, ev.classRoomId) }
  } else if event_type == event_student_dropped {
    let ev = parse_json(payload, StudentDroppedFromClassRoom::{ studentId: "", classRoomId: "" })
    { ..state, enrolledClassRoomIds: remove(state.enrolledClassRoomIds, ev.classRoomId) }
  } else {
    state
  }
}

fn apply_student_list(state : StudentListState, event_type : String, payload : String) -> StudentListState {
  let items = state.items
  if event_type == event_student_created {
    let ev = parse_json(payload, StudentCreated::{ studentId: "", name: "", maxClassCount: 0 })
    items[ev.studentId] = {
      studentId: ev.studentId,
      name: ev.name,
      maxClassCount: ev.maxClassCount,
      enrolledClassRoomIds: [],
    }
  } else if event_type == event_student_enrolled {
    let ev = parse_json(payload, StudentEnrolledInClassRoom::{ studentId: "", classRoomId: "" })
    match items.get(ev.studentId) {
      None => ()
      Some(cur) => items[ev.studentId] = { ..cur, enrolledClassRoomIds: add_unique(cur.enrolledClassRoomIds, ev.classRoomId) }
    }
  } else if event_type == event_student_dropped {
    let ev = parse_json(payload, StudentDroppedFromClassRoom::{ studentId: "", classRoomId: "" })
    match items.get(ev.studentId) {
      None => ()
      Some(cur) => items[ev.studentId] = { ..cur, enrolledClassRoomIds: remove(cur.enrolledClassRoomIds, ev.classRoomId) }
    }
  }
  { items }
}

fn apply_classroom_tag(state : ClassRoomState, event_type : String, payload : String) -> ClassRoomState {
  if event_type == event_classroom_created {
    let ev = parse_json(payload, ClassRoomCreated::{ classRoomId: "", name: "", maxStudents: 0 })
    {
      classRoomId: ev.classRoomId,
      name: ev.name,
      maxStudents: ev.maxStudents,
      enrolledStudentIds: [],
    }
  } else if event_type == event_student_enrolled {
    let ev = parse_json(payload, StudentEnrolledInClassRoom::{ studentId: "", classRoomId: "" })
    { ..state, enrolledStudentIds: add_unique(state.enrolledStudentIds, ev.studentId) }
  } else if event_type == event_student_dropped {
    let ev = parse_json(payload, StudentDroppedFromClassRoom::{ studentId: "", classRoomId: "" })
    { ..state, enrolledStudentIds: remove(state.enrolledStudentIds, ev.studentId) }
  } else {
    state
  }
}

fn classroom_item_from_state(state : ClassRoomState) -> ClassRoomItem {
  let enrolled_count = state.enrolledStudentIds.length()
  let remaining_capacity = state.maxStudents - enrolled_count
  {
    classRoomId: state.classRoomId,
    name: state.name,
    maxStudents: state.maxStudents,
    enrolledCount: enrolled_count,
    isFull: state.maxStudents > 0 && enrolled_count >= state.maxStudents,
    remainingCapacity: if remaining_capacity > 0 { remaining_capacity } else { 0 },
  }
}

fn apply_classroom_list(state : ClassRoomListState, event_type : String, payload : String) -> ClassRoomListState {
  let items = state.items
  if event_type == event_classroom_created {
    let ev = parse_json(payload, ClassRoomCreated::{ classRoomId: "", name: "", maxStudents: 0 })
    items[ev.classRoomId] = {
      classRoomId: ev.classRoomId,
      name: ev.name,
      maxStudents: ev.maxStudents,
      enrolledCount: 0,
      isFull: false,
      remainingCapacity: ev.maxStudents,
    }
  } else if event_type == event_student_enrolled {
    let ev = parse_json(payload, StudentEnrolledInClassRoom::{ studentId: "", classRoomId: "" })
    match items.get(ev.classRoomId) {
      None => ()
      Some(cur) => {
        let enrolled = cur.enrolledCount + 1
        let remaining = cur.maxStudents - enrolled
        items[ev.classRoomId] = {
          ..cur,
          enrolledCount: enrolled,
          isFull: cur.maxStudents > 0 && enrolled >= cur.maxStudents,
          remainingCapacity: if remaining > 0 { remaining } else { 0 },
        }
      }
    }
  } else if event_type == event_student_dropped {
    let ev = parse_json(payload, StudentDroppedFromClassRoom::{ studentId: "", classRoomId: "" })
    match items.get(ev.classRoomId) {
      None => ()
      Some(cur) => {
        let enrolled = if cur.enrolledCount > 0 { cur.enrolledCount - 1 } else { 0 }
        let remaining = cur.maxStudents - enrolled
        items[ev.classRoomId] = {
          ..cur,
          enrolledCount: enrolled,
          isFull: cur.maxStudents > 0 && enrolled >= cur.maxStudents,
          remainingCapacity: if remaining > 0 { remaining } else { 0 },
        }
      }
    }
  }
  { items }
}

fn apply_user_directory_tag(state : UserDirectoryState, event_type : String, payload : String) -> UserDirectoryState {
  if event_type == event_user_registered {
    let ev = parse_json(payload, UserRegistered::{
      userId: "",
      displayName: "",
      email: "",
      department: None,
      registeredAt: "",
      monthlyReservationLimit: 0,
    })
    {
      userId: ev.userId,
      displayName: ev.displayName,
      email: ev.email,
      department: ev.department,
      registeredAt: ev.registeredAt,
      monthlyReservationLimit: ev.monthlyReservationLimit,
      externalProviders: [],
      isActive: true,
    }
  } else if event_type == event_user_profile_updated {
    let ev = parse_json(payload, UserProfileUpdated::{
      userId: "",
      displayName: "",
      email: "",
      department: None,
      monthlyReservationLimit: 0,
    })
    {
      ..state,
      displayName: ev.displayName,
      email: ev.email,
      department: ev.department,
      monthlyReservationLimit: ev.monthlyReservationLimit,
    }
  } else {
    state
  }
}

fn apply_user_directory_list(state : UserDirectoryListState, event_type : String, payload : String) -> UserDirectoryListState {
  let items = state.items
  if event_type == event_user_registered {
    let ev = parse_json(payload, UserRegistered::{
      userId: "",
      displayName: "",
      email: "",
      department: None,
      registeredAt: "",
      monthlyReservationLimit: 0,
    })
    items[ev.userId] = {
      userId: ev.userId,
      displayName: ev.displayName,
      email: ev.email,
      department: ev.department,
      isActive: true,
      registeredAt: ev.registeredAt,
      monthlyReservationLimit: ev.monthlyReservationLimit,
      externalProviders: [],
      roles: [],
    }
  } else if event_type == event_user_profile_updated {
    let ev = parse_json(payload, UserProfileUpdated::{
      userId: "",
      displayName: "",
      email: "",
      department: None,
      monthlyReservationLimit: 0,
    })
    match items.get(ev.userId) {
      None => ()
      Some(cur) => items[ev.userId] = {
        ..cur,
        displayName: ev.displayName,
        email: ev.email,
        department: ev.department,
        monthlyReservationLimit: ev.monthlyReservationLimit,
      }
    }
  }
  { items }
}

fn apply_user_access_tag(state : UserAccessState, event_type : String, payload : String) -> UserAccessState {
  if event_type == event_user_access_granted {
    let ev = parse_json(payload, UserAccessGranted::{ userId: "", initialRole: "", grantedAt: "" })
    { userId: ev.userId, roles: [ev.initialRole], grantedAt: ev.grantedAt, isActive: true }
  } else if event_type == event_user_role_granted {
    let ev = parse_json(payload, UserRoleGranted::{ userId: "", role: "", grantedAt: "" })
    { ..state, roles: add_unique(state.roles, ev.role) }
  } else {
    state
  }
}

fn apply_user_access_list(state : UserAccessListState, event_type : String, payload : String) -> UserAccessListState {
  let items = state.items
  if event_type == event_user_access_granted {
    let ev = parse_json(payload, UserAccessGranted::{ userId: "", initialRole: "", grantedAt: "" })
    items[ev.userId] = { userId: ev.userId, roles: [ev.initialRole], isActive: true, grantedAt: ev.grantedAt }
  } else if event_type == event_user_role_granted {
    let ev = parse_json(payload, UserRoleGranted::{ userId: "", role: "", grantedAt: "" })
    match items.get(ev.userId) {
      None => ()
      Some(cur) => items[ev.userId] = { ..cur, roles: add_unique(cur.roles, ev.role) }
    }
  }
  { items }
}

fn apply_room_tag(state : RoomState, event_type : String, payload : String) -> RoomState {
  if event_type == event_room_created {
    let ev = parse_json(payload, RoomCreated::{
      roomId: "",
      name: "",
      capacity: 0,
      location: "",
      equipment: [],
      requiresApproval: false,
    })
    {
      roomId: ev.roomId,
      name: ev.name,
      capacity: ev.capacity,
      location: ev.location,
      equipment: ev.equipment,
      requiresApproval: ev.requiresApproval,
      isActive: true,
    }
  } else if event_type == event_room_updated {
    let ev = parse_json(payload, RoomUpdated::{
      roomId: "",
      name: "",
      capacity: 0,
      location: "",
      equipment: [],
      requiresApproval: false,
    })
    {
      ..state,
      name: ev.name,
      capacity: ev.capacity,
      location: ev.location,
      equipment: ev.equipment,
      requiresApproval: ev.requiresApproval,
    }
  } else {
    state
  }
}

fn apply_room_list(state : RoomListState, event_type : String, payload : String) -> RoomListState {
  let items = state.items
  if event_type == event_room_created {
    let ev = parse_json(payload, RoomCreated::{
      roomId: "",
      name: "",
      capacity: 0,
      location: "",
      equipment: [],
      requiresApproval: false,
    })
    items[ev.roomId] = {
      roomId: ev.roomId,
      name: ev.name,
      capacity: ev.capacity,
      location: ev.location,
      equipment: ev.equipment,
      requiresApproval: ev.requiresApproval,
      isActive: true,
    }
  } else if event_type == event_room_updated {
    let ev = parse_json(payload, RoomUpdated::{
      roomId: "",
      name: "",
      capacity: 0,
      location: "",
      equipment: [],
      requiresApproval: false,
    })
    match items.get(ev.roomId) {
      None => ()
      Some(cur) => items[ev.roomId] = {
        ..cur,
        name: ev.name,
        capacity: ev.capacity,
        location: ev.location,
        equipment: ev.equipment,
        requiresApproval: ev.requiresApproval,
      }
    }
  }
  { items }
}

fn apply_reservation_tag(state : ReservationState, event_type : String, payload : String) -> ReservationState {
  if event_type == event_reservation_draft_created {
    let ev = parse_json(payload, ReservationDraftCreated::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
    })
    {
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      organizerId: ev.organizerId,
      organizerName: ev.organizerName,
      startTime: ev.startTime,
      endTime: ev.endTime,
      purpose: ev.purpose,
      selectedEquipment: ev.selectedEquipment,
      status: "Draft",
      requiresApproval: false,
      approvalRequestId: None,
      approvalRequestComment: None,
      approvalDecisionComment: None,
      confirmedAt: None,
      reason: None,
    }
  } else if event_type == event_reservation_hold_committed {
    let ev = parse_json(payload, ReservationHoldCommitted::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      requiresApproval: false,
      approvalRequestId: None,
      approvalRequestComment: None,
    })
    {
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      organizerId: ev.organizerId,
      organizerName: ev.organizerName,
      startTime: ev.startTime,
      endTime: ev.endTime,
      purpose: ev.purpose,
      selectedEquipment: ev.selectedEquipment,
      status: "Held",
      requiresApproval: ev.requiresApproval,
      approvalRequestId: ev.approvalRequestId,
      approvalRequestComment: ev.approvalRequestComment,
      approvalDecisionComment: None,
      confirmedAt: None,
      reason: None,
    }
  } else if event_type == event_reservation_confirmed {
    let ev = parse_json(payload, ReservationConfirmed::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      confirmedAt: "",
      approvalRequestId: None,
      approvalRequestComment: None,
      approvalDecisionComment: None,
    })
    {
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      organizerId: ev.organizerId,
      organizerName: ev.organizerName,
      startTime: ev.startTime,
      endTime: ev.endTime,
      purpose: ev.purpose,
      selectedEquipment: ev.selectedEquipment,
      status: "Confirmed",
      requiresApproval: option_string(ev.approvalRequestId) != "",
      approvalRequestId: ev.approvalRequestId,
      approvalRequestComment: ev.approvalRequestComment,
      approvalDecisionComment: ev.approvalDecisionComment,
      confirmedAt: Some(ev.confirmedAt),
      reason: None,
    }
  } else if event_type == event_reservation_cancelled {
    let ev = parse_json(payload, ReservationCancelled::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      approvalRequestComment: None,
      reason: "",
      cancelledAt: "",
    })
    {
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      organizerId: ev.organizerId,
      organizerName: ev.organizerName,
      startTime: ev.startTime,
      endTime: ev.endTime,
      purpose: ev.purpose,
      selectedEquipment: ev.selectedEquipment,
      status: "Cancelled",
      requiresApproval: false,
      approvalRequestId: None,
      approvalRequestComment: ev.approvalRequestComment,
      approvalDecisionComment: Some(ev.reason),
      confirmedAt: None,
      reason: Some(ev.reason),
    }
  } else if event_type == event_reservation_rejected {
    let ev = parse_json(payload, ReservationRejected::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      approvalRequestId: "",
      approvalRequestComment: None,
      reason: "",
      rejectedAt: "",
    })
    {
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      organizerId: ev.organizerId,
      organizerName: ev.organizerName,
      startTime: ev.startTime,
      endTime: ev.endTime,
      purpose: ev.purpose,
      selectedEquipment: ev.selectedEquipment,
      status: "Rejected",
      requiresApproval: true,
      approvalRequestId: Some(ev.approvalRequestId),
      approvalRequestComment: ev.approvalRequestComment,
      approvalDecisionComment: Some(ev.reason),
      confirmedAt: None,
      reason: Some(ev.reason),
    }
  } else {
    state
  }
}

fn apply_reservation_list(state : ReservationListState, event_type : String, payload : String) -> ReservationListState {
  let items = state.items
  if event_type == event_reservation_draft_created {
    let ev = parse_json(payload, ReservationDraftCreated::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
    })
    items[ev.reservationId] = {
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      organizerId: ev.organizerId,
      organizerName: ev.organizerName,
      startTime: ev.startTime,
      endTime: ev.endTime,
      purpose: ev.purpose,
      selectedEquipment: ev.selectedEquipment,
      status: "Draft",
      requiresApproval: false,
      approvalRequestId: None,
      approvalRequestComment: None,
      approvalDecisionComment: None,
    }
  } else if event_type == event_reservation_hold_committed {
    let ev = parse_json(payload, ReservationHoldCommitted::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      requiresApproval: false,
      approvalRequestId: None,
      approvalRequestComment: None,
    })
    match items.get(ev.reservationId) {
      None => ()
      Some(cur) => items[ev.reservationId] = {
        ..cur,
        status: "Held",
        requiresApproval: ev.requiresApproval,
        approvalRequestId: ev.approvalRequestId,
        approvalRequestComment: ev.approvalRequestComment,
      }
    }
  } else if event_type == event_reservation_confirmed {
    let ev = parse_json(payload, ReservationConfirmed::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      confirmedAt: "",
      approvalRequestId: None,
      approvalRequestComment: None,
      approvalDecisionComment: None,
    })
    match items.get(ev.reservationId) {
      None => ()
      Some(cur) => items[ev.reservationId] = {
        ..cur,
        status: "Confirmed",
        requiresApproval: option_string(ev.approvalRequestId) != "",
        approvalRequestId: ev.approvalRequestId,
        approvalRequestComment: ev.approvalRequestComment,
        approvalDecisionComment: ev.approvalDecisionComment,
      }
    }
  } else if event_type == event_reservation_cancelled {
    let ev = parse_json(payload, ReservationCancelled::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      approvalRequestComment: None,
      reason: "",
      cancelledAt: "",
    })
    match items.get(ev.reservationId) {
      None => ()
      Some(cur) => items[ev.reservationId] = {
        ..cur,
        status: "Cancelled",
        requiresApproval: false,
        approvalRequestId: None,
        approvalRequestComment: ev.approvalRequestComment,
        approvalDecisionComment: Some(ev.reason),
      }
    }
  } else if event_type == event_reservation_rejected {
    let ev = parse_json(payload, ReservationRejected::{
      reservationId: "",
      roomId: "",
      organizerId: "",
      organizerName: "",
      startTime: "",
      endTime: "",
      purpose: "",
      selectedEquipment: [],
      approvalRequestId: "",
      approvalRequestComment: None,
      reason: "",
      rejectedAt: "",
    })
    match items.get(ev.reservationId) {
      None => ()
      Some(cur) => items[ev.reservationId] = {
        ..cur,
        status: "Rejected",
        requiresApproval: true,
        approvalRequestId: Some(ev.approvalRequestId),
        approvalRequestComment: ev.approvalRequestComment,
        approvalDecisionComment: Some(ev.reason),
      }
    }
  }
  { items }
}

fn apply_approval_request_tag(state : ApprovalRequestState, event_type : String, payload : String) -> ApprovalRequestState {
  if event_type == event_approval_flow_started {
    let ev = parse_json(payload, ApprovalFlowStarted::{
      approvalRequestId: "",
      reservationId: "",
      roomId: "",
      requesterId: "",
      approverIds: [],
      requestedAt: "",
      requestComment: None,
    })
    {
      approvalRequestId: ev.approvalRequestId,
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      requesterId: ev.requesterId,
      approverIds: ev.approverIds,
      requestedAt: ev.requestedAt,
      requestComment: ev.requestComment,
      status: "Pending",
    }
  } else if event_type == event_approval_decision_recorded {
    let ev = parse_json(payload, ApprovalDecisionRecorded::{
      approvalRequestId: "",
      reservationId: "",
      approverId: "",
      decision: "",
      comment: None,
      decidedAt: "",
    })
    { ..state, status: if ev.decision == "Rejected" { "Rejected" } else { "Approved" } }
  } else {
    state
  }
}

fn apply_approval_inbox_list(state : ApprovalInboxState, event_type : String, payload : String) -> ApprovalInboxState {
  let items = state.items
  if event_type == event_approval_flow_started {
    let ev = parse_json(payload, ApprovalFlowStarted::{
      approvalRequestId: "",
      reservationId: "",
      roomId: "",
      requesterId: "",
      approverIds: [],
      requestedAt: "",
      requestComment: None,
    })
    items[ev.approvalRequestId] = {
      approvalRequestId: ev.approvalRequestId,
      reservationId: ev.reservationId,
      roomId: ev.roomId,
      requesterId: ev.requesterId,
      requestComment: ev.requestComment,
      approverIds: ev.approverIds,
      requestedAt: ev.requestedAt,
      status: "Pending",
    }
  } else if event_type == event_approval_decision_recorded {
    let ev = parse_json(payload, ApprovalDecisionRecorded::{
      approvalRequestId: "",
      reservationId: "",
      approverId: "",
      decision: "",
      comment: None,
      decidedAt: "",
    })
    match items.get(ev.approvalRequestId) {
      None => ()
      Some(cur) => items[ev.approvalRequestId] = {
        ..cur,
        status: if ev.decision == "Rejected" { "Rejected" } else { "Approved" },
        requestedAt: ev.decidedAt,
      }
    }
  }
  { items }
}

fn apply_event_to_instance(inst : Instance, event_type : String, payload_json : String) -> Instance {
  match inst.kind {
    WeatherTag => { ..inst, weatherTag: apply_weather_tag(inst.weatherTag, event_type, payload_json) }
    StudentTag => { ..inst, studentTag: apply_student_tag(inst.studentTag, event_type, payload_json) }
    ClassRoomTag => { ..inst, classRoomTag: apply_classroom_tag(inst.classRoomTag, event_type, payload_json) }
    UserDirectoryTag => { ..inst, userDirectoryTag: apply_user_directory_tag(inst.userDirectoryTag, event_type, payload_json) }
    UserAccessTag => { ..inst, userAccessTag: apply_user_access_tag(inst.userAccessTag, event_type, payload_json) }
    RoomTag => { ..inst, roomTag: apply_room_tag(inst.roomTag, event_type, payload_json) }
    ReservationTag => { ..inst, reservationTag: apply_reservation_tag(inst.reservationTag, event_type, payload_json) }
    ApprovalRequestTag => { ..inst, approvalRequestTag: apply_approval_request_tag(inst.approvalRequestTag, event_type, payload_json) }
    WeatherList => { ..inst, weatherList: apply_weather_list(inst.weatherList, event_type, payload_json) }
    StudentList => { ..inst, studentList: apply_student_list(inst.studentList, event_type, payload_json) }
    ClassRoomList => { ..inst, classRoomList: apply_classroom_list(inst.classRoomList, event_type, payload_json) }
    UserDirectoryList => { ..inst, userDirectoryList: apply_user_directory_list(inst.userDirectoryList, event_type, payload_json) }
    UserAccessList => { ..inst, userAccessList: apply_user_access_list(inst.userAccessList, event_type, payload_json) }
    RoomList => { ..inst, roomList: apply_room_list(inst.roomList, event_type, payload_json) }
    ReservationList => { ..inst, reservationList: apply_reservation_list(inst.reservationList, event_type, payload_json) }
    ApprovalInboxList => { ..inst, approvalInboxList: apply_approval_inbox_list(inst.approvalInboxList, event_type, payload_json) }
    Unknown => inst
  }
}

fn matches_optional_filter(value : String, filter : String?) -> Bool {
  match filter {
    None => true
    Some(f) => value.to_lower().contains(f.to_lower())
  }
}

fn apply_paging[T](items : Array[T], page_number : Int?, page_size : Int?) -> Array[T] {
  match page_size {
    None => items
    Some(ps) => {
      if ps <= 0 {
        return items
      }
      let pn = match page_number {
        None => 1
        Some(v) => if v > 0 { v } else { 1 }
      }
      let start = (pn - 1) * ps
      if start >= items.length() {
        []
      } else {
        items[start:start + ps].to_array()
      }
    }
  }
}

fn weather_items(state : WeatherForecastListState, query : WeatherListQuery) -> Array[WeatherForecastItem] {
  let items : Array[WeatherForecastItem] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    match query.forecastId {
      None => ()
      Some(forecast_id) => if v.forecastId != forecast_id { continue }
    }
    if !matches_optional_filter(v.location, query.locationFilter) {
      continue
    }
    items.push(v)
  }
  items.sort_by((left, right) => left.location.compare(right.location))
  items
}

fn user_directory_items(state : UserDirectoryListState, query : UserDirectoryListQuery) -> Array[UserDirectoryListItem] {
  let items : Array[UserDirectoryListItem] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    if query.activeOnly && !v.isActive {
      continue
    }
    items.push(v)
  }
  items.sort_by((left, right) => left.displayName.compare(right.displayName))
  apply_paging(items, query.pageNumber, query.pageSize)
}

fn user_access_items(state : UserAccessListState, query : UserAccessListQuery) -> Array[UserAccessListItem] {
  let items : Array[UserAccessListItem] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    if query.activeOnly && !v.isActive {
      continue
    }
    match query.roleFilter {
      None => ()
      Some(role) => if !contains(v.roles, role) { continue }
    }
    items.push(v)
  }
  items.sort_by((left, right) => right.grantedAt.compare(left.grantedAt))
  apply_paging(items, query.pageNumber, query.pageSize)
}

fn room_items(state : RoomListState, query : PagingQuery) -> Array[RoomListItem] {
  let items : Array[RoomListItem] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    items.push(v)
  }
  items.sort_by((left, right) => left.name.compare(right.name))
  apply_paging(items, query.pageNumber, query.pageSize)
}

fn student_items(state : StudentListState, query : PagingQuery) -> Array[StudentState] {
  let items : Array[StudentState] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    items.push(v)
  }
  items.sort_by((left, right) => left.name.compare(right.name))
  apply_paging(items, query.pageNumber, query.pageSize)
}

fn classroom_items(state : ClassRoomListState, query : PagingQuery) -> Array[ClassRoomItem] {
  let items : Array[ClassRoomItem] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    items.push(v)
  }
  items.sort_by((left, right) => left.name.compare(right.name))
  apply_paging(items, query.pageNumber, query.pageSize)
}

fn reservation_items(state : ReservationListState, query : ReservationListQuery) -> Array[ReservationListItem] {
  let items : Array[ReservationListItem] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    match query.roomId {
      None => ()
      Some(room_id) => if v.roomId != room_id { continue }
    }
    items.push(v)
  }
  items.sort_by((left, right) => left.startTime.compare(right.startTime))
  apply_paging(items, query.pageNumber, query.pageSize)
}

fn approval_items(state : ApprovalInboxState, query : ApprovalListQuery) -> Array[ApprovalInboxItem] {
  let items : Array[ApprovalInboxItem] = []
  for kv in state.items.iter() {
    let (_k, v) = kv
    if query.pendingOnly && v.status != "Pending" {
      continue
    }
    items.push(v)
  }
  items.sort_by((left, right) => right.requestedAt.compare(left.requestedAt))
  apply_paging(items, query.pageNumber, query.pageSize)
}

fn serialize_instance_state(inst : Instance) -> String {
  match inst.kind {
    WeatherTag => if inst.weatherTag.forecastId == "" { "{}" } else { json_stringify(inst.weatherTag.to_json()) }
    StudentTag => if inst.studentTag.studentId == "" { "{}" } else { json_stringify(inst.studentTag.to_json()) }
    ClassRoomTag => if inst.classRoomTag.classRoomId == "" { "{}" } else { json_stringify(inst.classRoomTag.to_json()) }
    UserDirectoryTag => if inst.userDirectoryTag.userId == "" { "{}" } else { json_stringify(inst.userDirectoryTag.to_json()) }
    UserAccessTag => if inst.userAccessTag.userId == "" { "{}" } else { json_stringify(inst.userAccessTag.to_json()) }
    RoomTag => if inst.roomTag.roomId == "" { "{}" } else { json_stringify(inst.roomTag.to_json()) }
    ReservationTag => if inst.reservationTag.reservationId == "" { "{}" } else { json_stringify(inst.reservationTag.to_json()) }
    ApprovalRequestTag => if inst.approvalRequestTag.approvalRequestId == "" { "{}" } else { json_stringify(inst.approvalRequestTag.to_json()) }
    WeatherList => json_stringify(inst.weatherList.to_json())
    StudentList => json_stringify(inst.studentList.to_json())
    ClassRoomList => json_stringify(inst.classRoomList.to_json())
    UserDirectoryList => json_stringify(inst.userDirectoryList.to_json())
    UserAccessList => json_stringify(inst.userAccessList.to_json())
    RoomList => json_stringify(inst.roomList.to_json())
    ReservationList => json_stringify(inst.reservationList.to_json())
    ApprovalInboxList => json_stringify(inst.approvalInboxList.to_json())
    Unknown => "{}"
  }
}

fn restore_instance_state(inst : Instance, state_json : String) -> Instance {
  if is_empty_json(state_json) {
    return instance_new(inst.kind)
  }
  match inst.kind {
    WeatherTag => { ..inst, weatherTag: parse_json(state_json, empty_weather_state()) }
    StudentTag => { ..inst, studentTag: parse_json(state_json, empty_student_state()) }
    ClassRoomTag => { ..inst, classRoomTag: parse_json(state_json, empty_classroom_state()) }
    UserDirectoryTag => { ..inst, userDirectoryTag: parse_json(state_json, empty_user_directory_state()) }
    UserAccessTag => { ..inst, userAccessTag: parse_json(state_json, empty_user_access_state()) }
    RoomTag => { ..inst, roomTag: parse_json(state_json, empty_room_state()) }
    ReservationTag => { ..inst, reservationTag: parse_json(state_json, empty_reservation_state()) }
    ApprovalRequestTag => { ..inst, approvalRequestTag: parse_json(state_json, empty_approval_request_state()) }
    WeatherList => { ..inst, weatherList: parse_json(state_json, empty_weather_list_state()) }
    StudentList => { ..inst, studentList: parse_json(state_json, empty_student_list_state()) }
    ClassRoomList => { ..inst, classRoomList: parse_json(state_json, empty_classroom_list_state()) }
    UserDirectoryList => { ..inst, userDirectoryList: parse_json(state_json, empty_user_directory_list_state()) }
    UserAccessList => { ..inst, userAccessList: parse_json(state_json, empty_user_access_list_state()) }
    RoomList => { ..inst, roomList: parse_json(state_json, empty_room_list_state()) }
    ReservationList => { ..inst, reservationList: parse_json(state_json, empty_reservation_list_state()) }
    ApprovalInboxList => { ..inst, approvalInboxList: parse_json(state_json, empty_approval_inbox_state()) }
    Unknown => inst
  }
}

fn execute_query_json(inst : Instance, query_type : String, params_json : String) -> String {
  if inst.kind == WeatherList && query_type == query_weather_count {
    let query = parse_json(params_json, WeatherCountQuery::{ locationFilter: None, forecastId: None, waitForSortableUniqueId: None })
    let items = weather_items(inst.weatherList, {
      locationFilter: query.locationFilter,
      forecastId: query.forecastId,
      waitForSortableUniqueId: query.waitForSortableUniqueId,
      pageNumber: None,
      pageSize: None,
    })
    return json_stringify(CountResult::{ count: items.length() }.to_json())
  }
  "null"
}

fn execute_list_query_json(inst : Instance, query_type : String, params_json : String) -> String {
  match inst.kind {
    WeatherList => {
      if query_type == query_weather_list {
        let query = parse_json(params_json, WeatherListQuery::{ locationFilter: None, forecastId: None, waitForSortableUniqueId: None, pageNumber: None, pageSize: None })
        json_stringify(weather_items(inst.weatherList, query).to_json())
      } else {
        "[]"
      }
    }
    StudentList => {
      if query_type == query_student_list {
        let query = parse_json(params_json, PagingQuery::{ pageNumber: None, pageSize: None, waitForSortableUniqueId: None })
        json_stringify(student_items(inst.studentList, query).to_json())
      } else {
        "[]"
      }
    }
    ClassRoomList => {
      if query_type == query_classroom_list {
        let query = parse_json(params_json, PagingQuery::{ pageNumber: None, pageSize: None, waitForSortableUniqueId: None })
        json_stringify(classroom_items(inst.classRoomList, query).to_json())
      } else {
        "[]"
      }
    }
    UserDirectoryList => {
      if query_type == query_user_directory_list {
        let query = parse_json(params_json, UserDirectoryListQuery::{ pageNumber: None, pageSize: None, waitForSortableUniqueId: None, activeOnly: false })
        json_stringify(user_directory_items(inst.userDirectoryList, query).to_json())
      } else {
        "[]"
      }
    }
    UserAccessList => {
      if query_type == query_user_access_list {
        let query = parse_json(params_json, UserAccessListQuery::{ pageNumber: None, pageSize: None, waitForSortableUniqueId: None, activeOnly: false, roleFilter: None })
        json_stringify(user_access_items(inst.userAccessList, query).to_json())
      } else {
        "[]"
      }
    }
    RoomList => {
      if query_type == query_room_list {
        let query = parse_json(params_json, PagingQuery::{ pageNumber: None, pageSize: None, waitForSortableUniqueId: None })
        json_stringify(room_items(inst.roomList, query).to_json())
      } else {
        "[]"
      }
    }
    ReservationList => {
      if query_type == query_reservation_list {
        let query = parse_json(params_json, ReservationListQuery::{ pageNumber: None, pageSize: None, waitForSortableUniqueId: None, roomId: None })
        json_stringify(reservation_items(inst.reservationList, query).to_json())
      } else {
        "[]"
      }
    }
    ApprovalInboxList => {
      if query_type == query_approval_inbox {
        let query = parse_json(params_json, ApprovalListQuery::{ pageNumber: None, pageSize: None, waitForSortableUniqueId: None, pendingOnly: true })
        json_stringify(approval_items(inst.approvalInboxList, query).to_json())
      } else {
        "[]"
      }
    }
    _ => "[]"
  }
}
