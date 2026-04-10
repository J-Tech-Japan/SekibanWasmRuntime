import { serve } from "@hono/node-server";
import { Hono } from "hono";
import { logger } from "hono/logger";
import { v4 as uuidv4 } from "uuid";
import {
  SekibanRuntimeClient,
  type Command,
  type CommandContext,
  type CommandOutput,
  type EventOutput,
  tagString,
  isEmptyJSON,
  newCommandOutput,
  AlreadyExistsError,
  NotFoundError,
  ValidationError,
} from "@sekiban/ts";

// ---------------------------------------------------------------------------
// Constants (matching Go constants.go)
// ---------------------------------------------------------------------------

// Event types
const EventWeatherForecastCreated = "WeatherForecastCreated";
const EventWeatherForecastLocationUpdated = "WeatherForecastLocationUpdated";
const EventWeatherForecastDeleted = "WeatherForecastDeleted";
const EventStudentCreated = "StudentCreated";
const EventClassRoomCreated = "ClassRoomCreated";
const EventStudentEnrolledInClassRoom = "StudentEnrolledInClassRoom";
const EventStudentDroppedFromClassRoom = "StudentDroppedFromClassRoom";
const EventUserRegistered = "UserRegistered";
const EventUserProfileUpdated = "UserProfileUpdated";
const EventUserAccessGranted = "UserAccessGranted";
const EventUserRoleGranted = "UserRoleGranted";
const EventRoomCreated = "RoomCreated";
const EventRoomUpdated = "RoomUpdated";
const EventReservationDraftCreated = "ReservationDraftCreated";
const EventReservationHoldCommitted = "ReservationHoldCommitted";
const EventReservationConfirmed = "ReservationConfirmed";
const EventReservationCancelled = "ReservationCancelled";
const EventReservationRejected = "ReservationRejected";
const EventApprovalFlowStarted = "ApprovalFlowStarted";
const EventApprovalDecisionRecorded = "ApprovalDecisionRecorded";

// Tag groups
const TagGroupWeather = "weather";
const TagGroupStudent = "Student";
const TagGroupClassRoom = "ClassRoom";
const TagGroupUser = "User";
const TagGroupUserAccess = "UserAccess";
const TagGroupRoom = "Room";
const TagGroupRoomReservation = "RoomReservation";
const TagGroupReservation = "Reservation";
const TagGroupApprovalRequest = "ApprovalRequest";

// Tag projector map
const tagProjectorMap: Record<string, string> = {
  [TagGroupWeather]: "WeatherForecastProjector",
  [TagGroupStudent]: "StudentProjector",
  [TagGroupClassRoom]: "ClassRoomProjector",
  [TagGroupUser]: "UserDirectoryProjector",
  [TagGroupUserAccess]: "UserAccessProjector",
  [TagGroupRoom]: "RoomProjector",
  [TagGroupRoomReservation]: "RoomReservationsProjector",
  [TagGroupReservation]: "ReservationProjector",
  [TagGroupApprovalRequest]: "ApprovalRequestProjector",
};

// ---------------------------------------------------------------------------
// State types (for parsing tag state JSON)
// ---------------------------------------------------------------------------

interface WeatherForecastState {
  forecastId?: string;
}

interface StudentState {
  studentId?: string;
  enrolledClassRoomIds?: string[];
  maxClassCount?: number;
  currentClassCount?: number;
}

interface ClassRoomState {
  classRoomId?: string;
  maxStudents?: number;
  currentStudentCount?: number;
  enrolledStudentIds?: string[];
}

interface UserDirectoryState {
  userId?: string;
  displayName?: string;
  email?: string;
  department?: string | null;
  monthlyReservationLimit?: number;
}

interface UserAccessState {
  userId?: string;
}

interface RoomState {
  roomId?: string;
  requiresApproval?: boolean;
}

interface ReservationState {
  reservationId?: string;
  roomId?: string;
  organizerId?: string;
  organizerName?: string;
  startTime?: string;
  endTime?: string;
  purpose?: string;
  selectedEquipment?: string[];
  status?: string;
  approvalRequestComment?: string | null;
}

interface ApprovalRequestState {
  approvalRequestId?: string;
  reservationId?: string;
}

interface ReservationSlot {
  startTime: string;
  endTime: string;
}

interface RoomReservationsState {
  activeReservations?: Record<string, ReservationSlot>;
}

// ---------------------------------------------------------------------------
// State parsers
// ---------------------------------------------------------------------------

function parseState<T>(raw: string): T | null {
  if (isEmptyJSON(raw)) return null;
  return JSON.parse(raw) as T;
}

function isEmpty(state: Record<string, any> | null, idField: string): boolean {
  if (state === null) return true;
  const id = (state as any)[idField];
  return !id || id === "";
}

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

function nowIso(): string {
  return new Date().toISOString();
}

function parseIsoDate(value: string, fieldName: string): Date {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    throw new ValidationError(`${fieldName} must be a valid ISO-8601 timestamp`);
  }
  return parsed;
}

function hasRoomReservationConflict(
  state: RoomReservationsState | null,
  startTime: string,
  endTime: string,
  excludeReservationId?: string | null,
): boolean {
  const start = parseIsoDate(startTime, "startTime").getTime();
  const end = parseIsoDate(endTime, "endTime").getTime();
  if (start >= end) {
    throw new ValidationError("startTime must be before endTime");
  }

  for (const [reservationId, slot] of Object.entries(state?.activeReservations ?? {})) {
    if (excludeReservationId && reservationId === excludeReservationId) {
      continue;
    }

    const slotStart = parseIsoDate(slot.startTime, "slot.startTime").getTime();
    const slotEnd = parseIsoDate(slot.endTime, "slot.endTime").getTime();
    if (start < slotEnd && slotStart < end) {
      return true;
    }
  }

  return false;
}

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

// 1. CreateWeatherForecast
class CreateWeatherForecast implements Command {
  forecastId?: string | null;
  location: string;
  date: string;
  temperatureC: number;
  summary?: string | null;

  constructor(data: { forecastId?: string | null; location: string; date: string; temperatureC: number; summary?: string | null }) {
    this.forecastId = data.forecastId;
    this.location = data.location;
    this.date = data.date;
    this.temperatureC = data.temperatureC;
    this.summary = data.summary;
  }

