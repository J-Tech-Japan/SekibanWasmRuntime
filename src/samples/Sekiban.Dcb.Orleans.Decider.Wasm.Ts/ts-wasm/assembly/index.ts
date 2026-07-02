import { JSON } from "json-as/assembly";
import { readStr, writeStr, applyPaging } from "@sekiban/as-wasm/assembly";
import * as C from "./domain/constants";
export { alloc, dealloc } from "@sekiban/as-wasm/assembly";
export { mv_metadata, mv_initialize, mv_apply_event } from "./materialized_view";

// ---------------------------------------------------------------------------
// Projector instance storage
// ---------------------------------------------------------------------------

@json
class WeatherForecastState {
  forecastId: string = "";
  location: string = "";
  date: string = "";
  temperatureC: i32 = 0;
  summary: string = "";
  createdAt: string = "";
  isDeleted: bool = false;
  deletedAt: string = "";
}

@json
class WeatherForecastItem {
  forecastId: string = "";
  location: string = "";
  date: string = "";
  temperatureC: i32 = 0;
  summary: string = "";
  createdAt: string = "";
  isDeleted: bool = false;
  deletedAt: string = "";
}

@json
class StudentState {
  studentId: string = "";
  name: string = "";
  maxClassCount: i32 = 0;
  enrolledClassRoomIds: string[] = [];
}

@json
class ClassRoomState {
  classRoomId: string = "";
  name: string = "";
  maxStudents: i32 = 0;
  enrolledStudentIds: string[] = [];
  isFull: bool = false;
}

@json
class ClassRoomItem {
  classRoomId: string = "";
  name: string = "";
  maxStudents: i32 = 0;
  enrolledCount: i32 = 0;
  isFull: bool = false;
  remainingCapacity: i32 = 0;
}

@json
class UserDirectoryState {
  userId: string = "";
  displayName: string = "";
  email: string = "";
  department: string = "";
  registeredAt: string = "";
  monthlyReservationLimit: i32 = 0;
  externalProviders: string[] = [];
  isActive: bool = false;
}

@json
class UserDirectoryListItem {
  userId: string = "";
  displayName: string = "";
  email: string = "";
  department: string = "";
  registeredAt: string = "";
  monthlyReservationLimit: i32 = 0;
  roles: string[] = [];
  isActive: bool = false;
}

@json
class UserAccessState {
  userId: string = "";
  roles: string[] = [];
  grantedAt: string = "";
  isActive: bool = false;
}

@json
class UserAccessListItem {
  userId: string = "";
  roles: string[] = [];
  grantedAt: string = "";
  isActive: bool = false;
}

@json
class RoomState {
  roomId: string = "";
  name: string = "";
  capacity: i32 = 0;
  location: string = "";
  equipment: string[] = [];
  requiresApproval: bool = false;
  isActive: bool = false;
}

@json
class RoomListItem {
  roomId: string = "";
  name: string = "";
  capacity: i32 = 0;
  location: string = "";
  equipment: string[] = [];
  requiresApproval: bool = false;
  isActive: bool = false;
}

@json
class ReservationSlot {
  startTime: string = "";
  endTime: string = "";
  purpose: string = "";
  organizerId: string = "";
  status: i32 = 0;
}

@json
class RoomReservationsState {
  activeReservations: Map<string, ReservationSlot> = new Map<string, ReservationSlot>();
}

@json
class ReservationState {
  reservationId: string = "";
  roomId: string = "";
  organizerId: string = "";
  organizerName: string = "";
  startTime: string = "";
  endTime: string = "";
  purpose: string = "";
  selectedEquipment: string[] = [];
  status: string = "";
  requiresApproval: bool = false;
  approvalRequestId: string = "";
  approvalRequestComment: string = "";
  approvalDecisionComment: string = "";
  confirmedAt: string = "";
  reason: string = "";
  cancelReason: string = "";
  cancelledAt: string = "";
  rejectReason: string = "";
  rejectedAt: string = "";
}

@json
class ReservationListItem {
  reservationId: string = "";
  roomId: string = "";
  organizerId: string = "";
  organizerName: string = "";
  startTime: string = "";
  endTime: string = "";
  purpose: string = "";
  selectedEquipment: string[] = [];
  status: string = "";
  requiresApproval: bool = false;
  approvalRequestId: string = "";
  confirmedAt: string = "";
  cancelledAt: string = "";
  rejectedAt: string = "";
}

@json
class ApprovalRequestState {
  approvalRequestId: string = "";
  reservationId: string = "";
  roomId: string = "";
  requesterId: string = "";
  approverIds: string[] = [];
  requestedAt: string = "";
  requestComment: string = "";
  status: string = "";
  approverId: string = "";
  decisionComment: string = "";
  decidedAt: string = "";
}

@json
class ApprovalInboxItem {
  approvalRequestId: string = "";
  reservationId: string = "";
  roomId: string = "";
  requesterId: string = "";
  approverIds: string[] = [];
  requestedAt: string = "";
  requestComment: string = "";
  status: string = "";
  approverId: string = "";
  decisionComment: string = "";
  decidedAt: string = "";
}

// ---------------------------------------------------------------------------
// Event payload classes
// ---------------------------------------------------------------------------

