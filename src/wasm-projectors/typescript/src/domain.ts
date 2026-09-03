import { z } from "zod";
import {
  command,
  done,
  domain,
  event,
  evolve,
  none,
  projector,
  read,
  readSet,
  reject,
  stateUnion,
  tagFamily,
  toRuntimeDomain,
  validate,
  validationReject,
  type DomainViewDefinition,
  type ProjectorDefinition,
  type ProjectorEvent,
  type RejectKind,
  type RuntimeDomainDefinition,
} from "@sekiban/dcb-domain";

const room = tagFamily("room");
const reservation = tagFamily("reservation");

export const roomTag = (roomId: string) => room.of(roomId);
export const reservationTag = (reservationId: string) => reservation.of(reservationId);

const roomInput = z.object({ roomId: z.string().min(1), name: z.string().optional() });
const reservationInput = z.object({ roomId: z.string().min(1), reservationId: z.string().min(1), userId: z.string().optional() });
const reservationOnlyInput = z.object({ reservationId: z.string().min(1) });

export type RoomInput = z.infer<typeof roomInput>;
export type ReservationInput = z.infer<typeof reservationInput>;
export type ReservationOnlyInput = z.infer<typeof reservationOnlyInput>;

const roomCreated = event("RoomCreated", z.object({
  roomId: z.string().min(1),
  name: z.string(),
}), {
  tags: (payload) => [room.of(payload.roomId)],
});

const roomReserved = event("RoomReserved", z.object({
  roomId: z.string().min(1),
  reservationId: z.string().min(1),
  userId: z.string(),
}), {
  tags: (payload) => [room.of(payload.roomId), reservation.of(payload.reservationId)],
});

const reservationCancelled = event("ReservationCancelled", z.object({
  reservationId: z.string().min(1),
  roomId: z.string().min(1).optional(),
}), {
  tags: (payload) => [reservation.of(payload.reservationId)],
});

const roomReleased = event("RoomReleased", z.object({
  roomId: z.string().min(1),
}), {
  tags: (payload) => [room.of(payload.roomId)],
});

type RoomState =
  | { readonly status: "empty"; readonly version: number; readonly roomId: null; readonly name: string }
  | { readonly status: "created"; readonly version: number; readonly roomId: string; readonly name: string }
  | { readonly status: "released"; readonly version: number; readonly roomId: string; readonly name: string };

type ReservationState =
  | { readonly status: "empty"; readonly version: number; readonly reservationId: null; readonly roomId: null }
  | { readonly status: "reserved"; readonly version: number; readonly reservationId: string; readonly roomId: string }
  | { readonly status: "cancelled"; readonly version: number; readonly reservationId: string; readonly roomId: string | null };

const roomState = stateUnion(z.discriminatedUnion("status", [
  z.object({ status: z.literal("empty"), version: z.number().int().nonnegative(), roomId: z.null(), name: z.string() }),
  z.object({ status: z.literal("created"), version: z.number().int().nonnegative(), roomId: z.string(), name: z.string() }),
  z.object({ status: z.literal("released"), version: z.number().int().nonnegative(), roomId: z.string(), name: z.string() }),
]), {
  discriminator: "status",
  initial: { status: "empty", version: 0, roomId: null, name: "" },
});

const reservationState = stateUnion(z.discriminatedUnion("status", [
  z.object({ status: z.literal("empty"), version: z.number().int().nonnegative(), reservationId: z.null(), roomId: z.null() }),
  z.object({ status: z.literal("reserved"), version: z.number().int().nonnegative(), reservationId: z.string(), roomId: z.string() }),
  z.object({ status: z.literal("cancelled"), version: z.number().int().nonnegative(), reservationId: z.string(), roomId: z.string().nullable() }),
]), {
  discriminator: "status",
  initial: { status: "empty", version: 0, reservationId: null, roomId: null },
});

const roomInitialState: RoomState = { status: "empty", version: 0, roomId: null, name: "" };
const reservationInitialState: ReservationState = { status: "empty", version: 0, reservationId: null, roomId: null };

type RoomEvent = typeof roomCreated | typeof roomReleased;
type ReservationEvent = typeof roomReserved | typeof reservationCancelled;

function roomCreatedEvent(eventValue: ProjectorEvent<RoomEvent>): ProjectorEvent<typeof roomCreated> {
  return {
    definition: roomCreated,
    eventType: roomCreated.eventType,
    payload: roomCreated.make(eventValue.payload),
    tags: eventValue.tags,
  };
}

function roomReleasedEvent(eventValue: ProjectorEvent<RoomEvent>): ProjectorEvent<typeof roomReleased> {
  return {
    definition: roomReleased,
    eventType: roomReleased.eventType,
    payload: roomReleased.make(eventValue.payload),
    tags: eventValue.tags,
  };
}