  commandType(): string { return "CreateWeatherForecast"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const id = (this.forecastId && this.forecastId !== "") ? this.forecastId : uuidv4();
    const tag = tagString(TagGroupWeather, id);

    const resp = await ctx.getTagState(TagGroupWeather, id);
    const state = parseState<WeatherForecastState>(resp.stateJson);
    if (!isEmpty(state, "forecastId")) {
      throw new AlreadyExistsError(`weather forecast ${id}`);
    }

    return newCommandOutput(
      EventWeatherForecastCreated,
      {
        forecastId: id,
        location: this.location,
        date: this.date,
        temperatureC: this.temperatureC,
        summary: this.summary ?? "",
        createdAt: nowIso(),
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 2. UpdateWeatherForecastLocation
class UpdateWeatherForecastLocation implements Command {
  forecastId: string;
  newLocation: string;

  constructor(data: { forecastId: string; newLocation: string }) {
    this.forecastId = data.forecastId;
    this.newLocation = data.newLocation;
  }

  commandType(): string { return "UpdateWeatherForecastLocation"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const tag = tagString(TagGroupWeather, this.forecastId);

    const resp = await ctx.getTagState(TagGroupWeather, this.forecastId);
    const state = parseState<WeatherForecastState>(resp.stateJson);
    if (isEmpty(state, "forecastId")) {
      throw new NotFoundError(`weather forecast ${this.forecastId}`);
    }

    return newCommandOutput(
      EventWeatherForecastLocationUpdated,
      {
        forecastId: this.forecastId,
        newLocation: this.newLocation,
        updatedAt: nowIso(),
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 3. DeleteWeatherForecast
class DeleteWeatherForecast implements Command {
  forecastId: string;

  constructor(data: { forecastId: string }) {
    this.forecastId = data.forecastId;
  }

  commandType(): string { return "DeleteWeatherForecast"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const tag = tagString(TagGroupWeather, this.forecastId);

    const resp = await ctx.getTagState(TagGroupWeather, this.forecastId);
    const state = parseState<WeatherForecastState>(resp.stateJson);
    if (isEmpty(state, "forecastId")) {
      throw new NotFoundError(`weather forecast ${this.forecastId}`);
    }

    return newCommandOutput(
      EventWeatherForecastDeleted,
      {
        forecastId: this.forecastId,
        deletedAt: nowIso(),
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 4. CreateStudent
class CreateStudent implements Command {
  studentId?: string | null;
  name: string;
  maxClassCount: number;

  constructor(data: { studentId?: string | null; name: string; maxClassCount: number }) {
    this.studentId = data.studentId;
    this.name = data.name;
    this.maxClassCount = data.maxClassCount;
  }

  commandType(): string { return "CreateStudent"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    if (this.maxClassCount < 1) {
      throw new ValidationError("maxClassCount must be at least 1");
    }

    const id = (this.studentId && this.studentId !== "") ? this.studentId : uuidv4();
    const tag = tagString(TagGroupStudent, id);

    const resp = await ctx.getTagState(TagGroupStudent, id);
    const state = parseState<StudentState>(resp.stateJson);
    if (!isEmpty(state, "studentId")) {
      throw new AlreadyExistsError(`student ${id}`);
    }

    return newCommandOutput(
      EventStudentCreated,
      {
        studentId: id,
        name: this.name,
        maxClassCount: this.maxClassCount,
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 5. CreateClassRoom
class CreateClassRoom implements Command {
  classRoomId?: string | null;
  name: string;
  maxStudents: number;

  constructor(data: { classRoomId?: string | null; name: string; maxStudents: number }) {
    this.classRoomId = data.classRoomId;
    this.name = data.name;
    this.maxStudents = data.maxStudents;
  }

  commandType(): string { return "CreateClassRoom"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    if (this.maxStudents < 1) {
      throw new ValidationError("maxStudents must be at least 1");
    }

    const id = (this.classRoomId && this.classRoomId !== "") ? this.classRoomId : uuidv4();
    const tag = tagString(TagGroupClassRoom, id);

    const resp = await ctx.getTagState(TagGroupClassRoom, id);
    const state = parseState<ClassRoomState>(resp.stateJson);
    if (!isEmpty(state, "classRoomId")) {
      throw new AlreadyExistsError(`classroom ${id}`);
    }

    return newCommandOutput(
      EventClassRoomCreated,
      {
        classRoomId: id,
        name: this.name,
        maxStudents: this.maxStudents,
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 6. EnrollStudentInClassRoom
class EnrollStudentInClassRoom implements Command {
  studentId: string;
  classRoomId: string;

  constructor(data: { studentId: string; classRoomId: string }) {
    this.studentId = data.studentId;
    this.classRoomId = data.classRoomId;
  }

  commandType(): string { return "EnrollStudentInClassRoom"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const studentTag = tagString(TagGroupStudent, this.studentId);
    const classTag = tagString(TagGroupClassRoom, this.classRoomId);

    const studentResp = await ctx.getTagState(TagGroupStudent, this.studentId);
    const student = parseState<StudentState>(studentResp.stateJson);
    if (isEmpty(student, "studentId")) {
      throw new NotFoundError(`student ${this.studentId}`);
    }

    const classResp = await ctx.getTagState(TagGroupClassRoom, this.classRoomId);
    const cls = parseState<ClassRoomState>(classResp.stateJson);
    if (isEmpty(cls, "classRoomId")) {
      throw new NotFoundError(`classroom ${this.classRoomId}`);
    }

    const classRemaining = (cls!.maxStudents ?? 0) - (cls!.currentStudentCount ?? 0);
    if (classRemaining <= 0) {
      throw new ValidationError(`classroom ${this.classRoomId} is full`);
    }

    const studentRemaining = (student!.maxClassCount ?? 0) - (student!.currentClassCount ?? 0);
    if (studentRemaining <= 0) {
      throw new ValidationError(`student ${this.studentId} has reached max class count`);
    }

    if (student!.enrolledClassRoomIds && student!.enrolledClassRoomIds.includes(this.classRoomId)) {
      throw new ValidationError(`student ${this.studentId} already enrolled in classroom ${this.classRoomId}`);
    }

    return newCommandOutput(
      EventStudentEnrolledInClassRoom,
      {
        studentId: this.studentId,
        classRoomId: this.classRoomId,
      },
      [studentTag, classTag], [studentTag, classTag],
      { [studentTag]: studentResp.version, [classTag]: classResp.version },
    );
  }
}

// 7. DropStudentFromClassRoom
class DropStudentFromClassRoom implements Command {
  studentId: string;
  classRoomId: string;

  constructor(data: { studentId: string; classRoomId: string }) {
    this.studentId = data.studentId;
    this.classRoomId = data.classRoomId;
  }

  commandType(): string { return "DropStudentFromClassRoom"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const studentTag = tagString(TagGroupStudent, this.studentId);
    const classTag = tagString(TagGroupClassRoom, this.classRoomId);

    const studentResp = await ctx.getTagState(TagGroupStudent, this.studentId);
    const student = parseState<StudentState>(studentResp.stateJson);
    if (isEmpty(student, "studentId")) {
      throw new NotFoundError(`student ${this.studentId}`);
    }

    const classResp = await ctx.getTagState(TagGroupClassRoom, this.classRoomId);
    const cls = parseState<ClassRoomState>(classResp.stateJson);
    if (isEmpty(cls, "classRoomId")) {
      throw new NotFoundError(`classroom ${this.classRoomId}`);
    }

    if (!student!.enrolledClassRoomIds || !student!.enrolledClassRoomIds.includes(this.classRoomId)) {
      throw new ValidationError(`student ${this.studentId} not enrolled in classroom ${this.classRoomId}`);
    }

    return newCommandOutput(
      EventStudentDroppedFromClassRoom,
      {
        studentId: this.studentId,
        classRoomId: this.classRoomId,
      },
      [studentTag, classTag], [studentTag, classTag],
      { [studentTag]: studentResp.version, [classTag]: classResp.version },
    );
  }
}

// 8. RegisterUser
class RegisterUser implements Command {
  userId?: string | null;
  displayName: string;
  email: string;
  department?: string | null;
  monthlyReservationLimit: number;

  constructor(data: { userId?: string | null; displayName: string; email: string; department?: string | null; monthlyReservationLimit: number }) {
    this.userId = data.userId;
    this.displayName = data.displayName;
    this.email = data.email;
    this.department = data.department;
    this.monthlyReservationLimit = data.monthlyReservationLimit;
  }

  commandType(): string { return "RegisterUser"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const id = (this.userId && this.userId !== "") ? this.userId : uuidv4();
    const tag = tagString(TagGroupUser, id);

    const resp = await ctx.getTagState(TagGroupUser, id);
    const state = parseState<UserDirectoryState>(resp.stateJson);
    if (!isEmpty(state, "userId")) {
      throw new AlreadyExistsError(`user ${id}`);
    }

    return newCommandOutput(
      EventUserRegistered,
      {
        userId: id,
        displayName: this.displayName,
        email: this.email,
        department: this.department ?? null,
        registeredAt: nowIso(),
        monthlyReservationLimit: this.monthlyReservationLimit,
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 9. UpdateUserMonthlyReservationLimit
class UpdateUserMonthlyReservationLimit implements Command {
  userId: string;
  monthlyReservationLimit: number;

  constructor(data: { userId: string; monthlyReservationLimit: number }) {
    this.userId = data.userId;
    this.monthlyReservationLimit = data.monthlyReservationLimit;
  }

  commandType(): string { return "UpdateUserMonthlyReservationLimit"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const tag = tagString(TagGroupUser, this.userId);

    const resp = await ctx.getTagState(TagGroupUser, this.userId);
    const state = parseState<UserDirectoryState>(resp.stateJson);
    if (isEmpty(state, "userId")) {
      throw new NotFoundError(`user ${this.userId}`);
    }

    return newCommandOutput(
      EventUserProfileUpdated,
      {
        userId: this.userId,
        displayName: state!.displayName,
        email: state!.email,
        department: state!.department ?? null,
        monthlyReservationLimit: this.monthlyReservationLimit,
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 10. GrantUserAccess
class GrantUserAccess implements Command {
  userId: string;
  initialRole: string;

  constructor(data: { userId: string; initialRole: string }) {
    this.userId = data.userId;
    this.initialRole = data.initialRole;
  }

  commandType(): string { return "GrantUserAccess"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const userResp = await ctx.getTagState(TagGroupUser, this.userId);
    const user = parseState<UserDirectoryState>(userResp.stateJson);
    if (isEmpty(user, "userId")) {
      throw new NotFoundError(`user ${this.userId}`);
    }

    const accessTag = tagString(TagGroupUserAccess, this.userId);

    return newCommandOutput(
      EventUserAccessGranted,
      {
        userId: this.userId,
        initialRole: this.initialRole,
        grantedAt: nowIso(),
      },
      [accessTag], [accessTag],
      {},
    );
  }
}

// 11. GrantUserRole
class GrantUserRole implements Command {
  userId: string;
  role: string;

  constructor(data: { userId: string; role: string }) {
    this.userId = data.userId;
    this.role = data.role;
  }

  commandType(): string { return "GrantUserRole"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const accessTag = tagString(TagGroupUserAccess, this.userId);

    const resp = await ctx.getTagState(TagGroupUserAccess, this.userId);
    const state = parseState<UserAccessState>(resp.stateJson);
    if (isEmpty(state, "userId")) {
      throw new NotFoundError(`user access for ${this.userId}`);
    }

    return newCommandOutput(
      EventUserRoleGranted,
      {
        userId: this.userId,
        role: this.role,
        grantedAt: nowIso(),
      },
      [accessTag], [accessTag],
      { [accessTag]: resp.version },
    );
  }
}

// 12. CreateRoom
class CreateRoom implements Command {
  roomId?: string | null;
  name: string;
  capacity: number;
  location: string;
  equipment: string[];
  requiresApproval: boolean;

  constructor(data: { roomId?: string | null; name: string; capacity: number; location: string; equipment?: string[]; requiresApproval: boolean }) {
    this.roomId = data.roomId;
    this.name = data.name;
    this.capacity = data.capacity;
    this.location = data.location;
    this.equipment = data.equipment ?? [];
    this.requiresApproval = data.requiresApproval;
  }

  commandType(): string { return "CreateRoom"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const id = (this.roomId && this.roomId !== "") ? this.roomId : uuidv4();
    const tag = tagString(TagGroupRoom, id);

    const resp = await ctx.getTagState(TagGroupRoom, id);
    const state = parseState<RoomState>(resp.stateJson);
    if (!isEmpty(state, "roomId")) {
      throw new AlreadyExistsError(`room ${id}`);
    }

    return newCommandOutput(
      EventRoomCreated,
      {
        roomId: id,
        name: this.name,
        capacity: this.capacity,
        location: this.location,
        equipment: this.equipment,
        requiresApproval: this.requiresApproval,
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 13. UpdateRoom
class UpdateRoom implements Command {
  roomId: string;
  name: string;
  capacity: number;
  location: string;
  equipment: string[];
  requiresApproval: boolean;

  constructor(data: { roomId: string; name: string; capacity: number; location: string; equipment?: string[]; requiresApproval: boolean }) {
    this.roomId = data.roomId;
    this.name = data.name;
    this.capacity = data.capacity;
    this.location = data.location;
    this.equipment = data.equipment ?? [];
    this.requiresApproval = data.requiresApproval;
  }

  commandType(): string { return "UpdateRoom"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const tag = tagString(TagGroupRoom, this.roomId);

    const resp = await ctx.getTagState(TagGroupRoom, this.roomId);
    const state = parseState<RoomState>(resp.stateJson);
    if (isEmpty(state, "roomId")) {
      throw new NotFoundError(`room ${this.roomId}`);
    }

    return newCommandOutput(
      EventRoomUpdated,
      {
        roomId: this.roomId,
        name: this.name,
        capacity: this.capacity,
        location: this.location,
        equipment: this.equipment,
        requiresApproval: this.requiresApproval,
      },
      [tag], [tag],
      { [tag]: resp.version },
    );
  }
}

// 14. CreateReservationDraft
class CreateReservationDraft implements Command {
  reservationId?: string | null;
  roomId: string;
  organizerId: string;
  organizerName: string;
  startTime: string;
  endTime: string;
  purpose: string;
  selectedEquipment: string[];

  constructor(data: { reservationId?: string | null; roomId: string; organizerId: string; organizerName: string; startTime: string; endTime: string; purpose: string; selectedEquipment?: string[] }) {
    this.reservationId = data.reservationId;
    this.roomId = data.roomId;
    this.organizerId = data.organizerId;
    this.organizerName = data.organizerName;
    this.startTime = data.startTime;
    this.endTime = data.endTime;
    this.purpose = data.purpose;
    this.selectedEquipment = data.selectedEquipment ?? [];
  }

  commandType(): string { return "CreateReservationDraft"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    // Check room exists
    const roomResp = await ctx.getTagState(TagGroupRoom, this.roomId);
    const room = parseState<RoomState>(roomResp.stateJson);
    if (isEmpty(room, "roomId")) {
      throw new NotFoundError(`room ${this.roomId}`);
    }

    const id = (this.reservationId && this.reservationId !== "") ? this.reservationId : uuidv4();
    const reservationTag = tagString(TagGroupReservation, id);
    const roomTag = tagString(TagGroupRoom, this.roomId);

    return newCommandOutput(
      EventReservationDraftCreated,
      {
        reservationId: id,
        roomId: this.roomId,
        organizerId: this.organizerId,
        organizerName: this.organizerName,
        startTime: this.startTime,
        endTime: this.endTime,
        purpose: this.purpose,
        selectedEquipment: this.selectedEquipment,
      },
      [reservationTag, roomTag], [reservationTag],
      {},
    );
  }
}

class CreateQuickReservation implements Command {
  reservationId?: string | null;
  roomId: string;
  organizerId: string;
  organizerName: string;
  startTime: string;
  endTime: string;
  purpose: string;
  approvalRequestComment?: string | null;
  selectedEquipment: string[];

  constructor(data: {
    reservationId?: string | null;
    roomId: string;
    organizerId: string;
    organizerName: string;
    startTime: string;
    endTime: string;
    purpose: string;
    approvalRequestComment?: string | null;
    selectedEquipment?: string[];
  }) {
    this.reservationId = data.reservationId;
    this.roomId = data.roomId;
    this.organizerId = data.organizerId;
    this.organizerName = data.organizerName;
    this.startTime = data.startTime;
    this.endTime = data.endTime;
    this.purpose = data.purpose;
    this.approvalRequestComment = data.approvalRequestComment;
    this.selectedEquipment = data.selectedEquipment ?? [];
  }

  commandType(): string { return "CreateQuickReservation"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const reservationId = (this.reservationId && this.reservationId !== "") ? this.reservationId : uuidv4();
    const reservationTag = tagString(TagGroupReservation, reservationId);

    const reservationResp = await ctx.getTagState(TagGroupReservation, reservationId);
    const reservation = parseState<ReservationState>(reservationResp.stateJson);
    if (!isEmpty(reservation, "reservationId")) {
      throw new AlreadyExistsError(`reservation ${reservationId}`);
    }

    const roomResp = await ctx.getTagState(TagGroupRoom, this.roomId);
    const room = parseState<RoomState>(roomResp.stateJson);
    if (isEmpty(room, "roomId")) {
      throw new NotFoundError(`room ${this.roomId}`);
    }

    const roomReservationsResp = await ctx.getTagState(TagGroupRoomReservation, this.roomId);
    const roomReservations = parseState<RoomReservationsState>(roomReservationsResp.stateJson);
    if (hasRoomReservationConflict(roomReservations, this.startTime, this.endTime, null)) {
      throw new ValidationError("Reservation time conflicts with another held or confirmed reservation");
    }

    const roomReservationTag = tagString(TagGroupRoomReservation, this.roomId);
    const tags = [reservationTag, roomReservationTag];
    const versions = { [roomReservationTag]: roomReservationsResp.version };
    const events: EventOutput[] = [
      {
        eventType: EventReservationDraftCreated,
        payload: {
          reservationId,
          roomId: this.roomId,
          organizerId: this.organizerId,
          organizerName: this.organizerName,
          startTime: this.startTime,
          endTime: this.endTime,
          purpose: this.purpose,
          selectedEquipment: this.selectedEquipment,
        },
        tags,
        versions,
      },
      {
        eventType: EventReservationHoldCommitted,
        payload: {
          reservationId,
          roomId: this.roomId,
          organizerId: this.organizerId,
          organizerName: this.organizerName,
          startTime: this.startTime,
          endTime: this.endTime,
          purpose: this.purpose,
          selectedEquipment: this.selectedEquipment,
          requiresApproval: Boolean(room?.requiresApproval),
          approvalRequestId: null,
          approvalRequestComment: this.approvalRequestComment ?? null,
        },
        tags,
        versions,
      },
    ];

    if (!Boolean(room?.requiresApproval)) {
      events.push({
        eventType: EventReservationConfirmed,
        payload: {
          reservationId,
          roomId: this.roomId,
          organizerId: this.organizerId,
          organizerName: this.organizerName,
          startTime: this.startTime,
          endTime: this.endTime,
          purpose: this.purpose,
          selectedEquipment: this.selectedEquipment,
          confirmedAt: nowIso(),
          approvalRequestId: null,
          approvalRequestComment: null,
          approvalDecisionComment: null,
        },
        tags,
        versions,
      });
    }

    return {
      events,
      tags,
      consistencyTags: tags,
    };
  }
}

// 15. CommitReservationHold
class CommitReservationHold implements Command {
  reservationId: string;
  roomId: string;
  requiresApproval: boolean;
  approvalRequestId?: string | null;
  approvalRequestComment?: string | null;

  constructor(data: { reservationId: string; roomId: string; requiresApproval: boolean; approvalRequestId?: string | null; approvalRequestComment?: string | null }) {
    this.reservationId = data.reservationId;
    this.roomId = data.roomId;
    this.requiresApproval = data.requiresApproval;
    this.approvalRequestId = data.approvalRequestId;
    this.approvalRequestComment = data.approvalRequestComment;
  }

  commandType(): string { return "CommitReservationHold"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const reservationTag = tagString(TagGroupReservation, this.reservationId);

    const resp = await ctx.getTagState(TagGroupReservation, this.reservationId);
    const state = parseState<ReservationState>(resp.stateJson);
    if (isEmpty(state, "reservationId")) {
      throw new NotFoundError(`reservation ${this.reservationId}`);
    }
    if (state!.status !== "Draft") {
      throw new ValidationError(`reservation ${this.reservationId} is not in Draft status`);
    }

    const roomTag = tagString(TagGroupRoom, this.roomId);

    return newCommandOutput(
      EventReservationHoldCommitted,
      {
        reservationId: this.reservationId,
        roomId: this.roomId,
        organizerId: state!.organizerId,
        organizerName: state!.organizerName,
        startTime: state!.startTime,
        endTime: state!.endTime,
        purpose: state!.purpose,
        selectedEquipment: state!.selectedEquipment ?? [],
        requiresApproval: this.requiresApproval,
        approvalRequestId: this.approvalRequestId ?? null,
        approvalRequestComment: this.approvalRequestComment ?? null,
      },
      [reservationTag, roomTag], [reservationTag],
      { [reservationTag]: resp.version },
    );
  }
}

// 16. ConfirmReservation
class ConfirmReservation implements Command {
  reservationId: string;
  roomId: string;
  approvalRequestId?: string | null;
  approvalDecisionComment?: string | null;

  constructor(data: { reservationId: string; roomId: string; approvalRequestId?: string | null; approvalDecisionComment?: string | null }) {
    this.reservationId = data.reservationId;
    this.roomId = data.roomId;
    this.approvalRequestId = data.approvalRequestId;
    this.approvalDecisionComment = data.approvalDecisionComment;
  }

  commandType(): string { return "ConfirmReservation"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const reservationTag = tagString(TagGroupReservation, this.reservationId);

    const resp = await ctx.getTagState(TagGroupReservation, this.reservationId);
    const state = parseState<ReservationState>(resp.stateJson);
    if (isEmpty(state, "reservationId")) {
      throw new NotFoundError(`reservation ${this.reservationId}`);
    }

    const roomTag = tagString(TagGroupRoom, this.roomId);

    return newCommandOutput(
      EventReservationConfirmed,
      {
        reservationId: this.reservationId,
        roomId: this.roomId,
        organizerId: state!.organizerId,
        organizerName: state!.organizerName,
        startTime: state!.startTime,
        endTime: state!.endTime,
        purpose: state!.purpose,
        selectedEquipment: state!.selectedEquipment ?? [],
        confirmedAt: nowIso(),
        approvalRequestId: this.approvalRequestId ?? null,
        approvalRequestComment: state!.approvalRequestComment ?? null,
        approvalDecisionComment: this.approvalDecisionComment ?? null,
      },
      [reservationTag, roomTag], [reservationTag],
      { [reservationTag]: resp.version },
    );
  }
}

// 17. CancelReservation
class CancelReservation implements Command {
  reservationId: string;
  roomId: string;
  reason: string;

  constructor(data: { reservationId: string; roomId: string; reason: string }) {
    this.reservationId = data.reservationId;
    this.roomId = data.roomId;
    this.reason = data.reason;
  }

  commandType(): string { return "CancelReservation"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const reservationTag = tagString(TagGroupReservation, this.reservationId);

    const resp = await ctx.getTagState(TagGroupReservation, this.reservationId);
    const state = parseState<ReservationState>(resp.stateJson);
    if (isEmpty(state, "reservationId")) {
      throw new NotFoundError(`reservation ${this.reservationId}`);
    }

    const roomTag = tagString(TagGroupRoom, this.roomId);

    return newCommandOutput(
      EventReservationCancelled,
      {
        reservationId: this.reservationId,
        roomId: this.roomId,
        organizerId: state!.organizerId,
        organizerName: state!.organizerName,
        startTime: state!.startTime,
        endTime: state!.endTime,
        purpose: state!.purpose,
        selectedEquipment: state!.selectedEquipment ?? [],
        approvalRequestComment: state!.approvalRequestComment ?? null,
        reason: this.reason,
        cancelledAt: nowIso(),
      },
      [reservationTag, roomTag], [reservationTag],
      { [reservationTag]: resp.version },
    );
  }
}

// 18. RejectReservation
class RejectReservation implements Command {
  reservationId: string;
  roomId: string;
  approvalRequestId: string;
  reason: string;

  constructor(data: { reservationId: string; roomId: string; approvalRequestId: string; reason: string }) {
    this.reservationId = data.reservationId;
    this.roomId = data.roomId;
    this.approvalRequestId = data.approvalRequestId;
    this.reason = data.reason;
  }

  commandType(): string { return "RejectReservation"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const reservationTag = tagString(TagGroupReservation, this.reservationId);

    const resp = await ctx.getTagState(TagGroupReservation, this.reservationId);
    const state = parseState<ReservationState>(resp.stateJson);
    if (isEmpty(state, "reservationId")) {
      throw new NotFoundError(`reservation ${this.reservationId}`);
    }

    const roomTag = tagString(TagGroupRoom, this.roomId);

    return newCommandOutput(
      EventReservationRejected,
      {
        reservationId: this.reservationId,
        roomId: this.roomId,
        organizerId: state!.organizerId,
        organizerName: state!.organizerName,
        startTime: state!.startTime,
        endTime: state!.endTime,
        purpose: state!.purpose,
        selectedEquipment: state!.selectedEquipment ?? [],
        approvalRequestId: this.approvalRequestId,
        approvalRequestComment: state!.approvalRequestComment ?? null,
        reason: this.reason,
        rejectedAt: nowIso(),
      },
      [reservationTag, roomTag], [reservationTag],
      { [reservationTag]: resp.version },
    );
  }
}

// 19. StartApprovalFlow
class StartApprovalFlow implements Command {
  approvalRequestId?: string | null;
  reservationId: string;
  approverIds: string[];
  requestComment?: string | null;

  constructor(data: { approvalRequestId?: string | null; reservationId: string; approverIds?: string[]; requestComment?: string | null }) {
    this.approvalRequestId = data.approvalRequestId;
    this.reservationId = data.reservationId;
    this.approverIds = data.approverIds ?? [];
    this.requestComment = data.requestComment;
  }

  commandType(): string { return "StartApprovalFlow"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const reservationResp = await ctx.getTagState(TagGroupReservation, this.reservationId);
    const reservation = parseState<ReservationState>(reservationResp.stateJson);
    if (isEmpty(reservation, "reservationId")) {
      throw new NotFoundError(`reservation ${this.reservationId}`);
    }

    const id = (this.approvalRequestId && this.approvalRequestId !== "") ? this.approvalRequestId : uuidv4();
    const approvalTag = tagString(TagGroupApprovalRequest, id);

    return newCommandOutput(
      EventApprovalFlowStarted,
      {
        approvalRequestId: id,
        reservationId: this.reservationId,
        roomId: reservation!.roomId,
        requesterId: reservation!.organizerId,
        approverIds: this.approverIds,
        requestedAt: nowIso(),
        requestComment: this.requestComment ?? null,
      },
      [approvalTag], [approvalTag],
      {},
    );
  }
}

// 20. RecordApprovalDecision
class RecordApprovalDecision implements Command {
  approvalRequestId: string;
  approverId: string;
  decision: string;
  comment?: string | null;

  constructor(data: { approvalRequestId: string; approverId: string; decision: string; comment?: string | null }) {
    this.approvalRequestId = data.approvalRequestId;
    this.approverId = data.approverId;
    this.decision = data.decision;
    this.comment = data.comment;
  }

  commandType(): string { return "RecordApprovalDecision"; }

  async handle(ctx: CommandContext): Promise<CommandOutput> {
    const approvalTag = tagString(TagGroupApprovalRequest, this.approvalRequestId);

    const resp = await ctx.getTagState(TagGroupApprovalRequest, this.approvalRequestId);
    const state = parseState<ApprovalRequestState>(resp.stateJson);
    if (isEmpty(state, "approvalRequestId")) {
      throw new NotFoundError(`approval request ${this.approvalRequestId}`);
    }

    return newCommandOutput(
      EventApprovalDecisionRecorded,
      {
        approvalRequestId: this.approvalRequestId,
        reservationId: state!.reservationId,
        approverId: this.approverId,
        decision: this.decision,
        comment: this.comment ?? null,
        decidedAt: nowIso(),
      },
      [approvalTag], [approvalTag],
      { [approvalTag]: resp.version },
    );
  }
}

// ---------------------------------------------------------------------------
// Error handling helpers
// ---------------------------------------------------------------------------

function writeErrorFromCommand(err: unknown): { status: number; body: { error: string; message: string } } {
  if (err instanceof AlreadyExistsError) {
    return { status: 409, body: { error: "AlreadyExists", message: err.message } };
  }
  if (err instanceof NotFoundError) {
    return { status: 404, body: { error: "NotFound", message: err.message } };
  }
  if (err instanceof ValidationError) {
    return { status: 400, body: { error: "Validation", message: err.message } };
  }
  const message = err instanceof Error ? err.message : String(err);
  return { status: 500, body: { error: "InternalError", message } };
}

// ---------------------------------------------------------------------------
// Environment resolution
// ---------------------------------------------------------------------------

function resolveWasmServerURL(): string {
  if (process.env.WASM_SERVER_URL) return process.env.WASM_SERVER_URL;
  if (process.env.services__wasmserver__http__0) return process.env.services__wasmserver__http__0;
  if (process.env.services__wasmserver__https__0) return process.env.services__wasmserver__https__0;
  if (process.env.services__wasmserver__0) return process.env.services__wasmserver__0;
  return "http://localhost:5000";
}

function resolvePort(): number {
  const p = process.env.PORT;
  if (p) {
    const v = parseInt(p, 10);
    if (!isNaN(v)) return v;
  }
  return 8080;
}

// ---------------------------------------------------------------------------
// Application
// ---------------------------------------------------------------------------

const wasmServerURL = resolveWasmServerURL();
const port = resolvePort();

console.log(`WasmServer URL: ${wasmServerURL}`);
console.log(`Starting TS ClientAPI on port ${port}`);

const runtime = new SekibanRuntimeClient(wasmServerURL, tagProjectorMap);

const app = new Hono();
app.use("*", logger());

// ---------------------------------------------------------------------------
// Health
// ---------------------------------------------------------------------------

app.get("/health", (c) => {
  return c.json({ message: "Sekiban decider TS ClientApi is running" });
});

// ---------------------------------------------------------------------------
// Weather handlers
// ---------------------------------------------------------------------------

app.get("/api/weatherforecast", async (c) => {
  const params: Record<string, string> = {};
  const loc = c.req.query("location");
  if (loc) params.locationFilter = loc;
  const locFilter = c.req.query("locationFilter");
  if (locFilter) params.locationFilter = locFilter;
  const fid = c.req.query("forecastId");
  if (fid) params.forecastId = fid;
  try {
    const result = await runtime.executeListQuery("GetWeatherForecastListQuery", JSON.stringify(params), null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.get("/api/weatherforecast/count", async (c) => {
  const params: Record<string, string> = {};
  const loc = c.req.query("location");
  if (loc) params.locationFilter = loc;
  const locFilter = c.req.query("locationFilter");
  if (locFilter) params.locationFilter = locFilter;
  try {
    const result = await runtime.executeQuery("GetWeatherForecastCountQuery", JSON.stringify(params), null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/weatherforecast", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new CreateWeatherForecast(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/weatherforecast/update-location", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new UpdateWeatherForecastLocation(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/weatherforecast/delete", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new DeleteWeatherForecast(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Student handlers
// ---------------------------------------------------------------------------

app.get("/api/students", async (c) => {
  try {
    const result = await runtime.executeListQuery("GetStudentListQuery", "{}", null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/students", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new CreateStudent(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// ClassRoom handlers
// ---------------------------------------------------------------------------

app.get("/api/classrooms", async (c) => {
  try {
    const result = await runtime.executeListQuery("GetClassRoomListQuery", "{}", null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/classrooms", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new CreateClassRoom(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Enrollment handlers
// ---------------------------------------------------------------------------

app.post("/api/enrollments/add", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new EnrollStudentInClassRoom(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/enrollments/drop", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new DropStudentFromClassRoom(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// User handlers
// ---------------------------------------------------------------------------

app.get("/api/users", async (c) => {
  const params: Record<string, any> = {};
  if (c.req.query("activeOnly") === "true") {
    params.activeOnly = true;
  }
  try {
    const result = await runtime.executeListQuery("GetUserDirectoryListQuery", JSON.stringify(params), null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/users", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new RegisterUser(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/users/:userId/monthly-limit", async (c) => {
  try {
    const userId = c.req.param("userId");
    const body = await c.req.json();
    const cmd = new UpdateUserMonthlyReservationLimit({
      userId,
      monthlyReservationLimit: body.monthlyReservationLimit,
    });
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Room handlers
// ---------------------------------------------------------------------------

app.get("/api/rooms", async (c) => {
  try {
    const result = await runtime.executeListQuery("GetRoomListQuery", "{}", null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/rooms", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new CreateRoom(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.put("/api/rooms/:roomId", async (c) => {
  try {
    const roomId = c.req.param("roomId");
    const body = await c.req.json();
    body.roomId = roomId;
    const cmd = new UpdateRoom(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Reservation handlers
// ---------------------------------------------------------------------------

app.get("/api/reservations", async (c) => {
  const params: Record<string, string> = {};
  const roomId = c.req.query("roomId");
  if (roomId) params.roomId = roomId;
  try {
    const result = await runtime.executeListQuery("GetReservationListQuery", JSON.stringify(params), null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.get("/api/reservations/by-room/:roomId", async (c) => {
  const roomId = c.req.param("roomId");
  const params = { roomId };
  try {
    const result = await runtime.executeListQuery("GetReservationListQuery", JSON.stringify(params), null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/reservations/draft", async (c) => {
  try {
    const body = await c.req.json();
    const cmd = new CreateReservationDraft(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/reservations/quick", async (c) => {
  try {
    const body = await c.req.json();
    let resId = body.reservationId;
    if (!resId || resId === "") {
      resId = uuidv4();
      body.reservationId = resId;
    }
    const quickCmd = new CreateQuickReservation(body);
    const confirmResp = await runtime.finalizeCommand(quickCmd);

    return c.json({
      reservationId: resId,
      status: "Confirmed",
      commit: confirmResp,
    });
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/reservations/:reservationId/hold", async (c) => {
  try {
    const reservationId = c.req.param("reservationId");
    const body = await c.req.json();
    body.reservationId = reservationId;
    const cmd = new CommitReservationHold(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/reservations/:reservationId/confirm", async (c) => {
  try {
    const reservationId = c.req.param("reservationId");
    const body = await c.req.json();
    body.reservationId = reservationId;
    const cmd = new ConfirmReservation(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/reservations/:reservationId/cancel", async (c) => {
  try {
    const reservationId = c.req.param("reservationId");
    const body = await c.req.json();
    body.reservationId = reservationId;
    const cmd = new CancelReservation(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/reservations/:reservationId/reject", async (c) => {
  try {
    const reservationId = c.req.param("reservationId");
    const body = await c.req.json();
    body.reservationId = reservationId;
    const cmd = new RejectReservation(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Approval handlers
// ---------------------------------------------------------------------------

app.get("/api/approvals", async (c) => {
  const params: Record<string, any> = { pendingOnly: true };
  if (c.req.query("pendingOnly") === "false") {
    params.pendingOnly = false;
  }
  try {
    const result = await runtime.executeListQuery("GetApprovalInboxQuery", JSON.stringify(params), null);
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/approvals/:approvalRequestId/decision", async (c) => {
  try {
    const approvalRequestId = c.req.param("approvalRequestId");
    const body = await c.req.json();
    body.approvalRequestId = approvalRequestId;
    const cmd = new RecordApprovalDecision(body);
    const resp = await runtime.finalizeCommand(cmd);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Test data generation
// ---------------------------------------------------------------------------

interface RoomSpec {
  name: string;
  capacity: number;
  location: string;
  equipment: string[];
  requiresApproval: boolean;
}

const defaultRooms: RoomSpec[] = [
  { name: "Conference Room A", capacity: 10, location: "Floor 1", equipment: ["projector", "whiteboard"], requiresApproval: false },
  { name: "Conference Room B", capacity: 20, location: "Floor 1", equipment: ["projector", "whiteboard", "video"], requiresApproval: false },
  { name: "Board Room", capacity: 30, location: "Floor 2", equipment: ["projector", "whiteboard", "video", "phone"], requiresApproval: true },
  { name: "Training Room 1", capacity: 40, location: "Floor 2", equipment: ["projector", "whiteboard"], requiresApproval: false },
  { name: "Training Room 2", capacity: 40, location: "Floor 2", equipment: ["projector"], requiresApproval: false },
  { name: "Executive Suite", capacity: 8, location: "Floor 3", equipment: ["projector", "whiteboard", "video", "phone"], requiresApproval: true },
  { name: "Huddle Room 1", capacity: 4, location: "Floor 1", equipment: ["whiteboard"], requiresApproval: false },
  { name: "Huddle Room 2", capacity: 4, location: "Floor 1", equipment: ["whiteboard"], requiresApproval: false },
  { name: "Huddle Room 3", capacity: 4, location: "Floor 2", equipment: ["whiteboard"], requiresApproval: false },
  { name: "Auditorium", capacity: 100, location: "Floor 1", equipment: ["projector", "microphone", "video"], requiresApproval: true },
  { name: "Innovation Lab", capacity: 15, location: "Floor 3", equipment: ["projector", "whiteboard", "3d-printer"], requiresApproval: false },
  { name: "Quiet Room", capacity: 2, location: "Floor 2", equipment: [], requiresApproval: false },
  { name: "Phone Booth 1", capacity: 1, location: "Floor 1", equipment: ["phone"], requiresApproval: false },
  { name: "Phone Booth 2", capacity: 1, location: "Floor 1", equipment: ["phone"], requiresApproval: false },
  { name: "Phone Booth 3", capacity: 1, location: "Floor 2", equipment: ["phone"], requiresApproval: false },
  { name: "Workshop Room", capacity: 25, location: "Floor 3", equipment: ["projector", "whiteboard", "tools"], requiresApproval: false },
  { name: "Media Room", capacity: 12, location: "Floor 3", equipment: ["projector", "video", "sound-system"], requiresApproval: false },
  { name: "Lounge A", capacity: 15, location: "Floor 1", equipment: [], requiresApproval: false },
  { name: "Lounge B", capacity: 15, location: "Floor 2", equipment: [], requiresApproval: false },
  { name: "Rooftop Terrace", capacity: 50, location: "Rooftop", equipment: [], requiresApproval: true },
];

async function generateRooms(): Promise<{ created: number; failed: number }> {
  let created = 0;
  let failed = 0;
  for (const rm of defaultRooms) {
    const roomId = uuidv4();
    const cmd = new CreateRoom({
      roomId,
      name: rm.name,
      capacity: rm.capacity,
      location: rm.location,
      equipment: rm.equipment,
      requiresApproval: rm.requiresApproval,
    });
    try {
      await runtime.finalizeCommand(cmd);
      created++;
    } catch {
      failed++;
    }
  }
  return { created, failed };
}

async function generateReservations(count: number): Promise<{ created: number; failed: number; error?: string }> {
  // Register a test user
  const userId = uuidv4();
  const userCmd = new RegisterUser({
    userId,
    displayName: "Test Organizer",
    email: "test@example.com",
    monthlyReservationLimit: count + 10,
  });
  try {
    await runtime.finalizeCommand(userCmd);
  } catch {
    // ignore if user already exists
  }

  // Get room list
  let roomsJSON: string;
  try {
    roomsJSON = await runtime.executeListQuery("GetRoomListQuery", "{}", null);
  } catch {
    return { created: 0, failed: 0, error: "no rooms found" };
  }

  let roomItems: Array<{ roomId: string; name: string }>;
  try {
    roomItems = JSON.parse(roomsJSON);
  } catch {
    return { created: 0, failed: 0, error: "no rooms found" };
  }
  if (!roomItems || roomItems.length === 0) {
    return { created: 0, failed: 0, error: "no rooms found" };
  }

  const purposes = [
    "Team standup", "Sprint planning", "Design review",
    "1:1 meeting", "Client call", "Workshop",
    "Training session", "Brainstorming", "All-hands",
  ];

  let created = 0;
  let failed = 0;
  const baseTime = new Date();
  baseTime.setMinutes(0, 0, 0);
  baseTime.setTime(baseTime.getTime() + 60 * 60 * 1000); // +1 hour

  for (let i = 0; i < count; i++) {
    const room = roomItems[Math.floor(Math.random() * roomItems.length)];
    const resId = uuidv4();
    const startTime = new Date(baseTime.getTime() + i * 30 * 60 * 1000);
    const endTime = new Date(startTime.getTime() + 30 * 60 * 1000);
    const purpose = purposes[Math.floor(Math.random() * purposes.length)];

    try {
      const draftCmd = new CreateReservationDraft({
        reservationId: resId,
        roomId: room.roomId,
        organizerId: userId,
        organizerName: "Test Organizer",
        startTime: startTime.toISOString(),
        endTime: endTime.toISOString(),
        purpose,
        selectedEquipment: [],
      });
      await runtime.finalizeCommand(draftCmd);

      const holdCmd = new CommitReservationHold({
        reservationId: resId,
        roomId: room.roomId,
        requiresApproval: false,
      });
      await runtime.finalizeCommand(holdCmd);

      const confirmCmd = new ConfirmReservation({
        reservationId: resId,
        roomId: room.roomId,
      });
      await runtime.finalizeCommand(confirmCmd);

      created++;
    } catch {
      failed++;
    }
  }
  return { created, failed };
}

app.post("/api/test-data/generate", async (c) => {
  const countStr = c.req.query("count");
  let count = 100;
  if (countStr) {
    const v = parseInt(countStr, 10);
    if (!isNaN(v) && v > 0) count = v;
  }

  const roomResults = await generateRooms();
  const reservationResults = await generateReservations(count);

  return c.json({ rooms: roomResults, reservations: reservationResults });
});

app.post("/api/test-data/generate-rooms", async (c) => {
  const results = await generateRooms();
  return c.json(results);
});

app.post("/api/test-data/generate-reservations", async (c) => {
  const countStr = c.req.query("count");
  let count = 100;
  if (countStr) {
    const v = parseInt(countStr, 10);
    if (!isNaN(v) && v > 0) count = v;
  }
  const results = await generateReservations(count);
  return c.json(results);
});

// ---------------------------------------------------------------------------
// Start server
// ---------------------------------------------------------------------------

console.log(`Listening on :${port}`);
serve({
  fetch: app.fetch,
  port,
});