@json class WeatherForecastCreatedEv { forecastId: string = ""; location: string = ""; date: string = ""; temperatureC: i32 = 0; summary: string = ""; createdAt: string = ""; }
@json class WeatherForecastLocationUpdatedEv { forecastId: string = ""; newLocation: string = ""; updatedAt: string = ""; }
@json class WeatherForecastDeletedEv { forecastId: string = ""; deletedAt: string = ""; }
@json class StudentCreatedEv { studentId: string = ""; name: string = ""; maxClassCount: i32 = 0; }
@json class ClassRoomCreatedEv { classRoomId: string = ""; name: string = ""; maxStudents: i32 = 0; }
@json class StudentEnrolledEv { studentId: string = ""; classRoomId: string = ""; }
@json class StudentDroppedEv { studentId: string = ""; classRoomId: string = ""; }
@json class UserRegisteredEv { userId: string = ""; displayName: string = ""; email: string = ""; department: string = ""; registeredAt: string = ""; monthlyReservationLimit: i32 = 0; }
@json class UserProfileUpdatedEv { userId: string = ""; displayName: string = ""; email: string = ""; department: string = ""; monthlyReservationLimit: i32 = 0; }
@json class UserAccessGrantedEv { userId: string = ""; initialRole: string = ""; grantedAt: string = ""; }
@json class UserRoleGrantedEv { userId: string = ""; role: string = ""; grantedAt: string = ""; }
@json class RoomCreatedEv { roomId: string = ""; name: string = ""; capacity: i32 = 0; location: string = ""; equipment: string[] = []; requiresApproval: bool = false; }
@json class RoomUpdatedEv { roomId: string = ""; name: string = ""; capacity: i32 = 0; location: string = ""; equipment: string[] = []; requiresApproval: bool = false; }
@json class ReservationDraftCreatedEv { reservationId: string = ""; roomId: string = ""; organizerId: string = ""; organizerName: string = ""; startTime: string = ""; endTime: string = ""; purpose: string = ""; selectedEquipment: string[] = []; }
@json class ReservationHoldCommittedEv { reservationId: string = ""; roomId: string = ""; organizerId: string = ""; organizerName: string = ""; startTime: string = ""; endTime: string = ""; purpose: string = ""; selectedEquipment: string[] = []; requiresApproval: bool = false; approvalRequestId: string = ""; approvalRequestComment: string = ""; }
@json class ReservationConfirmedEv { reservationId: string = ""; roomId: string = ""; organizerId: string = ""; organizerName: string = ""; startTime: string = ""; endTime: string = ""; purpose: string = ""; selectedEquipment: string[] = []; confirmedAt: string = ""; approvalRequestId: string = ""; approvalRequestComment: string = ""; approvalDecisionComment: string = ""; }
@json class ReservationCancelledEv { reservationId: string = ""; roomId: string = ""; organizerId: string = ""; organizerName: string = ""; startTime: string = ""; endTime: string = ""; purpose: string = ""; selectedEquipment: string[] = []; approvalRequestComment: string = ""; reason: string = ""; cancelledAt: string = ""; }
@json class ReservationRejectedEv { reservationId: string = ""; roomId: string = ""; organizerId: string = ""; organizerName: string = ""; startTime: string = ""; endTime: string = ""; purpose: string = ""; selectedEquipment: string[] = []; approvalRequestId: string = ""; approvalRequestComment: string = ""; reason: string = ""; rejectedAt: string = ""; }
@json class ApprovalFlowStartedEv { approvalRequestId: string = ""; reservationId: string = ""; roomId: string = ""; requesterId: string = ""; approverIds: string[] = []; requestedAt: string = ""; requestComment: string = ""; }
@json class ApprovalDecisionRecordedEv { approvalRequestId: string = ""; reservationId: string = ""; approverId: string = ""; decision: string = ""; comment: string = ""; decidedAt: string = ""; }

// ---------------------------------------------------------------------------
// List state wrappers - use parallel arrays since Map serialization is complex
// ---------------------------------------------------------------------------

class WeatherForecastListState {
  items: Map<string, WeatherForecastItem> = new Map<string, WeatherForecastItem>();
}

class StudentListState {
  items: Map<string, StudentState> = new Map<string, StudentState>();
}

class ClassRoomListState {
  items: Map<string, ClassRoomItem> = new Map<string, ClassRoomItem>();
}

class UserDirectoryListState {
  items: Map<string, UserDirectoryListItem> = new Map<string, UserDirectoryListItem>();
}

class UserAccessListState {
  items: Map<string, UserAccessListItem> = new Map<string, UserAccessListItem>();
}

class RoomListState {
  items: Map<string, RoomListItem> = new Map<string, RoomListItem>();
}

class ReservationListState {
  items: Map<string, ReservationListItem> = new Map<string, ReservationListItem>();
}

class ApprovalRequestListState {
  items: Map<string, ApprovalInboxItem> = new Map<string, ApprovalInboxItem>();
}

// ---------------------------------------------------------------------------
// Instance
// ---------------------------------------------------------------------------

class Instance {
  kind: i32;
  weatherTag: WeatherForecastState = new WeatherForecastState();
  studentTag: StudentState = new StudentState();
  classRoomTag: ClassRoomState = new ClassRoomState();
  userDirectoryTag: UserDirectoryState = new UserDirectoryState();
  userAccessTag: UserAccessState = new UserAccessState();
  roomTag: RoomState = new RoomState();
  roomReservationsTag: RoomReservationsState = new RoomReservationsState();
  reservationTag: ReservationState = new ReservationState();
  approvalRequestTag: ApprovalRequestState = new ApprovalRequestState();
  weatherList: WeatherForecastListState = new WeatherForecastListState();
  studentList: StudentListState = new StudentListState();
  classRoomList: ClassRoomListState = new ClassRoomListState();
  userDirectoryList: UserDirectoryListState = new UserDirectoryListState();
  userAccessList: UserAccessListState = new UserAccessListState();
  roomList: RoomListState = new RoomListState();
  reservationList: ReservationListState = new ReservationListState();
  approvalList: ApprovalRequestListState = new ApprovalRequestListState();

  constructor(kind: i32) {
    this.kind = kind;
  }
}

const instances = new Map<i32, Instance>();
let nextId: i32 = 1;

function resolveKind(name: string): i32 {
  const lower = name.toLowerCase();
  if (lower == C.ProjectorWeatherTag.toLowerCase()) return C.KIND_WEATHER_TAG;
  if (lower == C.ProjectorStudentTag.toLowerCase()) return C.KIND_STUDENT_TAG;
  if (lower == C.ProjectorClassRoomTag.toLowerCase()) return C.KIND_CLASSROOM_TAG;
  if (lower == C.ProjectorUserDirectoryTag.toLowerCase()) return C.KIND_USER_DIRECTORY_TAG;
  if (lower == C.ProjectorUserAccessTag.toLowerCase()) return C.KIND_USER_ACCESS_TAG;
  if (lower == C.ProjectorRoomTag.toLowerCase()) return C.KIND_ROOM_TAG;
  if (lower == C.ProjectorRoomReservationsTag.toLowerCase()) return C.KIND_ROOM_RESERVATION_TAG;
  if (lower == C.ProjectorReservationTag.toLowerCase()) return C.KIND_RESERVATION_TAG;
  if (lower == C.ProjectorApprovalRequestTag.toLowerCase()) return C.KIND_APPROVAL_TAG;
  if (lower == C.ProjectorWeatherList.toLowerCase()) return C.KIND_WEATHER_LIST;
  if (lower == C.ProjectorStudentList.toLowerCase()) return C.KIND_STUDENT_LIST;
  if (lower == C.ProjectorClassRoomList.toLowerCase()) return C.KIND_CLASSROOM_LIST;
  if (lower == C.ProjectorUserDirectoryList.toLowerCase()) return C.KIND_USER_DIRECTORY_LIST;
  if (lower == C.ProjectorUserAccessList.toLowerCase()) return C.KIND_USER_ACCESS_LIST;
  if (lower == C.ProjectorRoomList.toLowerCase()) return C.KIND_ROOM_LIST;
  if (lower == C.ProjectorReservationList.toLowerCase()) return C.KIND_RESERVATION_LIST;
  if (lower == C.ProjectorApprovalRequestList.toLowerCase()) return C.KIND_APPROVAL_LIST;
  return C.KIND_UNKNOWN;
}