function roomReservedEvent(eventValue: ProjectorEvent<ReservationEvent>): ProjectorEvent<typeof roomReserved> {
  return {
    definition: roomReserved,
    eventType: roomReserved.eventType,
    payload: roomReserved.make(eventValue.payload),
    tags: eventValue.tags,
  };
}

function reservationCancelledEvent(eventValue: ProjectorEvent<ReservationEvent>): ProjectorEvent<typeof reservationCancelled> {
  return {
    definition: reservationCancelled,
    eventType: reservationCancelled.eventType,
    payload: reservationCancelled.make(eventValue.payload),
    tags: eventValue.tags,
  };
}

export const validateCreateRoom = validate<RoomState, RoomInput, RejectKind>((state) =>
  state.status === "empty"
    ? undefined
    : validationReject("conflict", "room already exists", "room_exists"));

export const validateReserveRoom = validate<RoomState, ReservationInput, RejectKind>((state) =>
  state.status === "empty"
    ? validationReject("not-found", "room does not exist", "room_missing")
    : undefined);

export const validateCancelReservation = validate<ReservationState, ReservationOnlyInput, RejectKind>((state) =>
  state.status === "empty"
    ? validationReject("not-found", "reservation does not exist", "reservation_missing")
    : undefined);

export const validateReleaseRoom = validate<RoomState, RoomInput, RejectKind>((state) =>
  state.status === "empty"
    ? validationReject("not-found", "room does not exist", "room_missing")
    : undefined);

export const evolveRoomCreated = evolve<RoomState, typeof roomCreated>((state, eventValue) => {
  const payload = roomCreated.parse(eventValue.payload);
  return { status: "created", version: state.version + 1, roomId: payload.roomId, name: payload.name };
});

export const evolveRoomReleased = evolve<RoomState, typeof roomReleased>((state) => {
  if (state.status === "empty") return state;
  return { status: "released", version: state.version + 1, roomId: state.roomId, name: state.name };
});

export const evolveRoomReserved = evolve<ReservationState, typeof roomReserved>((state, eventValue) => {
  const payload = roomReserved.parse(eventValue.payload);
  return { status: "reserved", version: state.version + 1, reservationId: payload.reservationId, roomId: payload.roomId };
});

export const evolveReservationCancelled = evolve<ReservationState, typeof reservationCancelled>((state) => {
  if (state.status === "empty") return state;
  return { status: "cancelled", version: state.version + 1, reservationId: state.reservationId, roomId: state.roomId };
});

const roomProjector: ProjectorDefinition<RoomState, "room", [typeof roomCreated, typeof roomReleased]> = projector({
  id: "RoomProjector",
  version: 1,
  tag: room,
  state: roomState,
  events: [roomCreated, roomReleased],
  initialState: roomInitialState,
  handlers: {
    RoomCreated: (state, eventValue) => evolveRoomCreated(state, roomCreatedEvent(eventValue)),
    RoomReleased: (state, eventValue) => evolveRoomReleased(state, roomReleasedEvent(eventValue)),
  },
});

const reservationProjector: ProjectorDefinition<ReservationState, "reservation", [typeof roomReserved, typeof reservationCancelled]> = projector({
  id: "ReservationProjector",
  version: 1,
  tag: reservation,
  state: reservationState,
  events: [roomReserved, reservationCancelled],
  initialState: reservationInitialState,
  handlers: {
    RoomReserved: (state, eventValue) => evolveRoomReserved(state, roomReservedEvent(eventValue)),
    ReservationCancelled: (state, eventValue) => evolveReservationCancelled(state, reservationCancelledEvent(eventValue)),
  },
});

function validationDecision(result: {
  readonly kind: "reject";
  readonly rejectKind: RejectKind;
  readonly reason: string;
  readonly details?: unknown;
} | undefined) {
  return result === undefined ? undefined : reject(result.rejectKind, result.reason, result.details);
}

export const createRoomCommand = command({
  id: "create-room",
  input: roomInput,
  reads: (input) => read(roomProjector, roomTag(input.roomId)),
  handle: async (input, context) => {
    const state = await context.state(roomProjector, roomTag(input.roomId));
    const invalid = validationDecision(validateCreateRoom(state, input));
    if (invalid !== undefined) return invalid;
    context.append(roomCreated, roomCreated.make({ roomId: input.roomId, name: input.name ?? "" }));
    return done({ roomId: input.roomId, name: input.name ?? "" });
  },
});

export const reserveRoomCommand = command({
  id: "reserve-room",
  input: reservationInput,
  reads: (input) => readSet(
    read(roomProjector, roomTag(input.roomId)),
    read(reservationProjector, reservationTag(input.reservationId)),
  ),
  handle: async (input, context) => {
    const roomStateValue = await context.state(roomProjector, roomTag(input.roomId));
    const roomInvalid = validationDecision(validateReserveRoom(roomStateValue, input));
    if (roomInvalid !== undefined) return roomInvalid;
    const reservationStateValue = await context.state(reservationProjector, reservationTag(input.reservationId));
    if (reservationStateValue.status !== "empty") return reject("conflict", "reservation already exists", "reservation_exists");
    context.append(roomReserved, roomReserved.make({
      roomId: input.roomId,
      reservationId: input.reservationId,
      userId: input.userId ?? "",
    }));
    return done({ roomId: input.roomId, reservationId: input.reservationId });
  },
});

