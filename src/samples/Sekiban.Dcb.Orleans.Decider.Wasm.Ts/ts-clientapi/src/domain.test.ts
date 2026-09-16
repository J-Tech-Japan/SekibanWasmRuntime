import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createHttpTransport, createSekibanExecutor } from "@sekiban/dcb-client";
import { createQuickReservationCommand, createReservationDraftCommand } from "./domain.js";

function encoded(value: unknown): string {
  return Buffer.from(JSON.stringify(value)).toString("base64");
}

function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

describe("command read/claim boundaries", () => {
  it("CreateQuickReservation claims reservation and room-reservation only", async () => {
    const commits: unknown[] = [];
    const fetcher: typeof fetch = async (input, init) => {
      const request = input instanceof Request ? input : new Request(input, init);
      const path = new URL(request.url).pathname;
      const body = request.method === "POST" ? await request.clone().json() : undefined;
      if (path.endsWith("/tag-latest-sortable")) {
        const tag = String((body as { tag?: string })?.tag ?? "");
        return response({ exists: tag.startsWith("Reservation:") ? false : true, lastSortableUniqueId: tag.includes("RoomReservation") ? "suid-rr" : "" });
      }
      if (path.endsWith("/tag-state")) {
        const tagStateId = String((body as { tagStateId?: string })?.tagStateId ?? "");
        const isRoom = tagStateId.startsWith("Room:");
        const isRoomRes = tagStateId.startsWith("RoomReservation:");
        return response({
          payload: encoded(isRoom ? { roomId: "room-1", requiresApproval: false } : isRoomRes ? { activeReservations: {} } : {}),
          version: 1,
          lastSortedUniqueId: isRoomRes ? "suid-rr" : "",
          tagGroup: isRoom ? "Room" : isRoomRes ? "RoomReservation" : "Reservation",
          tagContent: "room-1",
          tagProjector: isRoom ? "RoomProjector" : isRoomRes ? "RoomReservationsProjector" : "ReservationProjector",
        });
      }
      if (path.endsWith("/commit")) {
        commits.push(body);
        return response({ writtenEvents: [] });
      }
      return response({ code: "not_found" }, 404);
    };

    const executor = createSekibanExecutor(createHttpTransport({ baseUrl: "https://test", fetch: fetcher }));
    const result = await executor.execute(createQuickReservationCommand, {
      reservationId: "res-1",
      roomId: "room-1",
      organizerId: "org",
      organizerName: "Org",
      startTime: "2026-09-16T10:00:00.000Z",
      endTime: "2026-09-16T11:00:00.000Z",
      purpose: "test",
      selectedEquipment: [],
    });
    assert.equal(result.kind, "committed");
    const commit = commits[0] as { consistencyTags: Array<{ tag: string }> };
    const tags = commit.consistencyTags.map((entry) => entry.tag);
    assert.deepEqual(tags.sort(), ["Reservation:res-1", "RoomReservation:room-1"].sort());
    assert.equal(tags.includes("Room:room-1"), false);
  });

  it("CreateReservationDraft emits reservation-only consistency claim", async () => {
    const commits: unknown[] = [];
    const fetcher: typeof fetch = async (input, init) => {
      const request = input instanceof Request ? input : new Request(input, init);
      const path = new URL(request.url).pathname;
      const body = request.method === "POST" ? await request.clone().json() : undefined;
      if (path.endsWith("/tag-latest-sortable")) return response({ exists: false, lastSortableUniqueId: "" });
      if (path.endsWith("/tag-state")) {
        return response({
          payload: encoded({ roomId: "room-1" }),
          version: 1,
          lastSortedUniqueId: "",
          tagGroup: "Room",
          tagContent: "room-1",
          tagProjector: "RoomProjector",
        });
      }
      if (path.endsWith("/commit")) {
        commits.push(body);
        return response({ writtenEvents: [] });
      }
      return response({}, 404);
    };
    const executor = createSekibanExecutor(createHttpTransport({ baseUrl: "https://test", fetch: fetcher }));
    const result = await executor.execute(createReservationDraftCommand, {
      reservationId: "res-2",
      roomId: "room-1",
      organizerId: "org",
      organizerName: "Org",
      startTime: "2026-09-16T10:00:00.000Z",
      endTime: "2026-09-16T11:00:00.000Z",
      purpose: "test",
      selectedEquipment: [],
    });
    assert.equal(result.kind, "committed");
    const commit = commits[0] as { consistencyTags: Array<{ tag: string }> };
    assert.deepEqual(commit.consistencyTags.map((entry) => entry.tag), ["Reservation:res-2"]);
  });
});