// ---------------------------------------------------------------------------
// Exported WASM functions
// ---------------------------------------------------------------------------

export function create_instance(namePtr: u32, nameLen: u32): i32 {
  const name = readStr(namePtr, nameLen);
  const kind = resolveKind(name);
  if (kind == C.KIND_UNKNOWN) return -1;
  const id = nextId;
  nextId++;
  instances.set(id, new Instance(kind));
  return id;
}

// ---------------------------------------------------------------------------
// Tag projector apply functions
// ---------------------------------------------------------------------------

function applyWeatherTag(s: WeatherForecastState, et: string, p: string): WeatherForecastState {
  if (et == C.EventWeatherForecastCreated) {
    const ev = JSON.parse<WeatherForecastCreatedEv>(p);
    s.forecastId = ev.forecastId; s.location = ev.location; s.date = ev.date;
    s.temperatureC = ev.temperatureC; s.summary = ev.summary; s.createdAt = ev.createdAt;
    s.isDeleted = false; s.deletedAt = "";
  } else if (et == C.EventWeatherForecastLocationUpdated) {
    const ev = JSON.parse<WeatherForecastLocationUpdatedEv>(p);
    s.location = ev.newLocation;
  } else if (et == C.EventWeatherForecastDeleted) {
    const ev = JSON.parse<WeatherForecastDeletedEv>(p);
    s.isDeleted = true; s.deletedAt = ev.deletedAt;
  }
  return s;
}

function applyStudentTag(s: StudentState, et: string, p: string): StudentState {
  if (et == C.EventStudentCreated) {
    const ev = JSON.parse<StudentCreatedEv>(p);
    s.studentId = ev.studentId; s.name = ev.name; s.maxClassCount = ev.maxClassCount;
    s.enrolledClassRoomIds = [];
  } else if (et == C.EventStudentEnrolledInClassRoom) {
    const ev = JSON.parse<StudentEnrolledEv>(p);
    if (!s.enrolledClassRoomIds.includes(ev.classRoomId)) s.enrolledClassRoomIds.push(ev.classRoomId);
  } else if (et == C.EventStudentDroppedFromClassRoom) {
    const ev = JSON.parse<StudentDroppedEv>(p);
    const idx = s.enrolledClassRoomIds.indexOf(ev.classRoomId);
    if (idx >= 0) s.enrolledClassRoomIds.splice(idx, 1);
  }
  return s;
}

function applyClassRoomTag(s: ClassRoomState, et: string, p: string): ClassRoomState {
  if (et == C.EventClassRoomCreated) {
    const ev = JSON.parse<ClassRoomCreatedEv>(p);
    s.classRoomId = ev.classRoomId; s.name = ev.name; s.maxStudents = ev.maxStudents;
    s.enrolledStudentIds = []; s.isFull = false;
  } else if (et == C.EventStudentEnrolledInClassRoom) {
    const ev = JSON.parse<StudentEnrolledEv>(p);
    if (!s.enrolledStudentIds.includes(ev.studentId)) s.enrolledStudentIds.push(ev.studentId);
    s.isFull = s.enrolledStudentIds.length >= s.maxStudents;
  } else if (et == C.EventStudentDroppedFromClassRoom) {
    const ev = JSON.parse<StudentDroppedEv>(p);
    const idx = s.enrolledStudentIds.indexOf(ev.studentId);
    if (idx >= 0) s.enrolledStudentIds.splice(idx, 1);
    s.isFull = s.enrolledStudentIds.length >= s.maxStudents;
  }
  return s;
}

function applyUserDirectoryTag(s: UserDirectoryState, et: string, p: string): UserDirectoryState {
  if (et == C.EventUserRegistered) {
    const ev = JSON.parse<UserRegisteredEv>(p);
    s.userId = ev.userId; s.displayName = ev.displayName; s.email = ev.email;
    s.department = ev.department; s.registeredAt = ev.registeredAt;
    s.monthlyReservationLimit = ev.monthlyReservationLimit; s.isActive = true;
  } else if (et == C.EventUserProfileUpdated) {
    const ev = JSON.parse<UserProfileUpdatedEv>(p);
    s.displayName = ev.displayName; s.email = ev.email; s.department = ev.department;
    s.monthlyReservationLimit = ev.monthlyReservationLimit;
  }
  return s;
}

function applyUserAccessTag(s: UserAccessState, et: string, p: string): UserAccessState {
  if (et == C.EventUserAccessGranted) {
    const ev = JSON.parse<UserAccessGrantedEv>(p);
    s.userId = ev.userId; s.roles = [ev.initialRole]; s.grantedAt = ev.grantedAt; s.isActive = true;
  } else if (et == C.EventUserRoleGranted) {
    const ev = JSON.parse<UserRoleGrantedEv>(p);
    if (!s.roles.includes(ev.role)) s.roles.push(ev.role);
  }
  return s;
}

function applyRoomTag(s: RoomState, et: string, p: string): RoomState {
  if (et == C.EventRoomCreated) {
    const ev = JSON.parse<RoomCreatedEv>(p);
    s.roomId = ev.roomId; s.name = ev.name; s.capacity = ev.capacity;
    s.location = ev.location; s.equipment = ev.equipment; s.requiresApproval = ev.requiresApproval;
    s.isActive = true;
  } else if (et == C.EventRoomUpdated) {
    const ev = JSON.parse<RoomUpdatedEv>(p);
    s.name = ev.name; s.capacity = ev.capacity; s.location = ev.location;
    s.equipment = ev.equipment; s.requiresApproval = ev.requiresApproval;
  }
  return s;
}