export const cancelReservationCommand = command({
  id: "cancel-reservation",
  input: reservationOnlyInput,
  reads: (input) => read(reservationProjector, reservationTag(input.reservationId)),
  handle: async (input, context) => {
    const state = await context.state(reservationProjector, reservationTag(input.reservationId));
    const invalid = validationDecision(validateCancelReservation(state, input));
    if (invalid !== undefined) return invalid;
    const roomId = state.status === "empty" ? undefined : state.roomId;
    context.append(reservationCancelled, reservationCancelled.make({
      reservationId: input.reservationId,
      ...(roomId === null || roomId === undefined ? {} : { roomId }),
    }));
    return done({ reservationId: input.reservationId });
  },
});

export const releaseRoomCommand = command({
  id: "release-room",
  input: roomInput,
  reads: (input) => read(roomProjector, roomTag(input.roomId)),
  handle: async (input, context) => {
    const state = await context.state(roomProjector, roomTag(input.roomId));
    const invalid = validationDecision(validateReleaseRoom(state, input));
    if (invalid !== undefined) return invalid;
    if (state.status === "released") return none("room is already released");
    context.append(roomReleased, roomReleased.make({ roomId: input.roomId }));
    return done({ roomId: input.roomId });
  },
});

const roomQuery = {
  id: "GetRoomStateQuery",
  version: 1,
  queryType: "GetRoomStateQuery",
  endpoint: "query",
  tagGroup: "room",
  tagProjector: "RoomProjector",
} satisfies {
  readonly id: string;
  readonly version: number;
  readonly queryType: string;
  readonly endpoint: "query";
  readonly tagGroup: string;
  readonly tagProjector: string;
};

const reservationQuery = {
  id: "GetReservationListQuery",
  version: 1,
  queryType: "GetReservationListQuery",
  endpoint: "list-query",
  tagGroup: "reservation",
  tagProjector: "ReservationProjector",
} satisfies {
  readonly id: string;
  readonly version: number;
  readonly queryType: string;
  readonly endpoint: "list-query";
  readonly tagGroup: string;
  readonly tagProjector: string;
};

export const meetingRoomViews = Object.freeze([
  { id: "RoomProjector", source: "RoomProjector", projector: "RoomProjector", deliveryClass: "immediate-preferred" },
  { id: "ReservationProjector", source: "ReservationProjector", projector: "ReservationProjector", deliveryClass: "immediate-preferred" },
] satisfies readonly DomainViewDefinition[]);

export const meetingRoomDeliveryPolicy = {
  RoomProjector: "immediate-preferred",
  ReservationProjector: "immediate-preferred",
} satisfies Readonly<Record<string, "immediate-preferred" | "queued">>;

export const meetingRoomAuthoringDomain = domain({
  events: [roomCreated, roomReserved, reservationCancelled, roomReleased],
  commands: [createRoomCommand, reserveRoomCommand, cancelReservationCommand, releaseRoomCommand],
  projectors: [roomProjector, reservationProjector],
  views: meetingRoomViews,
});

/** Runtime consumers receive only the G28 bridge, never the old define* API. */
export const meetingRoomDomain: RuntimeDomainDefinition = toRuntimeDomain(meetingRoomAuthoringDomain);

export const meetingRoomRuntimeConfig = {
  deliveryClass: "immediate-preferred",
  deliveryViews: Object.freeze([
    { id: "RoomProjector", deliveryClass: meetingRoomDeliveryPolicy.RoomProjector },
    { id: "ReservationProjector", deliveryClass: meetingRoomDeliveryPolicy.ReservationProjector },
  ]),
  projectorPayloadNames: {
    RoomProjector: "RoomState",
    ReservationProjector: "ReservationState",
  },
  queries: [roomQuery, reservationQuery],
} satisfies {
  readonly deliveryClass: "immediate-preferred" | "queued";
  readonly deliveryViews: readonly { readonly id: string; readonly deliveryClass: "immediate-preferred" | "queued" }[];
  readonly projectorPayloadNames: Readonly<Record<string, string>>;
  readonly queries: readonly {
    readonly id: string;
    readonly version: number;
    readonly queryType: string;
    readonly endpoint: "query" | "list-query";
    readonly tagGroup: string;
    readonly tagProjector: string;
  }[];
};

export const meetingRoomProjectors = { roomProjector, reservationProjector };
export const meetingRoomEvents = { roomCreated, roomReserved, reservationCancelled, roomReleased };
export const meetingRoomCommands = { createRoomCommand, reserveRoomCommand, cancelReservationCommand, releaseRoomCommand };
