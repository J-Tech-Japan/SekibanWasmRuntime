import { z } from "zod";
import {
  command,
  done,
  event,
  projector,
  read,
  readExists,
  readSet,
  reject,
  stateUnion,
  tagFamily,
  type CommandDefinition,
} from "@sekiban/dcb-domain";
import type { SekibanExecutor } from "@sekiban/dcb-client";
import { readStateOrThrow } from "./executorAdapter.js";

export const weatherFamily = tagFamily("weather");
export const studentFamily = tagFamily("Student");
export const classRoomFamily = tagFamily("ClassRoom");
export const userFamily = tagFamily("User");
export const userAccessFamily = tagFamily("UserAccess");
export const roomFamily = tagFamily("Room");
export const roomReservationFamily = tagFamily("RoomReservation");
export const reservationFamily = tagFamily("Reservation");
export const approvalRequestFamily = tagFamily("ApprovalRequest");

export const weatherTag = (id: string) => weatherFamily.of(id);
export const studentTag = (id: string) => studentFamily.of(id);
export const classRoomTag = (id: string) => classRoomFamily.of(id);
export const userTag = (id: string) => userFamily.of(id);
export const userAccessTag = (id: string) => userAccessFamily.of(id);
export const roomTag = (id: string) => roomFamily.of(id);
export const roomReservationTag = (id: string) => roomReservationFamily.of(id);
export const reservationTag = (id: string) => reservationFamily.of(id);
export const approvalRequestTag = (id: string) => approvalRequestFamily.of(id);

const looseState = stateUnion(z.record(z.string(), z.unknown()), { initial: () => ({}) });

const emptyRecord = () => ({}) as Record<string, unknown>;