function applyRoomReservationsTag(s: RoomReservationsState, et: string, p: string): RoomReservationsState {
  if (et == C.EventReservationHoldCommitted) {
    const ev = JSON.parse<ReservationHoldCommittedEv>(p);
    const slot = new ReservationSlot();
    slot.startTime = ev.startTime;
    slot.endTime = ev.endTime;
    slot.purpose = ev.purpose;
    slot.organizerId = ev.organizerId;
    slot.status = 0;
    s.activeReservations.set(ev.reservationId, slot);
  } else if (et == C.EventReservationConfirmed) {
    const ev = JSON.parse<ReservationConfirmedEv>(p);
    const slot = new ReservationSlot();
    slot.startTime = ev.startTime;
    slot.endTime = ev.endTime;
    slot.purpose = ev.purpose;
    slot.organizerId = ev.organizerId;
    slot.status = 1;
    s.activeReservations.set(ev.reservationId, slot);
  } else if (et == C.EventReservationCancelled) {
    const ev = JSON.parse<ReservationCancelledEv>(p);
    s.activeReservations.delete(ev.reservationId);
  } else if (et == C.EventReservationRejected) {
    const ev = JSON.parse<ReservationRejectedEv>(p);
    s.activeReservations.delete(ev.reservationId);
  }
  return s;
}

function applyReservationTag(s: ReservationState, et: string, p: string): ReservationState {
  if (et == C.EventReservationDraftCreated) {
    const ev = JSON.parse<ReservationDraftCreatedEv>(p);
    s.reservationId = ev.reservationId; s.roomId = ev.roomId;
    s.organizerId = ev.organizerId; s.organizerName = ev.organizerName;
    s.startTime = ev.startTime; s.endTime = ev.endTime; s.purpose = ev.purpose;
    s.selectedEquipment = ev.selectedEquipment; s.status = "Draft";
    s.requiresApproval = false; s.approvalRequestId = ""; s.approvalRequestComment = "";
  } else if (et == C.EventReservationHoldCommitted) {
    const ev = JSON.parse<ReservationHoldCommittedEv>(p);
    s.status = "Held"; s.requiresApproval = ev.requiresApproval;
    s.approvalRequestId = ev.approvalRequestId; s.approvalRequestComment = ev.approvalRequestComment;
    if (ev.selectedEquipment.length > 0) s.selectedEquipment = ev.selectedEquipment;
  } else if (et == C.EventReservationConfirmed) {
    const ev = JSON.parse<ReservationConfirmedEv>(p);
    s.status = "Confirmed"; s.confirmedAt = ev.confirmedAt;
    s.approvalDecisionComment = ev.approvalDecisionComment;
  } else if (et == C.EventReservationCancelled) {
    const ev = JSON.parse<ReservationCancelledEv>(p);
    s.status = "Cancelled"; s.cancelReason = ev.reason; s.cancelledAt = ev.cancelledAt;
  } else if (et == C.EventReservationRejected) {
    const ev = JSON.parse<ReservationRejectedEv>(p);
    s.status = "Rejected"; s.rejectReason = ev.reason; s.rejectedAt = ev.rejectedAt;
  }
  return s;
}

function applyApprovalTag(s: ApprovalRequestState, et: string, p: string): ApprovalRequestState {
  if (et == C.EventApprovalFlowStarted) {
    const ev = JSON.parse<ApprovalFlowStartedEv>(p);
    s.approvalRequestId = ev.approvalRequestId; s.reservationId = ev.reservationId;
    s.roomId = ev.roomId; s.requesterId = ev.requesterId; s.approverIds = ev.approverIds;
    s.requestedAt = ev.requestedAt; s.requestComment = ev.requestComment; s.status = "Pending";
  } else if (et == C.EventApprovalDecisionRecorded) {
    const ev = JSON.parse<ApprovalDecisionRecordedEv>(p);
    s.approverId = ev.approverId; s.decisionComment = ev.comment; s.decidedAt = ev.decidedAt;
    s.status = ev.decision == "Approved" ? "Approved" : "Rejected";
  }
  return s;
}

// ---------------------------------------------------------------------------
// List projector apply functions
// ---------------------------------------------------------------------------

function applyWeatherList(s: WeatherForecastListState, et: string, p: string): WeatherForecastListState {
  if (et == C.EventWeatherForecastCreated) {
    const ev = JSON.parse<WeatherForecastCreatedEv>(p);
    const item = new WeatherForecastItem();
    item.forecastId = ev.forecastId; item.location = ev.location; item.date = ev.date;
    item.temperatureC = ev.temperatureC; item.summary = ev.summary; item.createdAt = ev.createdAt;
    item.isDeleted = false;
    s.items.set(ev.forecastId, item);
  } else if (et == C.EventWeatherForecastLocationUpdated) {
    const ev = JSON.parse<WeatherForecastLocationUpdatedEv>(p);
    if (s.items.has(ev.forecastId)) { const item = s.items.get(ev.forecastId); item.location = ev.newLocation; s.items.set(ev.forecastId, item); }
  } else if (et == C.EventWeatherForecastDeleted) {
    const ev = JSON.parse<WeatherForecastDeletedEv>(p);
    if (s.items.has(ev.forecastId)) { const item = s.items.get(ev.forecastId); item.isDeleted = true; item.deletedAt = ev.deletedAt; s.items.set(ev.forecastId, item); }
  }
  return s;
}

function applyStudentList(s: StudentListState, et: string, p: string): StudentListState {
  if (et == C.EventStudentCreated) {
    const ev = JSON.parse<StudentCreatedEv>(p);
    const item = new StudentState();
    item.studentId = ev.studentId; item.name = ev.name; item.maxClassCount = ev.maxClassCount;
    item.enrolledClassRoomIds = [];
    s.items.set(ev.studentId, item);
  } else if (et == C.EventStudentEnrolledInClassRoom) {
    const ev = JSON.parse<StudentEnrolledEv>(p);
    if (s.items.has(ev.studentId)) { const item = s.items.get(ev.studentId); if (!item.enrolledClassRoomIds.includes(ev.classRoomId)) item.enrolledClassRoomIds.push(ev.classRoomId); s.items.set(ev.studentId, item); }
  } else if (et == C.EventStudentDroppedFromClassRoom) {
    const ev = JSON.parse<StudentDroppedEv>(p);
    if (s.items.has(ev.studentId)) { const item = s.items.get(ev.studentId); const idx = item.enrolledClassRoomIds.indexOf(ev.classRoomId); if (idx >= 0) item.enrolledClassRoomIds.splice(idx, 1); s.items.set(ev.studentId, item); }
  }
  return s;
}

