import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createHttpTransport, createSekibanExecutor } from "@sekiban/dcb-client";
import { command, done, read, readExists, readSet, reject, type CommandDefinition } from "@sekiban/dcb-domain";
import {
  allCommands,
  cancelReservationCommand,
  commitReservationHoldCommand,
  confirmReservationCommand,
  createClassRoomCommand,
  createQuickReservationCommand,
  createReservationDraftCommand,
  reservationDraftCreated,
  createRoomCommand,
  createStudentCommand,
  createWeatherForecastCommand,
  deleteWeatherForecastCommand,
  dropStudentFromClassRoomCommand,
  enrollStudentInClassRoomCommand,
  executeCreateReservationDraft,
  grantUserAccessCommand,
  grantUserRoleCommand,
  normalizeOptionalId,
  recordApprovalDecisionCommand,
  registerUserCommand,
  rejectReservationCommand,
  reservationTag,
  roomProjector,
  roomTag,
  startApprovalFlowCommand,
  updateRoomCommand,
  updateUserMonthlyReservationLimitCommand,
  updateWeatherForecastLocationCommand,
} from "./domain.js";

function encoded(value: unknown): string {
  return Buffer.from(JSON.stringify(value)).toString("base64");
}

function response(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

type TagHead = { exists: boolean; head: string; state?: Record<string, unknown> };

type ReadRecord = { kind: "exists" | "state"; tag: string };

type MockConfig = {
  tags: Record<string, TagHead>;
};

function tagKey(tag: string): string {
  return tag;
}

function tagFromStateId(tagStateId: string): string {
  const parts = tagStateId.split(":");
  parts.pop();
  return parts.join(":");
}

function defaultStateForTag(tag: string): Record<string, unknown> {
  const [, content] = tag.split(":");
  if (tag.startsWith("weather:")) return { forecastId: content, location: "Kyoto" };
  if (tag.startsWith("Student:")) return { studentId: content, maxClassCount: 3, currentClassCount: 0, enrolledClassRoomIds: [] };
  if (tag.startsWith("ClassRoom:")) return { classRoomId: content, maxStudents: 10, currentStudentCount: 0 };
  if (tag.startsWith("User:")) return { userId: content, displayName: "User", email: "u@example.com", department: null };
  if (tag.startsWith("UserAccess:")) return { userId: content };
  if (tag.startsWith("Room:")) return { roomId: content, requiresApproval: false };
  if (tag.startsWith("RoomReservation:")) return { activeReservations: {} };
  if (tag.startsWith("Reservation:")) return {
    reservationId: content, roomId: "room-1", organizerId: "org", organizerName: "Org",
    startTime: "2026-09-16T10:00:00.000Z", endTime: "2026-09-16T11:00:00.000Z",
    purpose: "test", selectedEquipment: [], status: "Draft",
  };
  if (tag.startsWith("ApprovalRequest:")) return { approvalRequestId: content, reservationId: "res-1" };
  return {};
}

function createMockExecutor(config: MockConfig) {
  const commits: unknown[] = [];
  const reads: ReadRecord[] = [];
  const fetcher: typeof fetch = async (input, init) => {
    const request = input instanceof Request ? input : new Request(input, init);
    const path = new URL(request.url).pathname;
    const body = request.method === "POST" ? await request.clone().json() : undefined;
    if (path.endsWith("/tag-latest-sortable")) {
      const tag = String((body as { tag?: string })?.tag ?? "");
      reads.push({ kind: "exists", tag });
      const entry = config.tags[tagKey(tag)] ?? { exists: false, head: "" };
      return response({ exists: entry.exists, lastSortableUniqueId: entry.head });
    }
    if (path.endsWith("/tag-state")) {
      const tagStateId = String((body as { tagStateId?: string })?.tagStateId ?? "");
      const tagId = tagFromStateId(tagStateId);
      reads.push({ kind: "state", tag: tagId });
      const entry = config.tags[tagKey(tagId)] ?? { exists: false, head: "", state: {} };
      const [group, content] = tagId.split(":");
      const state = entry.state ?? (entry.exists ? defaultStateForTag(tagStateId) : {});
      const projectorMap: Record<string, string> = {
        weather: "WeatherForecastProjector",
        Student: "StudentProjector",
        ClassRoom: "ClassRoomProjector",
        User: "UserDirectoryProjector",
        UserAccess: "UserAccessProjector",
        Room: "RoomProjector",
        RoomReservation: "RoomReservationsProjector",
        Reservation: "ReservationProjector",
        ApprovalRequest: "ApprovalRequestProjector",
      };
      return response({
        payload: encoded(state),
        version: entry.exists ? 1 : 0,
        lastSortedUniqueId: entry.head,
        tagGroup: group,
        tagContent: content,
        tagProjector: projectorMap[group] ?? "UnknownProjector",
      });
    }
    if (path.endsWith("/commit")) {
      commits.push(body);
      return response({ writtenEvents: [] });
    }
    return response({ code: "not_found" }, 404);
  };
  const executor = createSekibanExecutor(createHttpTransport({ baseUrl: "https://test", fetch: fetcher }));
  return { executor, commits, reads };
}

function consistencyEntries(commit: unknown): Array<{ tag: string; lastSortableUniqueId: string }> {
  return (commit as { consistencyTags: Array<{ tag: string; lastSortableUniqueId: string }> }).consistencyTags;
}

function eventTags(commit: unknown): string[] {
  const candidates = (commit as { eventCandidates?: Array<{ tags?: string[] }> }).eventCandidates ?? [];
  return candidates.flatMap((c) => c.tags ?? []);
}

function uniqueSorted(values: string[]): string[] {
  return [...new Set(values)].sort();
}

function assertConsistency(commit: unknown, expected: Array<{ tag: string; head: string }>) {
  const actual = consistencyEntries(commit).map((e) => ({ tag: e.tag, head: e.lastSortableUniqueId }));
  assert.deepEqual(actual.sort((a, b) => a.tag.localeCompare(b.tag)), expected.sort((a, b) => a.tag.localeCompare(b.tag)));
}

function normalizeReads(values: ReadRecord[]): ReadRecord[] {
  return [...values].sort((a, b) => `${a.kind}:${a.tag}`.localeCompare(`${b.kind}:${b.tag}`));
}

function assertReads(actual: ReadRecord[], expected: ReadRecord[]) {
  for (const exp of expected) {
    assert.ok(
      actual.some((read) => read.kind === exp.kind && read.tag === exp.tag),
      `missing declared read ${exp.kind}:${exp.tag}; observed ${JSON.stringify(normalizeReads(actual))}`,
    );
  }
}

describe("all 21 commands read/event-tag/consistency matrix", () => {
  const matrix: Array<{
    name: string;
    command: CommandDefinition;
    input: Record<string, unknown>;
    tags: Record<string, TagHead>;
    expectedReads: ReadRecord[];
    expectedEventTags: string[];
    expectedConsistency: Array<{ tag: string; head: string }>;
  }> = [
    {
      name: "CreateWeatherForecast",
      command: createWeatherForecastCommand,
      input: { forecastId: "wf-1", location: "Kyoto", date: "2026-09-16", temperatureC: 20, summary: "sunny" },
      tags: {},
      expectedReads: [{ kind: "exists", tag: "weather:wf-1" }],
      expectedEventTags: ["weather:wf-1"],
      expectedConsistency: [{ tag: "weather:wf-1", head: "" }],
    },
    {
      name: "UpdateWeatherForecastLocation",
      command: updateWeatherForecastLocationCommand,
      input: { forecastId: "wf-1", newLocation: "Osaka" },
      tags: { "weather:wf-1": { exists: true, head: "suid-wf" } },
      expectedReads: [{ kind: "state", tag: "weather:wf-1" }],
      expectedEventTags: ["weather:wf-1"],
      expectedConsistency: [{ tag: "weather:wf-1", head: "suid-wf" }],
    },
    {
      name: "DeleteWeatherForecast",
      command: deleteWeatherForecastCommand,
      input: { forecastId: "wf-1" },
      tags: { "weather:wf-1": { exists: true, head: "suid-wf" } },
      expectedReads: [{ kind: "state", tag: "weather:wf-1" }],
      expectedEventTags: ["weather:wf-1"],
      expectedConsistency: [{ tag: "weather:wf-1", head: "suid-wf" }],
    },
    {
      name: "CreateStudent",
      command: createStudentCommand,
      input: { studentId: "stu-1", name: "Alice", maxClassCount: 2 },
      tags: {},
      expectedReads: [{ kind: "exists", tag: "Student:stu-1" }],
      expectedEventTags: ["Student:stu-1"],
      expectedConsistency: [{ tag: "Student:stu-1", head: "" }],
    },
    {
      name: "CreateClassRoom",
      command: createClassRoomCommand,
      input: { classRoomId: "cls-1", name: "Math", maxStudents: 20 },
      tags: {},
      expectedReads: [{ kind: "exists", tag: "ClassRoom:cls-1" }],
      expectedEventTags: ["ClassRoom:cls-1"],
      expectedConsistency: [{ tag: "ClassRoom:cls-1", head: "" }],
    },
    {
      name: "EnrollStudentInClassRoom",
      command: enrollStudentInClassRoomCommand,
      input: { studentId: "stu-1", classRoomId: "cls-1" },
      tags: {
        "Student:stu-1": { exists: true, head: "suid-stu" },
        "ClassRoom:cls-1": { exists: true, head: "suid-cls" },
      },
      expectedReads: [{ kind: "state", tag: "Student:stu-1" }, { kind: "state", tag: "ClassRoom:cls-1" }],
      expectedEventTags: ["Student:stu-1", "ClassRoom:cls-1"],
      expectedConsistency: [{ tag: "Student:stu-1", head: "suid-stu" }, { tag: "ClassRoom:cls-1", head: "suid-cls" }],
    },
    {
      name: "DropStudentFromClassRoom",
      command: dropStudentFromClassRoomCommand,
      input: { studentId: "stu-1", classRoomId: "cls-1" },
      tags: {
        "Student:stu-1": { exists: true, head: "suid-stu", state: { studentId: "stu-1", enrolledClassRoomIds: ["cls-1"] } },
        "ClassRoom:cls-1": { exists: true, head: "suid-cls" },
      },
      expectedReads: [{ kind: "state", tag: "Student:stu-1" }, { kind: "state", tag: "ClassRoom:cls-1" }],
      expectedEventTags: ["Student:stu-1", "ClassRoom:cls-1"],
      expectedConsistency: [{ tag: "Student:stu-1", head: "suid-stu" }, { tag: "ClassRoom:cls-1", head: "suid-cls" }],
    },
    {
      name: "RegisterUser",
      command: registerUserCommand,
      input: { userId: "usr-1", displayName: "Bob", email: "b@example.com", monthlyReservationLimit: 5 },
      tags: {},
      expectedReads: [{ kind: "exists", tag: "User:usr-1" }],
      expectedEventTags: ["User:usr-1"],
      expectedConsistency: [{ tag: "User:usr-1", head: "" }],
    },
    {
      name: "UpdateUserMonthlyReservationLimit",
      command: updateUserMonthlyReservationLimitCommand,
      input: { userId: "usr-1", monthlyReservationLimit: 10 },
      tags: { "User:usr-1": { exists: true, head: "suid-usr" } },
      expectedReads: [{ kind: "state", tag: "User:usr-1" }],
      expectedEventTags: ["User:usr-1"],
      expectedConsistency: [{ tag: "User:usr-1", head: "suid-usr" }],
    },
    {
      name: "GrantUserAccess",
      command: grantUserAccessCommand,
      input: { userId: "usr-1", initialRole: "member" },
      tags: { "User:usr-1": { exists: true, head: "suid-usr" }, "UserAccess:usr-1": { exists: false, head: "" } },
      expectedReads: [{ kind: "exists", tag: "UserAccess:usr-1" }, { kind: "state", tag: "User:usr-1" }],
      expectedEventTags: ["UserAccess:usr-1"],
      expectedConsistency: [{ tag: "UserAccess:usr-1", head: "" }],
    },
    {
      name: "GrantUserRole",
      command: grantUserRoleCommand,
      input: { userId: "usr-1", role: "admin" },
      tags: { "UserAccess:usr-1": { exists: true, head: "suid-ua" } },
      expectedReads: [{ kind: "state", tag: "UserAccess:usr-1" }],
      expectedEventTags: ["UserAccess:usr-1"],
      expectedConsistency: [{ tag: "UserAccess:usr-1", head: "suid-ua" }],
    },
    {
      name: "CreateRoom",
      command: createRoomCommand,
      input: { roomId: "room-1", name: "A", capacity: 10, location: "F1", equipment: [], requiresApproval: false },
      tags: {},
      expectedReads: [{ kind: "exists", tag: "Room:room-1" }],
      expectedEventTags: ["Room:room-1"],
      expectedConsistency: [{ tag: "Room:room-1", head: "" }],
    },
    {
      name: "UpdateRoom",
      command: updateRoomCommand,
      input: { roomId: "room-1", name: "A2", capacity: 12, location: "F2", equipment: [], requiresApproval: false },
      tags: { "Room:room-1": { exists: true, head: "suid-room" } },
      expectedReads: [{ kind: "state", tag: "Room:room-1" }],
      expectedEventTags: ["Room:room-1"],
      expectedConsistency: [{ tag: "Room:room-1", head: "suid-room" }],
    },
    {
      name: "CreateReservationDraft",
      command: createReservationDraftCommand,
      input: {
        reservationId: "res-2", roomId: "room-1", organizerId: "org", organizerName: "Org",
        startTime: "2026-09-16T10:00:00.000Z", endTime: "2026-09-16T11:00:00.000Z", purpose: "test", selectedEquipment: [],
      },
      tags: {},
      expectedReads: [{ kind: "exists", tag: "Reservation:res-2" }],
      expectedEventTags: ["Reservation:res-2", "Room:room-1"],
      expectedConsistency: [{ tag: "Reservation:res-2", head: "" }],
    },
    {
      name: "CreateQuickReservation",
      command: createQuickReservationCommand,
      input: {
        reservationId: "res-1", roomId: "room-1", organizerId: "org", organizerName: "Org",
        startTime: "2026-09-16T10:00:00.000Z", endTime: "2026-09-16T11:00:00.000Z", purpose: "test", selectedEquipment: [],
      },
      tags: {
        "Room:room-1": { exists: true, head: "" },
        "RoomReservation:room-1": { exists: true, head: "suid-rr" },
      },
      expectedReads: [
        { kind: "exists", tag: "Reservation:res-1" },
        { kind: "state", tag: "Room:room-1" },
        { kind: "state", tag: "RoomReservation:room-1" },
      ],
      expectedEventTags: ["Reservation:res-1", "RoomReservation:room-1"], // unique tags across multi-event quick path
      expectedConsistency: [{ tag: "Reservation:res-1", head: "" }, { tag: "RoomReservation:room-1", head: "suid-rr" }],
    },
    {
      name: "CommitReservationHold",
      command: commitReservationHoldCommand,
      input: { reservationId: "res-1", roomId: "room-1", requiresApproval: false },
      tags: { "Reservation:res-1": { exists: true, head: "suid-res" } },
      expectedReads: [{ kind: "state", tag: "Reservation:res-1" }],
      expectedEventTags: ["Reservation:res-1", "Room:room-1"],
      expectedConsistency: [{ tag: "Reservation:res-1", head: "suid-res" }],
    },
    {
      name: "ConfirmReservation",
      command: confirmReservationCommand,
      input: { reservationId: "res-1", roomId: "room-1" },
      tags: { "Reservation:res-1": { exists: true, head: "suid-res" } },
      expectedReads: [{ kind: "state", tag: "Reservation:res-1" }],
      expectedEventTags: ["Reservation:res-1", "Room:room-1"],
      expectedConsistency: [{ tag: "Reservation:res-1", head: "suid-res" }],
    },
    {
      name: "CancelReservation",
      command: cancelReservationCommand,
      input: { reservationId: "res-1", roomId: "room-1", reason: "changed plans" },
      tags: { "Reservation:res-1": { exists: true, head: "suid-res" } },
      expectedReads: [{ kind: "state", tag: "Reservation:res-1" }],
      expectedEventTags: ["Reservation:res-1", "Room:room-1"],
      expectedConsistency: [{ tag: "Reservation:res-1", head: "suid-res" }],
    },
    {
      name: "RejectReservation",
      command: rejectReservationCommand,
      input: { reservationId: "res-1", roomId: "room-1", approvalRequestId: "apr-1", reason: "denied" },
      tags: { "Reservation:res-1": { exists: true, head: "suid-res" } },
      expectedReads: [{ kind: "state", tag: "Reservation:res-1" }],
      expectedEventTags: ["Reservation:res-1", "Room:room-1"],
      expectedConsistency: [{ tag: "Reservation:res-1", head: "suid-res" }],
    },
    {
      name: "StartApprovalFlow",
      command: startApprovalFlowCommand,
      input: { approvalRequestId: "apr-1", reservationId: "res-1" },
      tags: { "Reservation:res-1": { exists: true, head: "suid-res" }, "ApprovalRequest:apr-1": { exists: false, head: "" } },
      expectedReads: [{ kind: "exists", tag: "ApprovalRequest:apr-1" }, { kind: "state", tag: "Reservation:res-1" }],
      expectedEventTags: ["ApprovalRequest:apr-1"],
      expectedConsistency: [{ tag: "ApprovalRequest:apr-1", head: "" }],
    },
    {
      name: "RecordApprovalDecision",
      command: recordApprovalDecisionCommand,
      input: { approvalRequestId: "apr-1", approverId: "mgr-1", decision: "approved" },
      tags: { "ApprovalRequest:apr-1": { exists: true, head: "suid-apr" } },
      expectedReads: [{ kind: "state", tag: "ApprovalRequest:apr-1" }],
      expectedEventTags: ["ApprovalRequest:apr-1"],
      expectedConsistency: [{ tag: "ApprovalRequest:apr-1", head: "suid-apr" }],
    },
  ];

  assert.equal(matrix.length, 21);
  assert.equal(allCommands.length, 21);

  for (const row of matrix) {
    it(`${row.name} emits expected reads, event tags, and consistency entries`, async () => {
      const { executor, commits, reads } = createMockExecutor({ tags: row.tags });
      const result = await executor.execute(row.command, row.input as never);
      assert.equal(result.kind, "committed", `${row.name} should commit`);
      assert.equal(commits.length, 1);
      const commit = commits[0];
      assertReads(reads, row.expectedReads);
      const tags = uniqueSorted(eventTags(commit));
      assert.deepEqual(tags, uniqueSorted(row.expectedEventTags), `${row.name} event tags`);
      assertConsistency(commit, row.expectedConsistency);
    });
  }
});

describe("GrantUserAccess and StartApprovalFlow assert-empty near misses", () => {
  it("GrantUserAccess rejects when UserAccess already exists", async () => {
    const { executor, commits } = createMockExecutor({
      tags: {
        "User:usr-1": { exists: true, head: "suid-usr" },
        "UserAccess:usr-1": { exists: true, head: "suid-ua-existing" },
      },
    });
    const result = await executor.execute(grantUserAccessCommand, { userId: "usr-1", initialRole: "member" });
    assert.equal(result.kind, "rejected");
    assert.equal(commits.length, 0);
  });

  it("GrantUserAccess omits User read-only head from consistency", async () => {
    const { executor, commits } = createMockExecutor({
      tags: { "User:usr-1": { exists: true, head: "suid-usr" } },
    });
    const result = await executor.execute(grantUserAccessCommand, { userId: "usr-1", initialRole: "member" });
    assert.equal(result.kind, "committed");
    assertConsistency(commits[0], [{ tag: "UserAccess:usr-1", head: "" }]);
  });

  it("StartApprovalFlow rejects when ApprovalRequest already exists", async () => {
    const { executor, commits } = createMockExecutor({
      tags: {
        "Reservation:res-1": { exists: true, head: "suid-res" },
        "ApprovalRequest:apr-1": { exists: true, head: "suid-apr-existing" },
      },
    });
    const result = await executor.execute(startApprovalFlowCommand, { approvalRequestId: "apr-1", reservationId: "res-1" });
    assert.equal(result.kind, "rejected");
    assert.equal(commits.length, 0);
  });

  it("StartApprovalFlow omits Reservation read-only head from consistency", async () => {
    const { executor, commits } = createMockExecutor({
      tags: { "Reservation:res-1": { exists: true, head: "suid-res" } },
    });
    const result = await executor.execute(startApprovalFlowCommand, { approvalRequestId: "apr-1", reservationId: "res-1" });
    assert.equal(result.kind, "committed");
    assertConsistency(commits[0], [{ tag: "ApprovalRequest:apr-1", head: "" }]);
  });
});

describe("CreateReservationDraft two-stage boundary", () => {
  it("executeCreateReservationDraft validates Room then emits reservation-only consistency", async () => {
    const { executor, commits } = createMockExecutor({
      tags: { "Room:room-1": { exists: true, head: "suid-room" } },
    });
    const result = await executeCreateReservationDraft(executor, {
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
    assertConsistency(commits[0], [{ tag: "Reservation:res-2", head: "" }]);
  });

  it("near miss: adding Room read to the command widens consistency with Room exact", async () => {
    const widenedDraft = command({
      id: "CreateReservationDraftWidenedNearMiss",
      input: createReservationDraftCommand.input,
      reads: (input) => readSet(
        readExists(reservationTag(input.reservationId!)),
        read(roomProjector, roomTag(input.roomId!)),
      ),
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
    const { executor, commits } = createMockExecutor({
      tags: { "Room:room-1": { exists: true, head: "suid-room" } },
    });
    const result = await executor.execute(widenedDraft, {
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
    const tags = consistencyEntries(commits[0]).map((e) => e.tag);
    assert.ok(tags.includes("Room:room-1"), "widened command must claim Room");
    assert.ok(tags.includes("Reservation:res-2"), "widened command still claims Reservation");
  });
});

describe("normalizeOptionalId server boundary", () => {
  it("uses the same normalized id for reads and events when pre-set", async () => {
    const fixedId = "00000000-0000-4000-8000-000000000001";
    const { executor, commits } = createMockExecutor({ tags: {} });
    const result = await executor.execute(createWeatherForecastCommand, {
      forecastId: fixedId,
      location: "Kyoto",
      date: "2026-09-16",
      temperatureC: 20,
      summary: "",
    });
    assert.equal(result.kind, "committed");
    assertConsistency(commits[0], [{ tag: `weather:${fixedId}`, head: "" }]);
    assert.equal(normalizeOptionalId(undefined).length, 36);
  });
});
