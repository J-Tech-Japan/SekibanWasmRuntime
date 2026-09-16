import { serve } from "@hono/node-server";
import { Hono } from "hono";
import { logger } from "hono/logger";
import { v4 as uuidv4 } from "uuid";
import { createHttpTransport, createSekibanExecutor } from "@sekiban/dcb-client";
import {
  cancelReservationCommand,
  commitReservationHoldCommand,
  confirmReservationCommand,
  createClassRoomCommand,
  createQuickReservationCommand,
  createRoomCommand,
  createStudentCommand,
  createWeatherForecastCommand,
  deleteWeatherForecastCommand,
  dropStudentFromClassRoomCommand,
  enrollStudentInClassRoomCommand,
  executeCreateReservationDraft,
  grantUserAccessCommand,
  grantUserRoleCommand,
  recordApprovalDecisionCommand,
  registerUserCommand,
  rejectReservationCommand,
  startApprovalFlowCommand,
  updateRoomCommand,
  updateUserMonthlyReservationLimitCommand,
  updateWeatherForecastLocationCommand,
} from "./domain.js";
import { executeOrThrow, writeErrorFromCommand } from "./executorAdapter.js";
import {
  createMaterializedViewState,
  getStatus as getMaterializedViewStatus,
  listClassrooms as listMaterializedViewClassrooms,
  listEnrollments as listMaterializedViewEnrollments,
  listStudents as listMaterializedViewStudents,
  type MaterializedViewState,
} from "./materializedView.js";

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

const executor = createSekibanExecutor(createHttpTransport({ baseUrl: wasmServerURL }));

async function finalizeCommand(cmd: Parameters<typeof executeOrThrow>[1], body: unknown) {
  return (await executeOrThrow(executor, cmd, body as never)).response;
}

let materializedView: MaterializedViewState;
try {
  materializedView = await createMaterializedViewState();
} catch (error) {
  console.warn(
    "Materialized view initialization failed; /api/mv/* endpoints will return 503 while non-MV endpoints remain available.",
    error,
  );
  materializedView = { pool: null };
}

const app = new Hono();
app.use("*", logger());

function parseOptionalInt(value: string | undefined): number | undefined {
  if (!value) return undefined;
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : undefined;
}