export const weatherForecastProjector = projector({
  id: "WeatherForecastProjector", version: 1, tag: weatherFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const studentProjector = projector({
  id: "StudentProjector", version: 1, tag: studentFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const classRoomProjector = projector({
  id: "ClassRoomProjector", version: 1, tag: classRoomFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const userDirectoryProjector = projector({
  id: "UserDirectoryProjector", version: 1, tag: userFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const userAccessProjector = projector({
  id: "UserAccessProjector", version: 1, tag: userAccessFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const roomProjector = projector({
  id: "RoomProjector", version: 1, tag: roomFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const roomReservationsProjector = projector({
  id: "RoomReservationsProjector", version: 1, tag: roomReservationFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const reservationProjector = projector({
  id: "ReservationProjector", version: 1, tag: reservationFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});
export const approvalRequestProjector = projector({
  id: "ApprovalRequestProjector", version: 1, tag: approvalRequestFamily, state: looseState, initialState: emptyRecord,
  events: [], handlers: {},
});

export function fixedNowIso(now: string | number | bigint): string {
  if (typeof now === "string") return now;
  return new Date(Number(now)).toISOString();
}

/** Normalize optional caller IDs once before retryable executor.execute. */
export function normalizeOptionalId(value: string | null | undefined): string {
  return value && value !== "" ? value : crypto.randomUUID();
}

function isEmptyState(state: Record<string, unknown>, idField: string): boolean {
  const id = state[idField];
  return typeof id !== "string" || id.length === 0;
}

function parseIsoDate(value: string, fieldName: string): Date {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    throw Object.assign(new Error(`${fieldName} must be a valid ISO-8601 timestamp`), { rejectKind: "validation" });
  }
  return parsed;
}

export function hasRoomReservationConflict(
  state: Record<string, unknown> | null | undefined,
  startTime: string,
  endTime: string,
  excludeReservationId?: string | null,
): boolean {
  const start = parseIsoDate(startTime, "startTime").getTime();
  const end = parseIsoDate(endTime, "endTime").getTime();
  if (start >= end) {
    throw Object.assign(new Error("startTime must be before endTime"), { rejectKind: "validation" });
  }
  const active = (state?.activeReservations ?? {}) as Record<string, { startTime: string; endTime: string }>;
  for (const [reservationId, slot] of Object.entries(active)) {
    if (excludeReservationId && reservationId === excludeReservationId) continue;
    const slotStart = parseIsoDate(slot.startTime, "slot.startTime").getTime();
    const slotEnd = parseIsoDate(slot.endTime, "slot.endTime").getTime();
    if (start < slotEnd && slotStart < end) return true;
  }
  return false;
}

const optionalString = z.string().nullable().optional();
const stringArray = z.array(z.string()).optional().default([]);

const weatherForecastCreated = event("WeatherForecastCreated", z.object({
  forecastId: z.string(), location: z.string(), date: z.string(), temperatureC: z.number(),
  summary: z.string(), createdAt: z.string(),
}), { tags: (p) => [weatherTag(p.forecastId)] });
const weatherForecastLocationUpdated = event("WeatherForecastLocationUpdated", z.object({
  forecastId: z.string(), newLocation: z.string(), updatedAt: z.string(),
}), { tags: (p) => [weatherTag(p.forecastId)] });
const weatherForecastDeleted = event("WeatherForecastDeleted", z.object({
  forecastId: z.string(), deletedAt: z.string(),
}), { tags: (p) => [weatherTag(p.forecastId)] });

const studentCreated = event("StudentCreated", z.object({
  studentId: z.string(), name: z.string(), maxClassCount: z.number(),
}), { tags: (p) => [studentTag(p.studentId)] });
const classRoomCreated = event("ClassRoomCreated", z.object({
  classRoomId: z.string(), name: z.string(), maxStudents: z.number(),
}), { tags: (p) => [classRoomTag(p.classRoomId)] });
const studentEnrolled = event("StudentEnrolledInClassRoom", z.object({
  studentId: z.string(), classRoomId: z.string(),
}), { tags: (p) => [studentTag(p.studentId), classRoomTag(p.classRoomId)] });
const studentDropped = event("StudentDroppedFromClassRoom", z.object({
  studentId: z.string(), classRoomId: z.string(),
}), { tags: (p) => [studentTag(p.studentId), classRoomTag(p.classRoomId)] });

const userRegistered = event("UserRegistered", z.object({
  userId: z.string(), displayName: z.string(), email: z.string(), department: z.string().nullable(),
  registeredAt: z.string(), monthlyReservationLimit: z.number(),
}), { tags: (p) => [userTag(p.userId)] });
const userProfileUpdated = event("UserProfileUpdated", z.object({
  userId: z.string(), displayName: z.string(), email: z.string(), department: z.string().nullable(),
  monthlyReservationLimit: z.number(),
}), { tags: (p) => [userTag(p.userId)] });
const userAccessGranted = event("UserAccessGranted", z.object({
  userId: z.string(), initialRole: z.string(), grantedAt: z.string(),
}), { tags: (p) => [userAccessTag(p.userId)] });
const userRoleGranted = event("UserRoleGranted", z.object({
  userId: z.string(), role: z.string(), grantedAt: z.string(),
}), { tags: (p) => [userAccessTag(p.userId)] });

const roomCreated = event("RoomCreated", z.object({
  roomId: z.string(), name: z.string(), capacity: z.number(), location: z.string(),
  equipment: z.array(z.string()), requiresApproval: z.boolean(),
}), { tags: (p) => [roomTag(p.roomId)] });
const roomUpdated = event("RoomUpdated", z.object({
  roomId: z.string(), name: z.string(), capacity: z.number(), location: z.string(),
  equipment: z.array(z.string()), requiresApproval: z.boolean(),
}), { tags: (p) => [roomTag(p.roomId)] });

export const reservationDraftCreated = event("ReservationDraftCreated", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
}), { tags: (p) => [reservationTag(p.reservationId), roomTag(p.roomId)] });
const reservationDraftCreatedQuick = event("ReservationDraftCreated", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
}), { tags: (p) => [reservationTag(p.reservationId), roomReservationTag(p.roomId)] });
const reservationHoldCommittedQuick = event("ReservationHoldCommitted", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
  requiresApproval: z.boolean(), approvalRequestId: z.string().nullable(), approvalRequestComment: z.string().nullable(),
}), { tags: (p) => [reservationTag(p.reservationId), roomReservationTag(p.roomId)] });
const reservationHoldCommitted = event("ReservationHoldCommitted", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
  requiresApproval: z.boolean(), approvalRequestId: z.string().nullable(), approvalRequestComment: z.string().nullable(),
}), { tags: (p) => [reservationTag(p.reservationId), roomTag(p.roomId)] });
const reservationConfirmedQuick = event("ReservationConfirmed", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
  confirmedAt: z.string(), approvalRequestId: z.string().nullable(), approvalRequestComment: z.string().nullable(),
  approvalDecisionComment: z.string().nullable(),
}), { tags: (p) => [reservationTag(p.reservationId), roomReservationTag(p.roomId)] });
const reservationConfirmed = event("ReservationConfirmed", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
  confirmedAt: z.string(), approvalRequestId: z.string().nullable(), approvalRequestComment: z.string().nullable(),
  approvalDecisionComment: z.string().nullable(),
}), { tags: (p) => [reservationTag(p.reservationId), roomTag(p.roomId)] });
const reservationCancelled = event("ReservationCancelled", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
  approvalRequestComment: z.string().nullable(), reason: z.string(), cancelledAt: z.string(),
}), { tags: (p) => [reservationTag(p.reservationId), roomTag(p.roomId)] });
const reservationRejected = event("ReservationRejected", z.object({
  reservationId: z.string(), roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: z.array(z.string()),
  approvalRequestId: z.string(), approvalRequestComment: z.string().nullable(), reason: z.string(), rejectedAt: z.string(),
}), { tags: (p) => [reservationTag(p.reservationId), roomTag(p.roomId)] });