function applyClassRoomList(s: ClassRoomListState, et: string, p: string): ClassRoomListState {
  if (et == C.EventClassRoomCreated) {
    const ev = JSON.parse<ClassRoomCreatedEv>(p);
    const item = new ClassRoomItem();
    item.classRoomId = ev.classRoomId; item.name = ev.name; item.maxStudents = ev.maxStudents;
    item.enrolledCount = 0; item.isFull = false; item.remainingCapacity = ev.maxStudents;
    s.items.set(ev.classRoomId, item);
  } else if (et == C.EventStudentEnrolledInClassRoom) {
    const ev = JSON.parse<StudentEnrolledEv>(p);
    if (s.items.has(ev.classRoomId)) { const item = s.items.get(ev.classRoomId); item.enrolledCount++; item.remainingCapacity = item.maxStudents - item.enrolledCount; item.isFull = item.enrolledCount >= item.maxStudents; s.items.set(ev.classRoomId, item); }
  } else if (et == C.EventStudentDroppedFromClassRoom) {
    const ev = JSON.parse<StudentDroppedEv>(p);
    if (s.items.has(ev.classRoomId)) { const item = s.items.get(ev.classRoomId); if (item.enrolledCount > 0) item.enrolledCount--; item.remainingCapacity = item.maxStudents - item.enrolledCount; item.isFull = item.enrolledCount >= item.maxStudents; s.items.set(ev.classRoomId, item); }
  }
  return s;
}

function applyUserDirectoryList(s: UserDirectoryListState, et: string, p: string): UserDirectoryListState {
  if (et == C.EventUserRegistered) {
    const ev = JSON.parse<UserRegisteredEv>(p);
    const item = new UserDirectoryListItem();
    item.userId = ev.userId; item.displayName = ev.displayName; item.email = ev.email;
    item.department = ev.department; item.registeredAt = ev.registeredAt;
    item.monthlyReservationLimit = ev.monthlyReservationLimit; item.isActive = true;
    s.items.set(ev.userId, item);
  } else if (et == C.EventUserProfileUpdated) {
    const ev = JSON.parse<UserProfileUpdatedEv>(p);
    if (s.items.has(ev.userId)) { const item = s.items.get(ev.userId); item.displayName = ev.displayName; item.email = ev.email; item.department = ev.department; item.monthlyReservationLimit = ev.monthlyReservationLimit; s.items.set(ev.userId, item); }
  }
  return s;
}

function applyUserAccessList(s: UserAccessListState, et: string, p: string): UserAccessListState {
  if (et == C.EventUserAccessGranted) {
    const ev = JSON.parse<UserAccessGrantedEv>(p);
    const item = new UserAccessListItem();
    item.userId = ev.userId; item.roles = [ev.initialRole]; item.grantedAt = ev.grantedAt; item.isActive = true;
    s.items.set(ev.userId, item);
  } else if (et == C.EventUserRoleGranted) {
    const ev = JSON.parse<UserRoleGrantedEv>(p);
    if (s.items.has(ev.userId)) { const item = s.items.get(ev.userId); if (!item.roles.includes(ev.role)) item.roles.push(ev.role); s.items.set(ev.userId, item); }
  }
  return s;
}

function applyRoomList(s: RoomListState, et: string, p: string): RoomListState {
  if (et == C.EventRoomCreated) {
    const ev = JSON.parse<RoomCreatedEv>(p);
    const item = new RoomListItem();
    item.roomId = ev.roomId; item.name = ev.name; item.capacity = ev.capacity;
    item.location = ev.location; item.equipment = ev.equipment; item.requiresApproval = ev.requiresApproval;
    item.isActive = true;
    s.items.set(ev.roomId, item);
  } else if (et == C.EventRoomUpdated) {
    const ev = JSON.parse<RoomUpdatedEv>(p);
    if (s.items.has(ev.roomId)) { const item = s.items.get(ev.roomId); item.name = ev.name; item.capacity = ev.capacity; item.location = ev.location; item.equipment = ev.equipment; item.requiresApproval = ev.requiresApproval; s.items.set(ev.roomId, item); }
  }
  return s;
}

function applyReservationList(s: ReservationListState, et: string, p: string): ReservationListState {
  if (et == C.EventReservationDraftCreated) {
    const ev = JSON.parse<ReservationDraftCreatedEv>(p);
    const item = new ReservationListItem();
    item.reservationId = ev.reservationId; item.roomId = ev.roomId;
    item.organizerId = ev.organizerId; item.organizerName = ev.organizerName;
    item.startTime = ev.startTime; item.endTime = ev.endTime; item.purpose = ev.purpose;
    item.selectedEquipment = ev.selectedEquipment; item.status = "Draft";
    s.items.set(ev.reservationId, item);
  } else if (et == C.EventReservationHoldCommitted) {
    const ev = JSON.parse<ReservationHoldCommittedEv>(p);
    if (s.items.has(ev.reservationId)) { const item = s.items.get(ev.reservationId); item.status = "Held"; item.requiresApproval = ev.requiresApproval; item.approvalRequestId = ev.approvalRequestId; if (ev.selectedEquipment.length > 0) item.selectedEquipment = ev.selectedEquipment; s.items.set(ev.reservationId, item); }
  } else if (et == C.EventReservationConfirmed) {
    const ev = JSON.parse<ReservationConfirmedEv>(p);
    if (s.items.has(ev.reservationId)) { const item = s.items.get(ev.reservationId); item.status = "Confirmed"; item.confirmedAt = ev.confirmedAt; s.items.set(ev.reservationId, item); }
  } else if (et == C.EventReservationCancelled) {
    const ev = JSON.parse<ReservationCancelledEv>(p);
    if (s.items.has(ev.reservationId)) { const item = s.items.get(ev.reservationId); item.status = "Cancelled"; item.cancelledAt = ev.cancelledAt; s.items.set(ev.reservationId, item); }
  } else if (et == C.EventReservationRejected) {
    const ev = JSON.parse<ReservationRejectedEv>(p);
    if (s.items.has(ev.reservationId)) { const item = s.items.get(ev.reservationId); item.status = "Rejected"; item.rejectedAt = ev.rejectedAt; s.items.set(ev.reservationId, item); }
  }
  return s;
}