async function listMemoryEnrollments(
  pageNumber?: number,
  pageSize?: number,
): Promise<string> {
  const fetchAllPageSize = Number.MAX_SAFE_INTEGER;
  const [studentsResult, classroomsResult] = await Promise.all([
    executor.listQuery({ queryType: "GetStudentListQuery", queryParamsJson: JSON.stringify({ pageNumber: 1, pageSize: fetchAllPageSize }) }),
    executor.listQuery({ queryType: "GetClassRoomListQuery", queryParamsJson: JSON.stringify({ pageNumber: 1, pageSize: fetchAllPageSize }) }),
  ]);
  const studentsJson = studentsResult.itemsJson;
  const classroomsJson = classroomsResult.itemsJson;

  const students = JSON.parse(studentsJson) as Array<{
    studentId: string;
    name: string;
    maxClassCount: number;
    enrolledClassRoomIds?: string[];
  }>;
  const classrooms = JSON.parse(classroomsJson) as Array<{
    classRoomId: string;
    name: string;
  }>;
  const classroomMap = new Map(classrooms.map((classroom) => [classroom.classRoomId, classroom]));

  const enrollments = students.flatMap((student) =>
    (student.enrolledClassRoomIds ?? [])
      .map((classRoomId) => {
        const classroom = classroomMap.get(classRoomId);
        if (!classroom) {
          return null;
        }
        return {
          studentId: student.studentId,
          studentName: student.name,
          grade: student.maxClassCount,
          classRoomId: classroom.classRoomId,
          className: classroom.name,
          enrollmentDate: new Date().toISOString(),
        };
      })
      .filter((enrollment): enrollment is NonNullable<typeof enrollment> => enrollment !== null),
  );

  const resolvedPageSize = pageSize && pageSize > 0 ? pageSize : enrollments.length || 1;
  const resolvedPageNumber = pageNumber && pageNumber > 0 ? pageNumber : 1;
  const offset = (resolvedPageNumber - 1) * resolvedPageSize;
  return JSON.stringify(enrollments.slice(offset, offset + resolvedPageSize));
}

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
    const result = await executor.listQuery({ queryType: "GetWeatherForecastListQuery", queryParamsJson: JSON.stringify(params) });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
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
    const result = await executor.query({ queryType: "GetWeatherForecastCountQuery", queryParamsJson: JSON.stringify(params) });
    return c.body(result.resultJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/weatherforecast", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(createWeatherForecastCommand, body);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/weatherforecast/update-location", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(updateWeatherForecastLocationCommand, body);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

app.post("/api/weatherforecast/delete", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(deleteWeatherForecastCommand, body);
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
    const result = await executor.listQuery({
      queryType: "GetStudentListQuery",
      queryParamsJson: JSON.stringify({
        pageNumber: parseOptionalInt(c.req.query("pageNumber")) ?? 1,
        pageSize: parseOptionalInt(c.req.query("pageSize")) ?? 20,
      }),
    });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/students", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(createStudentCommand, body);
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
    const result = await executor.listQuery({
      queryType: "GetClassRoomListQuery",
      queryParamsJson: JSON.stringify({
        pageNumber: parseOptionalInt(c.req.query("pageNumber")) ?? 1,
        pageSize: parseOptionalInt(c.req.query("pageSize")) ?? 20,
      }),
    });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/classrooms", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(createClassRoomCommand, body);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Enrollment handlers
// ---------------------------------------------------------------------------

app.get("/api/enrollments", async (c) => {
  try {
    const result = await listMemoryEnrollments(
      parseOptionalInt(c.req.query("pageNumber")),
      parseOptionalInt(c.req.query("pageSize")),
    );
    return c.body(result, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/enrollments/add", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(enrollStudentInClassRoomCommand, body);
    return c.json(resp);
  } catch (err) {
    const { status, body } = writeErrorFromCommand(err);
    return c.json(body, status as any);
  }
});

// ---------------------------------------------------------------------------
// Materialized view read handlers
// ---------------------------------------------------------------------------

app.get("/api/mv/status", async (c) => {
  try {
    return c.json(await getMaterializedViewStatus(materializedView));
  } catch (err) {
    const error = err as Error & { status?: number; code?: string };
    return c.json(
      { error: error.code ?? "MaterializedViewQueryFailed", message: error.message },
      (error.status ?? 500) as any,
    );
  }
});

app.get("/api/mv/students", async (c) => {
  try {
    return c.json(
      await listMaterializedViewStudents(materializedView, {
        pageNumber: parseOptionalInt(c.req.query("pageNumber")),
        pageSize: parseOptionalInt(c.req.query("pageSize")),
      }),
    );
  } catch (err) {
    const error = err as Error & { status?: number; code?: string };
    return c.json(
      { error: error.code ?? "MaterializedViewQueryFailed", message: error.message },
      (error.status ?? 500) as any,
    );
  }
});

app.get("/api/mv/classrooms", async (c) => {
  try {
    return c.json(
      await listMaterializedViewClassrooms(materializedView, {
        pageNumber: parseOptionalInt(c.req.query("pageNumber")),
        pageSize: parseOptionalInt(c.req.query("pageSize")),
      }),
    );
  } catch (err) {
    const error = err as Error & { status?: number; code?: string };
    return c.json(
      { error: error.code ?? "MaterializedViewQueryFailed", message: error.message },
      (error.status ?? 500) as any,
    );
  }
});

app.get("/api/mv/enrollments", async (c) => {
  try {
    return c.json(
      await listMaterializedViewEnrollments(materializedView, {
        pageNumber: parseOptionalInt(c.req.query("pageNumber")),
        pageSize: parseOptionalInt(c.req.query("pageSize")),
        studentId: c.req.query("studentId"),
        classRoomId: c.req.query("classRoomId"),
      }),
    );
  } catch (err) {
    const error = err as Error & { status?: number; code?: string };
    return c.json(
      { error: error.code ?? "MaterializedViewQueryFailed", message: error.message },
      (error.status ?? 500) as any,
    );
  }
});

app.post("/api/enrollments/drop", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(dropStudentFromClassRoomCommand, body);
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
    const result = await executor.listQuery({ queryType: "GetUserDirectoryListQuery", queryParamsJson: JSON.stringify(params) });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/users", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(registerUserCommand, body);
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
    const resp = await finalizeCommand(updateUserMonthlyReservationLimitCommand, {
      userId,
      monthlyReservationLimit: body.monthlyReservationLimit,
    });
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
    const result = await executor.listQuery({ queryType: "GetRoomListQuery", queryParamsJson: "{}" });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/rooms", async (c) => {
  try {
    const body = await c.req.json();
    const resp = await finalizeCommand(createRoomCommand, body);
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
    const resp = await finalizeCommand(updateRoomCommand, body);
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
    const result = await executor.listQuery({ queryType: "GetReservationListQuery", queryParamsJson: JSON.stringify(params) });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.get("/api/reservations/by-room/:roomId", async (c) => {
  const roomId = c.req.param("roomId");
  const params = { roomId };
  try {
    const result = await executor.listQuery({ queryType: "GetReservationListQuery", queryParamsJson: JSON.stringify(params) });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/reservations/draft", async (c) => {
  try {
    const body = await c.req.json();
    const resp = (await executeCreateReservationDraft(executor, body)).response;
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
    const confirmResp = await finalizeCommand(createQuickReservationCommand, body);

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
    const resp = await finalizeCommand(commitReservationHoldCommand, body);
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
    const resp = await finalizeCommand(confirmReservationCommand, body);
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
    const resp = await finalizeCommand(cancelReservationCommand, body);
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
    const resp = await finalizeCommand(rejectReservationCommand, body);
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
    const result = await executor.listQuery({ queryType: "GetApprovalInboxQuery", queryParamsJson: JSON.stringify(params) });
    return c.body(result.itemsJson, 200, { "Content-Type": "application/json" });
  } catch (err) {
    return c.json({ error: "RuntimeQueryFailed", message: err instanceof Error ? err.message : String(err) }, 502);
  }
});

app.post("/api/approvals/:approvalRequestId/decision", async (c) => {
  try {
    const approvalRequestId = c.req.param("approvalRequestId");
    const body = await c.req.json();
    body.approvalRequestId = approvalRequestId;
    const resp = await finalizeCommand(recordApprovalDecisionCommand, body);
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
    try {
      await finalizeCommand(createRoomCommand, {
      roomId,
      name: rm.name,
      capacity: rm.capacity,
      location: rm.location,
      equipment: rm.equipment,
      requiresApproval: rm.requiresApproval,
    });
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
  try {
    await finalizeCommand(registerUserCommand, {
    userId,
    displayName: "Test Organizer",
    email: "test@example.com",
    monthlyReservationLimit: count + 10,
  });
  } catch {
    // ignore if user already exists
  }

  // Get room list
  let roomsJSON: string;
  try {
    roomsJSON = (await executor.listQuery({ queryType: "GetRoomListQuery", queryParamsJson: "{}" })).itemsJson;
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
      await executeCreateReservationDraft(executor, {
        reservationId: resId,
        roomId: room.roomId,
        organizerId: userId,
        organizerName: "Test Organizer",
        startTime: startTime.toISOString(),
        endTime: endTime.toISOString(),
        purpose,
        selectedEquipment: [],
      });
      await finalizeCommand(commitReservationHoldCommand, {
        reservationId: resId,
        roomId: room.roomId,
        requiresApproval: false,
      });
      await finalizeCommand(confirmReservationCommand, {
        reservationId: resId,
        roomId: room.roomId,
      });

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