const approvalFlowStarted = event("ApprovalFlowStarted", z.object({
  approvalRequestId: z.string(), reservationId: z.string(), roomId: z.string(), requesterId: z.string(),
  approverIds: z.array(z.string()), requestedAt: z.string(), requestComment: z.string().nullable(),
}), { tags: (p) => [approvalRequestTag(p.approvalRequestId)] });
const approvalDecisionRecorded = event("ApprovalDecisionRecorded", z.object({
  approvalRequestId: z.string(), reservationId: z.string(), approverId: z.string(), decision: z.string(),
  comment: z.string().nullable(), decidedAt: z.string(),
}), { tags: (p) => [approvalRequestTag(p.approvalRequestId)] });


const idInput = z.object({ forecastId: optionalString, location: z.string(), date: z.string(), temperatureC: z.number(), summary: optionalString });
const updateWeatherInput = z.object({ forecastId: z.string(), newLocation: z.string() });
const deleteWeatherInput = z.object({ forecastId: z.string() });

export const createWeatherForecastCommand = command({
  id: "CreateWeatherForecast",
  input: idInput,
  reads: (input) => readExists(weatherTag(input.forecastId!)),
  handle: async (input, ctx) => {
    const id = input.forecastId!;
    if (await ctx.exists(weatherTag(id))) return reject("conflict", `weather forecast ${id}`);
    ctx.append(weatherForecastCreated, weatherForecastCreated.make({
      forecastId: id, location: input.location, date: input.date, temperatureC: input.temperatureC,
      summary: input.summary ?? "", createdAt: fixedNowIso(ctx.now()),
    }));
    return done({ forecastId: id });
  },
});