function applyApprovalList(s: ApprovalRequestListState, et: string, p: string): ApprovalRequestListState {
  if (et == C.EventApprovalFlowStarted) {
    const ev = JSON.parse<ApprovalFlowStartedEv>(p);
    const item = new ApprovalInboxItem();
    item.approvalRequestId = ev.approvalRequestId; item.reservationId = ev.reservationId;
    item.roomId = ev.roomId; item.requesterId = ev.requesterId; item.approverIds = ev.approverIds;
    item.requestedAt = ev.requestedAt; item.requestComment = ev.requestComment; item.status = "Pending";
    s.items.set(ev.approvalRequestId, item);
  } else if (et == C.EventApprovalDecisionRecorded) {
    const ev = JSON.parse<ApprovalDecisionRecordedEv>(p);
    if (s.items.has(ev.approvalRequestId)) { const item = s.items.get(ev.approvalRequestId); item.approverId = ev.approverId; item.decisionComment = ev.comment; item.decidedAt = ev.decidedAt; item.status = ev.decision == "Approved" ? "Approved" : "Rejected"; s.items.set(ev.approvalRequestId, item); }
  }
  return s;
}

// ---------------------------------------------------------------------------
// apply_event export
// ---------------------------------------------------------------------------

export function apply_event(instanceId: i32, eventTypePtr: u32, eventTypeLen: u32, payloadPtr: u32, payloadLen: u32): void {
  if (!instances.has(instanceId)) return;
  const inst = instances.get(instanceId);
  const eventType = readStr(eventTypePtr, eventTypeLen);
  const payload = readStr(payloadPtr, payloadLen);

  switch (inst.kind) {
    case C.KIND_WEATHER_TAG: inst.weatherTag = applyWeatherTag(inst.weatherTag, eventType, payload); break;
    case C.KIND_STUDENT_TAG: inst.studentTag = applyStudentTag(inst.studentTag, eventType, payload); break;
    case C.KIND_CLASSROOM_TAG: inst.classRoomTag = applyClassRoomTag(inst.classRoomTag, eventType, payload); break;
    case C.KIND_USER_DIRECTORY_TAG: inst.userDirectoryTag = applyUserDirectoryTag(inst.userDirectoryTag, eventType, payload); break;
    case C.KIND_USER_ACCESS_TAG: inst.userAccessTag = applyUserAccessTag(inst.userAccessTag, eventType, payload); break;
    case C.KIND_ROOM_TAG: inst.roomTag = applyRoomTag(inst.roomTag, eventType, payload); break;
    case C.KIND_ROOM_RESERVATION_TAG: inst.roomReservationsTag = applyRoomReservationsTag(inst.roomReservationsTag, eventType, payload); break;
    case C.KIND_RESERVATION_TAG: inst.reservationTag = applyReservationTag(inst.reservationTag, eventType, payload); break;
    case C.KIND_APPROVAL_TAG: inst.approvalRequestTag = applyApprovalTag(inst.approvalRequestTag, eventType, payload); break;
    case C.KIND_WEATHER_LIST: inst.weatherList = applyWeatherList(inst.weatherList, eventType, payload); break;
    case C.KIND_STUDENT_LIST: inst.studentList = applyStudentList(inst.studentList, eventType, payload); break;
    case C.KIND_CLASSROOM_LIST: inst.classRoomList = applyClassRoomList(inst.classRoomList, eventType, payload); break;
    case C.KIND_USER_DIRECTORY_LIST: inst.userDirectoryList = applyUserDirectoryList(inst.userDirectoryList, eventType, payload); break;
    case C.KIND_USER_ACCESS_LIST: inst.userAccessList = applyUserAccessList(inst.userAccessList, eventType, payload); break;
    case C.KIND_ROOM_LIST: inst.roomList = applyRoomList(inst.roomList, eventType, payload); break;
    case C.KIND_RESERVATION_LIST: inst.reservationList = applyReservationList(inst.reservationList, eventType, payload); break;
    case C.KIND_APPROVAL_LIST: inst.approvalList = applyApprovalList(inst.approvalList, eventType, payload); break;
  }
}

// ---------------------------------------------------------------------------
// serialize_state / restore_state
// ---------------------------------------------------------------------------

function serializeMapState<V>(items: Map<string, V>): string {
  const keys = items.keys();
  let result = '{"items":{';
  for (let i = 0; i < keys.length; i++) {
    if (i > 0) result += ",";
    result += '"' + keys[i] + '":' + JSON.stringify<V>(items.get(keys[i]));
  }
  result += "}}";
  return result;
}

export function serialize_state(instanceId: i32): u64 {
  if (!instances.has(instanceId)) return writeStr("{}");
  const inst = instances.get(instanceId);
  let json = "{}";
  switch (inst.kind) {
    case C.KIND_WEATHER_TAG: json = JSON.stringify<WeatherForecastState>(inst.weatherTag); break;
    case C.KIND_STUDENT_TAG: json = JSON.stringify<StudentState>(inst.studentTag); break;
    case C.KIND_CLASSROOM_TAG: json = JSON.stringify<ClassRoomState>(inst.classRoomTag); break;
    case C.KIND_USER_DIRECTORY_TAG: json = JSON.stringify<UserDirectoryState>(inst.userDirectoryTag); break;
    case C.KIND_USER_ACCESS_TAG: json = JSON.stringify<UserAccessState>(inst.userAccessTag); break;
    case C.KIND_ROOM_TAG: json = JSON.stringify<RoomState>(inst.roomTag); break;
    case C.KIND_ROOM_RESERVATION_TAG: json = JSON.stringify<RoomReservationsState>(inst.roomReservationsTag); break;
    case C.KIND_RESERVATION_TAG: json = JSON.stringify<ReservationState>(inst.reservationTag); break;
    case C.KIND_APPROVAL_TAG: json = JSON.stringify<ApprovalRequestState>(inst.approvalRequestTag); break;
    case C.KIND_WEATHER_LIST: json = serializeMapState<WeatherForecastItem>(inst.weatherList.items); break;
    case C.KIND_STUDENT_LIST: json = serializeMapState<StudentState>(inst.studentList.items); break;
    case C.KIND_CLASSROOM_LIST: json = serializeMapState<ClassRoomItem>(inst.classRoomList.items); break;
    case C.KIND_USER_DIRECTORY_LIST: json = serializeMapState<UserDirectoryListItem>(inst.userDirectoryList.items); break;
    case C.KIND_USER_ACCESS_LIST: json = serializeMapState<UserAccessListItem>(inst.userAccessList.items); break;
    case C.KIND_ROOM_LIST: json = serializeMapState<RoomListItem>(inst.roomList.items); break;
    case C.KIND_RESERVATION_LIST: json = serializeMapState<ReservationListItem>(inst.reservationList.items); break;
    case C.KIND_APPROVAL_LIST: json = serializeMapState<ApprovalInboxItem>(inst.approvalList.items); break;
  }
  return writeStr(json);
}

