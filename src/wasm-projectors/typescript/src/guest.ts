import { meetingRoomDomain } from "./domain.js";
import type { JsonValue, RuntimeProjectorDefinition } from "@sekiban/dcb-domain";

interface GuestInstance {
  readonly projector: RuntimeProjectorDefinition;
  state: JsonValue;
}

const instances = new Map<number, GuestInstance>();
let nextInstanceId = 1;

function findProjector(projectorName: string): RuntimeProjectorDefinition {
  const projector = meetingRoomDomain.projectors.find(
    (candidate) => candidate.id === projectorName,
  );
  if (projector === undefined) {
    throw new Error(`Unknown projector: ${projectorName}`);
  }
  return projector;
}

function getInstance(instanceId: number): GuestInstance {
  const instance = instances.get(instanceId);
  if (instance === undefined) {
    throw new Error(`Unknown projector instance: ${instanceId}`);
  }
  return instance;
}

function initialState(projector: RuntimeProjectorDefinition): JsonValue {
  return projector.initialState;
}

export function createInstance(projectorType: string): number {
  const projector = findProjector(projectorType);
  const instanceId = nextInstanceId++;
  instances.set(instanceId, {
    projector,
    state: initialState(projector),
  });
  return instanceId;
}

export function applyEvent(
  instanceId: number,
  eventType: string,
  payloadJson: string,
): void {
  const instance = getInstance(instanceId);
  if (!instance.projector.subscribedEventTypes.includes(eventType)) {
    return;
  }
  instance.state = instance.projector.apply(instance.state, {
    eventType,
    payload: JSON.parse(payloadJson) as unknown,
    eventTags: [],
    provenance: "g32",
  });
}

export function executeQuery(
  instanceId: number,
  queryType: string,
  paramsJson: string,
): string {
  const instance = getInstance(instanceId);
  if (queryType !== "GetRoomStateQuery" || instance.projector.id !== "RoomProjector") {
    return "{}";
  }

  const params = JSON.parse(paramsJson || "{}") as { roomId?: unknown };
  if (typeof params.roomId === "string") {
    const state = instance.state as { roomId?: unknown };
    if (state.roomId !== params.roomId) {
      return instance.projector.serializeState(initialState(instance.projector));
    }
  }
  return instance.projector.serializeState(instance.state);
}

export function executeListQuery(
  instanceId: number,
  queryType: string,
  paramsJson: string,
): string {
  const instance = getInstance(instanceId);
  if (
    queryType !== "GetReservationListQuery" ||
    instance.projector.id !== "ReservationProjector"
  ) {
    return "[]";
  }

  const state = instance.state as { status?: unknown; roomId?: unknown };
  if (state.status === "empty") {
    return "[]";
  }

  const params = JSON.parse(paramsJson || "{}") as { roomId?: unknown };
  if (typeof params.roomId === "string" && state.roomId !== params.roomId) {
    return "[]";
  }
  return JSON.stringify([instance.state]);
}

export function serializeState(instanceId: number): string {
  const instance = getInstance(instanceId);
  return instance.projector.serializeState(instance.state);
}

export function restoreState(instanceId: number, stateJson: string): void {
  const instance = getInstance(instanceId);
  instance.state = instance.projector.deserializeState(stateJson);
}

export function serializeEvent(eventType: string, payloadJson: string): string {
  const event = meetingRoomDomain.eventByType.get(eventType);
  if (event === undefined) {
    throw new Error(`Unknown event: ${eventType}`);
  }
  return JSON.stringify(event.parse(JSON.parse(payloadJson) as unknown));
}

export function deserializeEvent(eventType: string, json: string): string {
  return serializeEvent(eventType, json);
}

export function getEventTypes(): string[] {
  return meetingRoomDomain.events.map((event) => event.eventType);
}