export const updateWeatherForecastLocationCommand = command({
  id: "UpdateWeatherForecastLocation",
  input: updateWeatherInput,
  reads: (input) => read(weatherForecastProjector, weatherTag(input.forecastId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(weatherForecastProjector, weatherTag(input.forecastId));
    if (isEmptyState(state as Record<string, unknown>, "forecastId")) return reject("not-found", `weather forecast ${input.forecastId}`);
    ctx.append(weatherForecastLocationUpdated, weatherForecastLocationUpdated.make({
      forecastId: input.forecastId, newLocation: input.newLocation, updatedAt: fixedNowIso(ctx.now()),
    }));
    return done();
  },
});

export const deleteWeatherForecastCommand = command({
  id: "DeleteWeatherForecast",
  input: deleteWeatherInput,
  reads: (input) => read(weatherForecastProjector, weatherTag(input.forecastId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(weatherForecastProjector, weatherTag(input.forecastId));
    if (isEmptyState(state as Record<string, unknown>, "forecastId")) return reject("not-found", `weather forecast ${input.forecastId}`);
    ctx.append(weatherForecastDeleted, weatherForecastDeleted.make({ forecastId: input.forecastId, deletedAt: fixedNowIso(ctx.now()) }));
    return done();
  },
});

const studentInput = z.object({ studentId: optionalString, name: z.string(), maxClassCount: z.number() });
export const createStudentCommand = command({
  id: "CreateStudent", input: studentInput,
  reads: (input) => readExists(studentTag(input.studentId!)),
  handle: async (input, ctx) => {
    if (input.maxClassCount < 1) return reject("validation", "maxClassCount must be at least 1");
    const id = input.studentId!;
    if (await ctx.exists(studentTag(id))) return reject("conflict", `student ${id}`);
    ctx.append(studentCreated, studentCreated.make({ studentId: id, name: input.name, maxClassCount: input.maxClassCount }));
    return done({ studentId: id });
  },
});

const classRoomInput = z.object({ classRoomId: optionalString, name: z.string(), maxStudents: z.number() });
export const createClassRoomCommand = command({
  id: "CreateClassRoom", input: classRoomInput,
  reads: (input) => readExists(classRoomTag(input.classRoomId!)),
  handle: async (input, ctx) => {
    if (input.maxStudents < 1) return reject("validation", "maxStudents must be at least 1");
    const id = input.classRoomId!;
    if (await ctx.exists(classRoomTag(id))) return reject("conflict", `classroom ${id}`);
    ctx.append(classRoomCreated, classRoomCreated.make({ classRoomId: id, name: input.name, maxStudents: input.maxStudents }));
    return done({ classRoomId: id });
  },
});

const enrollInput = z.object({ studentId: z.string(), classRoomId: z.string() });
export const enrollStudentInClassRoomCommand = command({
  id: "EnrollStudentInClassRoom", input: enrollInput,
  reads: (input) => readSet(read(studentProjector, studentTag(input.studentId)), read(classRoomProjector, classRoomTag(input.classRoomId))),
  handle: async (input, ctx) => {
    const student = await ctx.state(studentProjector, studentTag(input.studentId)) as Record<string, unknown>;
    if (isEmptyState(student, "studentId")) return reject("not-found", `student ${input.studentId}`);
    const cls = await ctx.state(classRoomProjector, classRoomTag(input.classRoomId)) as Record<string, unknown>;
    if (isEmptyState(cls, "classRoomId")) return reject("not-found", `classroom ${input.classRoomId}`);
    const classRemaining = (Number(cls.maxStudents) || 0) - (Number(cls.currentStudentCount) || 0);
    if (classRemaining <= 0) return reject("validation", `classroom ${input.classRoomId} is full`);
    const studentRemaining = (Number(student.maxClassCount) || 0) - (Number(student.currentClassCount) || 0);
    if (studentRemaining <= 0) return reject("validation", `student ${input.studentId} has reached max class count`);
    const enrolled = (student.enrolledClassRoomIds as string[] | undefined) ?? [];
    if (enrolled.includes(input.classRoomId)) return reject("validation", `student ${input.studentId} already enrolled in classroom ${input.classRoomId}`);
    ctx.append(studentEnrolled, studentEnrolled.make({ studentId: input.studentId, classRoomId: input.classRoomId }));
    return done();
  },
});

export const dropStudentFromClassRoomCommand = command({
  id: "DropStudentFromClassRoom", input: enrollInput,
  reads: (input) => readSet(read(studentProjector, studentTag(input.studentId)), read(classRoomProjector, classRoomTag(input.classRoomId))),
  handle: async (input, ctx) => {
    const student = await ctx.state(studentProjector, studentTag(input.studentId)) as Record<string, unknown>;
    if (isEmptyState(student, "studentId")) return reject("not-found", `student ${input.studentId}`);
    const cls = await ctx.state(classRoomProjector, classRoomTag(input.classRoomId)) as Record<string, unknown>;
    if (isEmptyState(cls, "classRoomId")) return reject("not-found", `classroom ${input.classRoomId}`);
    const enrolled = (student.enrolledClassRoomIds as string[] | undefined) ?? [];
    if (!enrolled.includes(input.classRoomId)) return reject("validation", `student ${input.studentId} not enrolled in classroom ${input.classRoomId}`);
    ctx.append(studentDropped, studentDropped.make({ studentId: input.studentId, classRoomId: input.classRoomId }));
    return done();
  },
});

const userInput = z.object({
  userId: optionalString, displayName: z.string(), email: z.string(), department: optionalString,
  monthlyReservationLimit: z.number(),
});
export const registerUserCommand = command({
  id: "RegisterUser", input: userInput,
  reads: (input) => readExists(userTag(input.userId!)),
  handle: async (input, ctx) => {
    const id = input.userId!;
    if (await ctx.exists(userTag(id))) return reject("conflict", `user ${id}`);
    ctx.append(userRegistered, userRegistered.make({
      userId: id, displayName: input.displayName, email: input.email, department: input.department ?? null,
      registeredAt: fixedNowIso(ctx.now()), monthlyReservationLimit: input.monthlyReservationLimit,
    }));
    return done({ userId: id });
  },
});

const limitInput = z.object({ userId: z.string(), monthlyReservationLimit: z.number() });
export const updateUserMonthlyReservationLimitCommand = command({
  id: "UpdateUserMonthlyReservationLimit", input: limitInput,
  reads: (input) => read(userDirectoryProjector, userTag(input.userId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(userDirectoryProjector, userTag(input.userId)) as Record<string, unknown>;
    if (isEmptyState(state, "userId")) return reject("not-found", `user ${input.userId}`);
    ctx.append(userProfileUpdated, userProfileUpdated.make({
      userId: input.userId, displayName: String(state.displayName), email: String(state.email),
      department: (state.department as string | null) ?? null, monthlyReservationLimit: input.monthlyReservationLimit,
    }));
    return done();
  },
});

const grantAccessInput = z.object({ userId: z.string(), initialRole: z.string() });
export const grantUserAccessCommand = command({
  id: "GrantUserAccess", input: grantAccessInput,
  reads: (input) => readSet(read(userDirectoryProjector, userTag(input.userId)), readExists(userAccessTag(input.userId))),
  handle: async (input, ctx) => {
    const user = await ctx.state(userDirectoryProjector, userTag(input.userId)) as Record<string, unknown>;
    if (isEmptyState(user, "userId")) return reject("not-found", `user ${input.userId}`);
    if (await ctx.exists(userAccessTag(input.userId))) return reject("conflict", `user access for ${input.userId} already exists`);
    ctx.append(userAccessGranted, userAccessGranted.make({ userId: input.userId, initialRole: input.initialRole, grantedAt: fixedNowIso(ctx.now()) }));
    return done();
  },
});

const grantRoleInput = z.object({ userId: z.string(), role: z.string() });
export const grantUserRoleCommand = command({
  id: "GrantUserRole", input: grantRoleInput,
  reads: (input) => read(userAccessProjector, userAccessTag(input.userId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(userAccessProjector, userAccessTag(input.userId)) as Record<string, unknown>;
    if (isEmptyState(state, "userId")) return reject("not-found", `user access for ${input.userId}`);
    ctx.append(userRoleGranted, userRoleGranted.make({ userId: input.userId, role: input.role, grantedAt: fixedNowIso(ctx.now()) }));
    return done();
  },
});

const roomInput = z.object({
  roomId: optionalString, name: z.string(), capacity: z.number(), location: z.string(),
  equipment: stringArray, requiresApproval: z.boolean(),
});
export const createRoomCommand = command({
  id: "CreateRoom", input: roomInput,
  reads: (input) => readExists(roomTag(input.roomId!)),
  handle: async (input, ctx) => {
    const id = input.roomId!;
    if (await ctx.exists(roomTag(id))) return reject("conflict", `room ${id}`);
    ctx.append(roomCreated, roomCreated.make({
      roomId: id, name: input.name, capacity: input.capacity, location: input.location,
      equipment: input.equipment, requiresApproval: input.requiresApproval,
    }));
    return done({ roomId: id });
  },
});

export const updateRoomCommand = command({
  id: "UpdateRoom", input: roomInput.extend({ roomId: z.string() }),
  reads: (input) => read(roomProjector, roomTag(input.roomId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(roomProjector, roomTag(input.roomId)) as Record<string, unknown>;
    if (isEmptyState(state, "roomId")) return reject("not-found", `room ${input.roomId}`);
    ctx.append(roomUpdated, roomUpdated.make({
      roomId: input.roomId, name: input.name, capacity: input.capacity, location: input.location,
      equipment: input.equipment, requiresApproval: input.requiresApproval,
    }));
    return done();
  },
});

const draftInput = z.object({
  reservationId: optionalString, roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), selectedEquipment: stringArray,
});
export const createReservationDraftCommand = command({
  id: "CreateReservationDraft", input: draftInput,
  reads: (input) => readExists(reservationTag(input.reservationId!)),
  handle: async (input, ctx) => {
    const id = input.reservationId!;
    if (await ctx.exists(reservationTag(id))) return reject("conflict", `reservation ${id}`);
    ctx.append(reservationDraftCreated, reservationDraftCreated.make({
      reservationId: id, roomId: input.roomId, organizerId: input.organizerId, organizerName: input.organizerName,
      startTime: input.startTime, endTime: input.endTime, purpose: input.purpose, selectedEquipment: input.selectedEquipment,
    }));
    return done({ reservationId: id });
  },
});

export async function executeCreateReservationDraft(
  executor: SekibanExecutor,
  input: z.infer<typeof draftInput>,
) {
  await readStateOrThrow(executor, roomProjector, roomTag(input.roomId));
  const reservationId = normalizeOptionalId(input.reservationId);
  const { executeOrThrow } = await import("./executorAdapter.js");
  return executeOrThrow(executor, createReservationDraftCommand, { ...input, reservationId });
}

const quickInput = z.object({
  reservationId: optionalString, roomId: z.string(), organizerId: z.string(), organizerName: z.string(),
  startTime: z.string(), endTime: z.string(), purpose: z.string(), approvalRequestComment: optionalString,
  selectedEquipment: stringArray,
});
export const createQuickReservationCommand = command({
  id: "CreateQuickReservation", input: quickInput,
  reads: (input) => readSet(
    readExists(reservationTag(input.reservationId!)),
    read(roomProjector, roomTag(input.roomId)),
    read(roomReservationsProjector, roomReservationTag(input.roomId)),
  ),
  handle: async (input, ctx) => {
    const reservationId = input.reservationId!;
    if (await ctx.exists(reservationTag(reservationId))) return reject("conflict", `reservation ${reservationId}`);
    const room = await ctx.state(roomProjector, roomTag(input.roomId)) as Record<string, unknown>;
    if (isEmptyState(room, "roomId")) return reject("not-found", `room ${input.roomId}`);
    const roomReservations = await ctx.state(roomReservationsProjector, roomReservationTag(input.roomId)) as Record<string, unknown>;
    try {
      if (hasRoomReservationConflict(roomReservations, input.startTime, input.endTime, null)) {
        return reject("validation", "Reservation time conflicts with another held or confirmed reservation");
      }
    } catch (err) {
      return reject("validation", err instanceof Error ? err.message : String(err));
    }
    const base = {
      reservationId, roomId: input.roomId, organizerId: input.organizerId, organizerName: input.organizerName,
      startTime: input.startTime, endTime: input.endTime, purpose: input.purpose, selectedEquipment: input.selectedEquipment,
    };
    ctx.append(reservationDraftCreatedQuick, reservationDraftCreatedQuick.make(base));
    ctx.append(reservationHoldCommittedQuick, reservationHoldCommittedQuick.make({
      ...base,
      requiresApproval: Boolean(room.requiresApproval),
      approvalRequestId: null,
      approvalRequestComment: input.approvalRequestComment ?? null,
    }));
    if (!Boolean(room.requiresApproval)) {
      ctx.append(reservationConfirmedQuick, reservationConfirmedQuick.make({
        ...base,
        confirmedAt: fixedNowIso(ctx.now()),
        approvalRequestId: null,
        approvalRequestComment: null,
        approvalDecisionComment: null,
      }));
    }
    return done({ reservationId });
  },
});

const holdInput = z.object({
  reservationId: z.string(), roomId: z.string(), requiresApproval: z.boolean(),
  approvalRequestId: optionalString, approvalRequestComment: optionalString,
});
export const commitReservationHoldCommand = command({
  id: "CommitReservationHold", input: holdInput,
  reads: (input) => read(reservationProjector, reservationTag(input.reservationId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(reservationProjector, reservationTag(input.reservationId)) as Record<string, unknown>;
    if (isEmptyState(state, "reservationId")) return reject("not-found", `reservation ${input.reservationId}`);
    if (state.status !== "Draft") return reject("validation", `reservation ${input.reservationId} is not in Draft status`);
    ctx.append(reservationHoldCommitted, reservationHoldCommitted.make({
      reservationId: input.reservationId, roomId: input.roomId,
      organizerId: String(state.organizerId), organizerName: String(state.organizerName),
      startTime: String(state.startTime), endTime: String(state.endTime), purpose: String(state.purpose),
      selectedEquipment: (state.selectedEquipment as string[]) ?? [],
      requiresApproval: input.requiresApproval,
      approvalRequestId: input.approvalRequestId ?? null,
      approvalRequestComment: input.approvalRequestComment ?? null,
    }));
    return done();
  },
});

const confirmInput = z.object({
  reservationId: z.string(), roomId: z.string(),
  approvalRequestId: optionalString, approvalDecisionComment: optionalString,
});
export const confirmReservationCommand = command({
  id: "ConfirmReservation", input: confirmInput,
  reads: (input) => read(reservationProjector, reservationTag(input.reservationId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(reservationProjector, reservationTag(input.reservationId)) as Record<string, unknown>;
    if (isEmptyState(state, "reservationId")) return reject("not-found", `reservation ${input.reservationId}`);
    ctx.append(reservationConfirmed, reservationConfirmed.make({
      reservationId: input.reservationId, roomId: input.roomId,
      organizerId: String(state.organizerId), organizerName: String(state.organizerName),
      startTime: String(state.startTime), endTime: String(state.endTime), purpose: String(state.purpose),
      selectedEquipment: (state.selectedEquipment as string[]) ?? [],
      confirmedAt: fixedNowIso(ctx.now()),
      approvalRequestId: input.approvalRequestId ?? null,
      approvalRequestComment: (state.approvalRequestComment as string | null) ?? null,
      approvalDecisionComment: input.approvalDecisionComment ?? null,
    }));
    return done();
  },
});

const cancelInput = z.object({ reservationId: z.string(), roomId: z.string(), reason: z.string() });
export const cancelReservationCommand = command({
  id: "CancelReservation", input: cancelInput,
  reads: (input) => read(reservationProjector, reservationTag(input.reservationId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(reservationProjector, reservationTag(input.reservationId)) as Record<string, unknown>;
    if (isEmptyState(state, "reservationId")) return reject("not-found", `reservation ${input.reservationId}`);
    ctx.append(reservationCancelled, reservationCancelled.make({
      reservationId: input.reservationId, roomId: input.roomId,
      organizerId: String(state.organizerId), organizerName: String(state.organizerName),
      startTime: String(state.startTime), endTime: String(state.endTime), purpose: String(state.purpose),
      selectedEquipment: (state.selectedEquipment as string[]) ?? [],
      approvalRequestComment: (state.approvalRequestComment as string | null) ?? null,
      reason: input.reason, cancelledAt: fixedNowIso(ctx.now()),
    }));
    return done();
  },
});

const rejectInput = z.object({
  reservationId: z.string(), roomId: z.string(), approvalRequestId: z.string(), reason: z.string(),
});
export const rejectReservationCommand = command({
  id: "RejectReservation", input: rejectInput,
  reads: (input) => read(reservationProjector, reservationTag(input.reservationId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(reservationProjector, reservationTag(input.reservationId)) as Record<string, unknown>;
    if (isEmptyState(state, "reservationId")) return reject("not-found", `reservation ${input.reservationId}`);
    ctx.append(reservationRejected, reservationRejected.make({
      reservationId: input.reservationId, roomId: input.roomId,
      organizerId: String(state.organizerId), organizerName: String(state.organizerName),
      startTime: String(state.startTime), endTime: String(state.endTime), purpose: String(state.purpose),
      selectedEquipment: (state.selectedEquipment as string[]) ?? [],
      approvalRequestId: input.approvalRequestId,
      approvalRequestComment: (state.approvalRequestComment as string | null) ?? null,
      reason: input.reason, rejectedAt: fixedNowIso(ctx.now()),
    }));
    return done();
  },
});

const approvalStartInput = z.object({
  approvalRequestId: optionalString, reservationId: z.string(), approverIds: z.array(z.string()).optional(),
  requestComment: optionalString,
});
export const startApprovalFlowCommand = command({
  id: "StartApprovalFlow", input: approvalStartInput,
  reads: (input) => readSet(read(reservationProjector, reservationTag(input.reservationId)), readExists(approvalRequestTag(input.approvalRequestId!))),
  handle: async (input, ctx) => {
    const reservation = await ctx.state(reservationProjector, reservationTag(input.reservationId)) as Record<string, unknown>;
    if (isEmptyState(reservation, "reservationId")) return reject("not-found", `reservation ${input.reservationId}`);
    const id = input.approvalRequestId!;
    if (await ctx.exists(approvalRequestTag(id))) return reject("conflict", `approval request ${id} already exists`);
    ctx.append(approvalFlowStarted, approvalFlowStarted.make({
      approvalRequestId: id, reservationId: input.reservationId, roomId: String(reservation.roomId),
      requesterId: String(reservation.organizerId), approverIds: input.approverIds ?? [],
      requestedAt: fixedNowIso(ctx.now()), requestComment: input.requestComment ?? null,
    }));
    return done({ approvalRequestId: id });
  },
});

export async function executeStartApprovalFlow(
  executor: SekibanExecutor,
  input: z.infer<typeof approvalStartInput>,
) {
  const approvalRequestId = normalizeOptionalId(input.approvalRequestId);
  const { executeOrThrow } = await import("./executorAdapter.js");
  return executeOrThrow(executor, startApprovalFlowCommand, { ...input, approvalRequestId });
}

const decisionInput = z.object({
  approvalRequestId: z.string(), approverId: z.string(), decision: z.string(), comment: optionalString,
});
export const recordApprovalDecisionCommand = command({
  id: "RecordApprovalDecision", input: decisionInput,
  reads: (input) => read(approvalRequestProjector, approvalRequestTag(input.approvalRequestId)),
  handle: async (input, ctx) => {
    const state = await ctx.state(approvalRequestProjector, approvalRequestTag(input.approvalRequestId)) as Record<string, unknown>;
    if (isEmptyState(state, "approvalRequestId")) return reject("not-found", `approval request ${input.approvalRequestId}`);
    ctx.append(approvalDecisionRecorded, approvalDecisionRecorded.make({
      approvalRequestId: input.approvalRequestId, reservationId: String(state.reservationId),
      approverId: input.approverId, decision: input.decision, comment: input.comment ?? null,
      decidedAt: fixedNowIso(ctx.now()),
    }));
    return done();
  },
});

export const allCommands = [
  createWeatherForecastCommand, updateWeatherForecastLocationCommand, deleteWeatherForecastCommand,
  createStudentCommand, createClassRoomCommand, enrollStudentInClassRoomCommand, dropStudentFromClassRoomCommand,
  registerUserCommand, updateUserMonthlyReservationLimitCommand, grantUserAccessCommand, grantUserRoleCommand,
  createRoomCommand, updateRoomCommand, createReservationDraftCommand, createQuickReservationCommand,
  commitReservationHoldCommand, confirmReservationCommand, cancelReservationCommand, rejectReservationCommand,
  startApprovalFlowCommand, recordApprovalDecisionCommand,
] as const satisfies readonly CommandDefinition[];