function restoreMapState<V>(stateJson: string): Map<string, V> {
  const m = new Map<string, V>();
  // Find "items":{ and parse individual entries
  const itemsIdx = stateJson.indexOf('"items"');
  if (itemsIdx < 0) return m;
  let braceStart = stateJson.indexOf("{", itemsIdx + 7);
  if (braceStart < 0) return m;

  // Simple JSON object iteration - find key:value pairs
  let pos = braceStart + 1;
  while (pos < stateJson.length) {
    // Skip whitespace
    while (pos < stateJson.length && (stateJson.charCodeAt(pos) == 32 || stateJson.charCodeAt(pos) == 10 || stateJson.charCodeAt(pos) == 13 || stateJson.charCodeAt(pos) == 9)) pos++;
    if (pos >= stateJson.length || stateJson.charCodeAt(pos) == 125) break; // '}'
    if (stateJson.charCodeAt(pos) == 44) { pos++; continue; } // ','

    // Parse key
    if (stateJson.charCodeAt(pos) != 34) break; // not '"'
    const keyEnd = stateJson.indexOf('"', pos + 1);
    if (keyEnd < 0) break;
    const key = stateJson.substring(pos + 1, keyEnd);
    pos = keyEnd + 1;

    // Skip ':'
    while (pos < stateJson.length && stateJson.charCodeAt(pos) != 58) pos++;
    pos++; // skip ':'

    // Find value (object) - count braces
    while (pos < stateJson.length && stateJson.charCodeAt(pos) != 123) pos++;
    const valStart = pos;
    let depth = 0;
    while (pos < stateJson.length) {
      if (stateJson.charCodeAt(pos) == 123) depth++;
      else if (stateJson.charCodeAt(pos) == 125) { depth--; if (depth == 0) { pos++; break; } }
      pos++;
    }
    const valJson = stateJson.substring(valStart, pos);
    m.set(key, JSON.parse<V>(valJson));
  }
  return m;
}

export function restore_state(instanceId: i32, statePtr: u32, stateLen: u32): void {
  if (!instances.has(instanceId)) return;
  const inst = instances.get(instanceId);
  const stateJson = readStr(statePtr, stateLen);
  if (stateJson.length == 0 || stateJson == "{}" || stateJson == "null") return;

  switch (inst.kind) {
    case C.KIND_WEATHER_TAG: inst.weatherTag = JSON.parse<WeatherForecastState>(stateJson); break;
    case C.KIND_STUDENT_TAG: inst.studentTag = JSON.parse<StudentState>(stateJson); break;
    case C.KIND_CLASSROOM_TAG: inst.classRoomTag = JSON.parse<ClassRoomState>(stateJson); break;
    case C.KIND_USER_DIRECTORY_TAG: inst.userDirectoryTag = JSON.parse<UserDirectoryState>(stateJson); break;
    case C.KIND_USER_ACCESS_TAG: inst.userAccessTag = JSON.parse<UserAccessState>(stateJson); break;
    case C.KIND_ROOM_TAG: inst.roomTag = JSON.parse<RoomState>(stateJson); break;
    case C.KIND_ROOM_RESERVATION_TAG: inst.roomReservationsTag = JSON.parse<RoomReservationsState>(stateJson); break;
    case C.KIND_RESERVATION_TAG: inst.reservationTag = JSON.parse<ReservationState>(stateJson); break;
    case C.KIND_APPROVAL_TAG: inst.approvalRequestTag = JSON.parse<ApprovalRequestState>(stateJson); break;
    case C.KIND_WEATHER_LIST: inst.weatherList.items = restoreMapState<WeatherForecastItem>(stateJson); break;
    case C.KIND_STUDENT_LIST: inst.studentList.items = restoreMapState<StudentState>(stateJson); break;
    case C.KIND_CLASSROOM_LIST: inst.classRoomList.items = restoreMapState<ClassRoomItem>(stateJson); break;
    case C.KIND_USER_DIRECTORY_LIST: inst.userDirectoryList.items = restoreMapState<UserDirectoryListItem>(stateJson); break;
    case C.KIND_USER_ACCESS_LIST: inst.userAccessList.items = restoreMapState<UserAccessListItem>(stateJson); break;
    case C.KIND_ROOM_LIST: inst.roomList.items = restoreMapState<RoomListItem>(stateJson); break;
    case C.KIND_RESERVATION_LIST: inst.reservationList.items = restoreMapState<ReservationListItem>(stateJson); break;
    case C.KIND_APPROVAL_LIST: inst.approvalList.items = restoreMapState<ApprovalInboxItem>(stateJson); break;
  }
}

// ---------------------------------------------------------------------------
// Query execution
// ---------------------------------------------------------------------------

@json class PagingQuery { pageSize: i32 = 0; pageNumber: i32 = 0; }
@json class WeatherListQuery { locationFilter: string = ""; forecastId: string = ""; waitForSortableUniqueId: string = ""; pageSize: i32 = 0; pageNumber: i32 = 0; }
@json class ReservationListQuery { roomId: string = ""; pageSize: i32 = 0; pageNumber: i32 = 0; }
@json class ApprovalListQuery { pendingOnly: bool = false; pageSize: i32 = 0; pageNumber: i32 = 0; }
@json class UserDirectoryListQuery { activeOnly: bool = false; pageSize: i32 = 0; pageNumber: i32 = 0; }
@json class UserAccessListQuery { activeOnly: bool = false; roleFilter: string = ""; pageSize: i32 = 0; pageNumber: i32 = 0; }

@json class CountResult { count: i32 = 0; }

function executeWeatherListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<WeatherListQuery>(paramsJson);
  const items = inst.weatherList.items;
  const result: WeatherForecastItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    const item = items.get(keys[i]);
    if (item.isDeleted) continue;
    if (q.locationFilter.length > 0 && !item.location.toLowerCase().includes(q.locationFilter.toLowerCase())) continue;
    if (q.forecastId.length > 0 && item.forecastId != q.forecastId) continue;
    result.push(item);
  }
  // Sort by date descending
  result.sort((a, b) => a.date < b.date ? 1 : a.date > b.date ? -1 : 0);
  const paged = applyPaging<WeatherForecastItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<WeatherForecastItem[]>(paged);
}

