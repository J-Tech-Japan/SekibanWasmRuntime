import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "../..");
const packageRoot = join(repositoryRoot, "src/wasm-projectors/typescript");
const domainModule = await import(pathToFileURL(join(packageRoot, "build/js/domain.js")).href);
const { meetingRoomDomain } = domainModule;

const roomProjector = meetingRoomDomain.projectors.find((projector) => projector.id === "RoomProjector");
const reservationProjector = meetingRoomDomain.projectors.find((projector) => projector.id === "ReservationProjector");
if (roomProjector === undefined || reservationProjector === undefined) {
  throw new Error("Pinned meeting-room domain did not expose both expected projectors.");
}

const roomEvents = [
  { eventType: "RoomCreated", payload: { roomId: "room-1", name: "Boardroom" } },
  { eventType: "RoomReserved", payload: { roomId: "room-1", reservationId: "reservation-1", userId: "user-1" } },
];
const reservationEvents = [
  { eventType: "RoomReserved", payload: { roomId: "room-1", reservationId: "reservation-1", userId: "user-1" } },
  { eventType: "ReservationCancelled", payload: { reservationId: "reservation-1", roomId: "room-1" } },
];

function reduce(projector, events) {
  return events.reduce((state, event) => {
    if (!projector.subscribedEventTypes.includes(event.eventType)) return state;
    return projector.apply(state, {
      eventType: event.eventType,
      payload: event.payload,
      eventTags: [],
      provenance: "g32",
    });
  }, projector.initialState);
}

const roomState = reduce(roomProjector, roomEvents);
const reservationState = reduce(reservationProjector, reservationEvents);
const roomSerialized = roomProjector.serializeState(roomState);
const reservationSerialized = reservationProjector.serializeState(reservationState);
const emptyRoomSerialized = roomProjector.serializeState(roomProjector.initialState);
const reference = {
  eventTypes: [...meetingRoomDomain.events.map((event) => event.eventType)].sort(),
  eventSerialization: meetingRoomDomain.eventByType.get("RoomCreated").parse({ roomId: "room-1", name: "Boardroom" }),
  room: {
    serializedState: roomSerialized,
    query: roomSerialized,
    queryForOtherRoom: emptyRoomSerialized,
  },
  reservation: {
    serializedState: reservationSerialized,
    listQuery: JSON.stringify([reservationState]),
  },
};

await mkdir(join(packageRoot, "build"), { recursive: true });
await writeFile(
  join(packageRoot, "build/reference-results.json"),
  `${JSON.stringify(reference, null, 2)}\n`,
);
console.log(JSON.stringify({ status: "PASS", reference }));