function executeWeatherCountQuery(inst: Instance, paramsJson: string): string {
  const items = inst.weatherList.items;
  let count: i32 = 0;
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    if (!items.get(keys[i]).isDeleted) count++;
  }
  const r = new CountResult();
  r.count = count;
  return JSON.stringify<CountResult>(r);
}

function executeStudentListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<PagingQuery>(paramsJson);
  const items = inst.studentList.items;
  const result: StudentState[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) result.push(items.get(keys[i]));
  result.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
  const paged = applyPaging<StudentState>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<StudentState[]>(paged);
}

function executeClassRoomListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<PagingQuery>(paramsJson);
  const items = inst.classRoomList.items;
  const result: ClassRoomItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) result.push(items.get(keys[i]));
  result.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
  const paged = applyPaging<ClassRoomItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<ClassRoomItem[]>(paged);
}

function executeRoomListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<PagingQuery>(paramsJson);
  const items = inst.roomList.items;
  const result: RoomListItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) result.push(items.get(keys[i]));
  result.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
  const paged = applyPaging<RoomListItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<RoomListItem[]>(paged);
}

function executeReservationListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<ReservationListQuery>(paramsJson);
  const items = inst.reservationList.items;
  const result: ReservationListItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    const item = items.get(keys[i]);
    if (q.roomId.length > 0 && item.roomId != q.roomId) continue;
    result.push(item);
  }
  result.sort((a, b) => a.startTime < b.startTime ? 1 : a.startTime > b.startTime ? -1 : 0);
  const paged = applyPaging<ReservationListItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<ReservationListItem[]>(paged);
}

function executeApprovalInboxQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<ApprovalListQuery>(paramsJson);
  const items = inst.approvalList.items;
  const result: ApprovalInboxItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    const item = items.get(keys[i]);
    if (q.pendingOnly && item.status != "Pending") continue;
    result.push(item);
  }
  result.sort((a, b) => a.requestedAt < b.requestedAt ? 1 : a.requestedAt > b.requestedAt ? -1 : 0);
  const paged = applyPaging<ApprovalInboxItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<ApprovalInboxItem[]>(paged);
}

function executeUserDirectoryListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<UserDirectoryListQuery>(paramsJson);
  const items = inst.userDirectoryList.items;
  const result: UserDirectoryListItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    const item = items.get(keys[i]);
    if (q.activeOnly && !item.isActive) continue;
    result.push(item);
  }
  result.sort((a, b) => a.displayName < b.displayName ? -1 : a.displayName > b.displayName ? 1 : 0);
  const paged = applyPaging<UserDirectoryListItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<UserDirectoryListItem[]>(paged);
}

function executeUserAccessListQuery(inst: Instance, paramsJson: string): string {
  const q = JSON.parse<UserAccessListQuery>(paramsJson);
  const items = inst.userAccessList.items;
  const result: UserAccessListItem[] = [];
  const keys = items.keys();
  for (let i = 0; i < keys.length; i++) {
    const item = items.get(keys[i]);
    if (q.activeOnly && !item.isActive) continue;
    if (q.roleFilter.length > 0 && !item.roles.includes(q.roleFilter)) continue;
    result.push(item);
  }
  result.sort((a, b) => a.grantedAt < b.grantedAt ? 1 : a.grantedAt > b.grantedAt ? -1 : 0);
  const paged = applyPaging<UserAccessListItem>(result, q.pageSize, q.pageNumber);
  return JSON.stringify<UserAccessListItem[]>(paged);
}

// ---------------------------------------------------------------------------
// execute_query / execute_list_query exports
// ---------------------------------------------------------------------------

export function execute_query(instanceId: i32, queryTypePtr: u32, queryTypeLen: u32, paramsPtr: u32, paramsLen: u32): u64 {
  if (!instances.has(instanceId)) return writeStr("{}");
  const inst = instances.get(instanceId);
  const queryType = readStr(queryTypePtr, queryTypeLen);
  const paramsJson = readStr(paramsPtr, paramsLen);

  if (queryType == "GetWeatherForecastCountQuery") return writeStr(executeWeatherCountQuery(inst, paramsJson));
  return writeStr("{}");
}

export function execute_list_query(instanceId: i32, queryTypePtr: u32, queryTypeLen: u32, paramsPtr: u32, paramsLen: u32): u64 {
  if (!instances.has(instanceId)) return writeStr("[]");
  const inst = instances.get(instanceId);
  const queryType = readStr(queryTypePtr, queryTypeLen);
  const paramsJson = readStr(paramsPtr, paramsLen);

  if (queryType == "GetWeatherForecastListQuery") return writeStr(executeWeatherListQuery(inst, paramsJson));
  if (queryType == "GetStudentListQuery") return writeStr(executeStudentListQuery(inst, paramsJson));
  if (queryType == "GetClassRoomListQuery") return writeStr(executeClassRoomListQuery(inst, paramsJson));
  if (queryType == "GetRoomListQuery") return writeStr(executeRoomListQuery(inst, paramsJson));
  if (queryType == "GetReservationListQuery") return writeStr(executeReservationListQuery(inst, paramsJson));
  if (queryType == "GetApprovalInboxQuery") return writeStr(executeApprovalInboxQuery(inst, paramsJson));
  if (queryType == "GetUserDirectoryListQuery") return writeStr(executeUserDirectoryListQuery(inst, paramsJson));
  if (queryType == "GetUserAccessListQuery") return writeStr(executeUserAccessListQuery(inst, paramsJson));
  return writeStr("[]");
}

// ---------------------------------------------------------------------------
// get_event_types export
// ---------------------------------------------------------------------------

export function get_event_types(): u64 {
  const types = '["WeatherForecastCreated","WeatherForecastLocationUpdated","WeatherForecastDeleted","StudentCreated","ClassRoomCreated","StudentEnrolledInClassRoom","StudentDroppedFromClassRoom","UserRegistered","UserProfileUpdated","UserAccessGranted","UserRoleGranted","RoomCreated","RoomUpdated","ReservationDraftCreated","ReservationHoldCommitted","ReservationConfirmed","ReservationCancelled","ReservationRejected","ApprovalFlowStarted","ApprovalDecisionRecorded"]';
  return writeStr(types);
}
