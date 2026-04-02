import http from "node:http";
import path from "node:path";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";

import { MoonBitClientApiWasm } from "./wasmBridge.mjs";
import { HttpError, SekibanRuntimeClient } from "./runtimeClient.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const tagGroups = {
  weather: "weather",
  student: "Student",
  classRoom: "ClassRoom",
  user: "User",
  userAccess: "UserAccess",
  room: "Room",
  reservation: "Reservation",
  approvalRequest: "ApprovalRequest",
};

const appState = {
  runtime: new SekibanRuntimeClient(resolveWasmServerBase()),
  wasm: new MoonBitClientApiWasm(resolveMoonBitWasmPath()),
};

const server = http.createServer(async (req, res) => {
  try {
    await handleRequest(req, res);
  } catch (error) {
    sendError(res, error);
  }
});

const port = Number.parseInt(process.env.PORT ?? "8080", 10) || 8080;
server.listen(port, "0.0.0.0", () => {
  console.log(`MoonBit ClientApi listening on http://0.0.0.0:${port}`);
});

async function handleRequest(req, res) {
  const url = new URL(req.url ?? "/", `http://${req.headers.host ?? "127.0.0.1"}`);
  const { pathname } = url;
  const method = req.method ?? "GET";

  if (method === "OPTIONS") {
    sendNoContent(res);
    return;
  }

  if (method === "GET" && pathname === "/health") {
    sendJson(res, 200, { message: "Sekiban decider MoonBit ClientApi is running" });
    return;
  }

  if (method === "GET" && pathname === "/api/weatherforecast") {
    await handleGetWeatherForecasts(url, res);
    return;
  }

  if (method === "GET" && pathname === "/api/weatherforecast/count") {
    await handleGetWeatherForecastCount(url, res);
    return;
  }

  if (method === "POST" && pathname === "/api/weatherforecast") {
    await handleCreateWeatherForecast(req, res);
    return;
  }

  if (method === "POST" && pathname === "/api/weatherforecast/update-location") {
    await handleUpdateWeatherLocation(req, res);
    return;
  }

  if (method === "POST" && pathname === "/api/weatherforecast/delete") {
    await handleDeleteWeatherForecast(req, res);
    return;
  }

  if (method === "GET" && pathname === "/api/students") {
    await handleGetStudents(url, res);
    return;
  }

  if (method === "POST" && pathname === "/api/students") {
    await handleCreateStudent(req, res);
    return;
  }

  const studentMatch = pathname.match(/^\/api\/students\/([^/]+)$/);
  if (method === "GET" && studentMatch) {
    await handleGetStudent(studentMatch[1], res);
    return;
  }

  if (method === "GET" && pathname === "/api/classrooms") {
    await handleGetClassRooms(url, res);
    return;
  }

  if (method === "POST" && pathname === "/api/classrooms") {
    await handleCreateClassRoom(req, res);
    return;
  }

  const classroomMatch = pathname.match(/^\/api\/classrooms\/([^/]+)$/);
  if (method === "GET" && classroomMatch) {
    await handleGetClassRoom(classroomMatch[1], res);
    return;
  }

  if (method === "GET" && pathname === "/api/enrollments") {
    await handleGetEnrollments(url, res);
    return;
  }

  if (method === "POST" && pathname === "/api/enrollments/add") {
    await handleEnrollStudent(req, res);
    return;
  }

  if (method === "POST" && pathname === "/api/enrollments/drop") {
    await handleDropStudent(req, res);
    return;
  }

  if (method === "GET" && pathname === "/api/users") {
    await handleGetUsers(url, res);
    return;
  }

  const updateMonthlyLimitMatch = pathname.match(/^\/api\/users\/([^/]+)\/monthly-limit$/);
  if (method === "POST" && updateMonthlyLimitMatch) {
    await handleUpdateMonthlyLimit(updateMonthlyLimitMatch[1], req, res);
    return;
  }

  if (method === "GET" && pathname === "/api/rooms") {
    await handleGetRooms(url, res);
    return;
  }

  if (method === "POST" && pathname === "/api/rooms") {
    await handleCreateRoom(req, res);
    return;
  }

  const updateRoomMatch = pathname.match(/^\/api\/rooms\/([^/]+)$/);
  if (method === "PUT" && updateRoomMatch) {
    await handleUpdateRoom(updateRoomMatch[1], req, res);
    return;
  }

  if (method === "GET" && pathname === "/api/reservations") {
    await handleGetReservations(url, res, null);
    return;
  }

  const reservationsByRoomMatch = pathname.match(/^\/api\/reservations\/by-room\/([^/]+)$/);
  if (method === "GET" && reservationsByRoomMatch) {
    await handleGetReservations(url, res, reservationsByRoomMatch[1]);
    return;
  }

  if (method === "POST" && pathname === "/api/reservations/draft") {
    await handleCreateReservationDraft(req, res);
    return;
  }

  if (method === "POST" && pathname === "/api/reservations/quick") {
    await handleQuickReservation(req, res);
    return;
  }

  const reservationHoldMatch = pathname.match(/^\/api\/reservations\/([^/]+)\/hold$/);
  if (method === "POST" && reservationHoldMatch) {
    await handleCommitReservationHold(reservationHoldMatch[1], req, res);
    return;
  }

  const reservationConfirmMatch = pathname.match(/^\/api\/reservations\/([^/]+)\/confirm$/);
  if (method === "POST" && reservationConfirmMatch) {
    await handleConfirmReservation(reservationConfirmMatch[1], req, res);
    return;
  }

  const reservationCancelMatch = pathname.match(/^\/api\/reservations\/([^/]+)\/cancel$/);
  if (method === "POST" && reservationCancelMatch) {
    await handleCancelReservation(reservationCancelMatch[1], req, res);
    return;
  }

  const reservationRejectMatch = pathname.match(/^\/api\/reservations\/([^/]+)\/reject$/);
  if (method === "POST" && reservationRejectMatch) {
    await handleRejectReservation(reservationRejectMatch[1], req, res);
    return;
  }

  if (method === "GET" && pathname === "/api/approvals") {
    await handleGetApprovals(url, res);
    return;
  }

  const approvalDecisionMatch = pathname.match(/^\/api\/approvals\/([^/]+)\/decision$/);
  if (method === "POST" && approvalDecisionMatch) {
    await handleRecordApprovalDecision(approvalDecisionMatch[1], req, res);
    return;
  }

  if (method === "POST" && pathname === "/api/test-data/generate") {
    await handleGenerateTestData(url, res);
    return;
  }

  if (method === "POST" && pathname === "/api/test-data/generate-rooms") {
    await handleGenerateRoomsOnly(res);
    return;
  }

  if (method === "POST" && pathname === "/api/test-data/generate-reservations") {
    await handleGenerateReservationsOnly(url, res);
    return;
  }

  sendJson(res, 404, { error: "NotFound", message: `No route for ${method} ${pathname}` });
}

async function handleGetWeatherForecasts(url, res) {
  const pageNumber = parseOptionalInt(url.searchParams.get("pageNumber"));
  const pageSize = parseOptionalInt(url.searchParams.get("pageSize"));
  const waitForSortableUniqueId =
    url.searchParams.get("waitForSortableId") ??
    url.searchParams.get("waitForSortableUniqueId");
  const locationFilter =
    url.searchParams.get("locationFilter") ??
    url.searchParams.get("location");

  const result = await appState.runtime.executeListQuery(
    "GetWeatherForecastListQuery",
    {
      locationFilter,
      forecastId: null,
      waitForSortableUniqueId,
      pageNumber,
      pageSize,
    },
    waitForSortableUniqueId,
  );

  sendJson(res, 200, result.items);
}

async function handleGetWeatherForecastCount(url, res) {
  const waitForSortableUniqueId =
    url.searchParams.get("waitForSortableId") ??
    url.searchParams.get("waitForSortableUniqueId");
  const locationFilter =
    url.searchParams.get("locationFilter") ??
    url.searchParams.get("location");

  const result = await appState.runtime.executeQuery(
    "GetWeatherForecastCountQuery",
    {
      locationFilter,
      forecastId: null,
      waitForSortableUniqueId,
    },
    waitForSortableUniqueId,
  );

  sendJson(res, 200, result ?? { count: 0 });
}

async function handleCreateWeatherForecast(req, res) {
  const body = await readJson(req);
  const forecastId = normalizeId(body.forecastId);
  const tagState = await appState.runtime.getTagState(tagGroups.weather, forecastId);
  const wasmResult = appState.wasm.createWeather(tagState.payloadJson, tagState.version, {
    forecastId,
    location: String(body.location ?? ""),
    date: String(body.date ?? new Date().toISOString().slice(0, 10)),
    temperatureC: Number(body.temperatureC ?? 0),
    summary: body.summary == null ? null : String(body.summary),
    nowIso: new Date().toISOString(),
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    success: true,
    forecastId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
    error: null,
  });
}

async function handleUpdateWeatherLocation(req, res) {
  const body = await readJson(req);
  const forecastId = requiredId(body.forecastId, "forecastId");
  const tagState = await appState.runtime.getTagState(tagGroups.weather, forecastId);
  const wasmResult = appState.wasm.updateWeatherLocation(tagState.payloadJson, tagState.version, {
    forecastId,
    newLocation: String(body.newLocation ?? ""),
    nowIso: new Date().toISOString(),
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    success: true,
    forecastId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
    error: null,
  });
}

async function handleDeleteWeatherForecast(req, res) {
  const body = await readJson(req);
  const forecastId = requiredId(body.forecastId, "forecastId");
  const tagState = await appState.runtime.getTagState(tagGroups.weather, forecastId);
  const wasmResult = appState.wasm.deleteWeather(tagState.payloadJson, tagState.version, {
    forecastId,
    nowIso: new Date().toISOString(),
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    success: true,
    forecastId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
    error: null,
  });
}

async function handleGetStudents(url, res) {
  const waitForSortableUniqueId = url.searchParams.get("waitForSortableUniqueId");
  const result = await appState.runtime.executeListQuery(
    "GetStudentListQuery",
    {
      pageNumber: parseOptionalInt(url.searchParams.get("pageNumber")),
      pageSize: parseOptionalInt(url.searchParams.get("pageSize")),
      waitForSortableUniqueId,
    },
    waitForSortableUniqueId,
  );

  sendJson(res, 200, result.items);
}

async function handleGetStudent(studentId, res) {
  const state = await appState.runtime.getTagState(tagGroups.student, decodeURIComponent(studentId));
  sendJson(res, 200, {
    studentId: decodeURIComponent(studentId),
    classRoomId: null,
    payload: state.payload,
    version: state.version,
  });
}

async function handleCreateStudent(req, res) {
  const body = await readJson(req);
  const studentId = normalizeId(body.studentId);
  const tagState = await appState.runtime.getTagState(tagGroups.student, studentId);
  const wasmResult = appState.wasm.createStudent(tagState.payloadJson, tagState.version, {
    studentId,
    name: String(body.name ?? ""),
    maxClassCount: Number(body.maxClassCount ?? 0),
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    studentId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
    message: "Student created successfully",
  });
}

async function handleGetClassRooms(url, res) {
  const waitForSortableUniqueId = url.searchParams.get("waitForSortableUniqueId");
  const result = await appState.runtime.executeListQuery(
    "GetClassRoomListQuery",
    {
      pageNumber: parseOptionalInt(url.searchParams.get("pageNumber")),
      pageSize: parseOptionalInt(url.searchParams.get("pageSize")),
      waitForSortableUniqueId,
    },
    waitForSortableUniqueId,
  );

  sendJson(res, 200, result.items);
}

async function handleGetClassRoom(classRoomId, res) {
  const state = await appState.runtime.getTagState(tagGroups.classRoom, decodeURIComponent(classRoomId));
  sendJson(res, 200, {
    studentId: null,
    classRoomId: decodeURIComponent(classRoomId),
    payload: state.payload,
    version: state.version,
  });
}

async function handleCreateClassRoom(req, res) {
  const body = await readJson(req);
  const classRoomId = normalizeId(body.classRoomId);
  const tagState = await appState.runtime.getTagState(tagGroups.classRoom, classRoomId);
  const wasmResult = appState.wasm.createClassRoom(tagState.payloadJson, tagState.version, {
    classRoomId,
    name: String(body.name ?? ""),
    maxStudents: Number(body.maxStudents ?? 0),
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    classRoomId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
    message: "ClassRoom created successfully",
  });
}

async function handleGetEnrollments(url, res) {
  const waitForSortableUniqueId = url.searchParams.get("waitForSortableUniqueId");
  const [students, classRooms] = await Promise.all([
    appState.runtime.executeListQuery(
      "GetStudentListQuery",
      { pageNumber: null, pageSize: null, waitForSortableUniqueId },
      waitForSortableUniqueId,
    ),
    appState.runtime.executeListQuery(
      "GetClassRoomListQuery",
      { pageNumber: null, pageSize: null, waitForSortableUniqueId },
      waitForSortableUniqueId,
    ),
  ]);

  const classRoomById = new Map(classRooms.items.map((item) => [item.classRoomId, item]));
  const enrollments = [];
  for (const student of students.items) {
    for (const classRoomId of student.enrolledClassRoomIds ?? []) {
      const classRoom = classRoomById.get(classRoomId);
      if (classRoom) {
        enrollments.push({
          studentId: student.studentId,
          studentName: student.name,
          classRoomId,
          className: classRoom.name,
          enrollmentDate: new Date().toISOString(),
        });
      }
    }
  }

  sendJson(res, 200, enrollments);
}

async function handleEnrollStudent(req, res) {
  const body = await readJson(req);
  const studentId = requiredId(body.studentId, "studentId");
  const classRoomId = requiredId(body.classRoomId, "classRoomId");
  const [studentState, classRoomState] = await Promise.all([
    appState.runtime.getTagState(tagGroups.student, studentId),
    appState.runtime.getTagState(tagGroups.classRoom, classRoomId),
  ]);

  const wasmResult = appState.wasm.enroll(
    studentState.payloadJson,
    studentState.version,
    classRoomState.payloadJson,
    classRoomState.version,
    { studentId, classRoomId },
  );

  const command = await finalizeCommand(wasmResult, [studentState, classRoomState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    studentId,
    classRoomId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
    message: "Student enrolled successfully",
  });
}

async function handleDropStudent(req, res) {
  const body = await readJson(req);
  const studentId = requiredId(body.studentId, "studentId");
  const classRoomId = requiredId(body.classRoomId, "classRoomId");
  const [studentState, classRoomState] = await Promise.all([
    appState.runtime.getTagState(tagGroups.student, studentId),
    appState.runtime.getTagState(tagGroups.classRoom, classRoomId),
  ]);

  const wasmResult = appState.wasm.drop(
    studentState.payloadJson,
    studentState.version,
    classRoomState.payloadJson,
    classRoomState.version,
    { studentId, classRoomId },
  );

  const command = await finalizeCommand(wasmResult, [studentState, classRoomState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    studentId,
    classRoomId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
    message: "Student dropped successfully",
  });
}

async function handleGetUsers(url, res) {
  const waitForSortableUniqueId = url.searchParams.get("waitForSortableUniqueId");
  const [users, accesses] = await Promise.all([
    appState.runtime.executeListQuery(
      "GetUserDirectoryListQuery",
      {
        pageNumber: parseOptionalInt(url.searchParams.get("pageNumber")),
        pageSize: parseOptionalInt(url.searchParams.get("pageSize")),
        waitForSortableUniqueId,
        activeOnly: parseOptionalBool(url.searchParams.get("activeOnly")) ?? false,
      },
      waitForSortableUniqueId,
    ),
    appState.runtime.executeListQuery(
      "GetUserAccessListQuery",
      {
        pageNumber: null,
        pageSize: null,
        waitForSortableUniqueId,
        activeOnly: false,
        roleFilter: null,
      },
      waitForSortableUniqueId,
    ),
  ]);

  const rolesByUser = new Map(accesses.items.map((item) => [item.userId, item.roles ?? []]));
  const enriched = users.items.map((item) => ({
    ...item,
    roles: rolesByUser.get(item.userId) ?? [],
  }));

  sendJson(res, 200, enriched);
}

async function handleUpdateMonthlyLimit(userIdSegment, req, res) {
  const userId = decodeURIComponent(userIdSegment);
  const body = await readJson(req);
  const tagState = await appState.runtime.getTagState(tagGroups.user, userId);
  const limit = Number(body.monthlyReservationLimit ?? 0);
  const wasmResult = appState.wasm.updateUserMonthlyLimit(tagState.payloadJson, tagState.version, {
    userId,
    monthlyReservationLimit: limit,
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    success: true,
    userId,
    monthlyReservationLimit: limit,
    sortableUniqueId: command.sortableUniqueId,
  });
}

async function handleGetRooms(url, res) {
  const waitForSortableUniqueId = url.searchParams.get("waitForSortableUniqueId");
  const result = await appState.runtime.executeListQuery(
    "GetRoomListQuery",
    {
      pageNumber: parseOptionalInt(url.searchParams.get("pageNumber")),
      pageSize: parseOptionalInt(url.searchParams.get("pageSize")),
      waitForSortableUniqueId,
    },
    waitForSortableUniqueId,
  );

  sendJson(res, 200, result.items);
}

async function handleCreateRoom(req, res) {
  const body = await readJson(req);
  const roomId = normalizeId(body.roomId);
  const tagState = await appState.runtime.getTagState(tagGroups.room, roomId);
  const wasmResult = appState.wasm.createRoom(tagState.payloadJson, tagState.version, {
    roomId,
    name: String(body.name ?? ""),
    capacity: Number(body.capacity ?? 0),
    location: String(body.location ?? ""),
    equipment: normalizeStringArray(body.equipment),
    requiresApproval: Boolean(body.requiresApproval),
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    success: true,
    roomId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
  });
}

async function handleUpdateRoom(roomIdSegment, req, res) {
  const roomId = decodeURIComponent(roomIdSegment);
  const body = await readJson(req);
  const tagState = await appState.runtime.getTagState(tagGroups.room, roomId);
  const wasmResult = appState.wasm.updateRoom(tagState.payloadJson, tagState.version, {
    roomId,
    name: String(body.name ?? ""),
    capacity: Number(body.capacity ?? 0),
    location: String(body.location ?? ""),
    equipment: normalizeStringArray(body.equipment),
    requiresApproval: Boolean(body.requiresApproval),
  });

  const command = await finalizeCommand(wasmResult, [tagState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      error: command.error,
      sortableUniqueId: command.sortableUniqueId ?? null,
    });
    return;
  }

  sendJson(res, 200, {
    success: true,
    roomId,
    eventId: command.eventId,
    sortableUniqueId: command.sortableUniqueId,
  });
}

async function handleGetReservations(url, res, roomIdFromPath) {
  const waitForSortableUniqueId = url.searchParams.get("waitForSortableUniqueId");
  const roomId = roomIdFromPath ?? url.searchParams.get("roomId");
  const result = await appState.runtime.executeListQuery(
    "GetReservationListQuery",
    {
      pageNumber: parseOptionalInt(url.searchParams.get("pageNumber")),
      pageSize: parseOptionalInt(url.searchParams.get("pageSize")),
      waitForSortableUniqueId,
      roomId: roomId ? decodeURIComponent(roomId) : null,
    },
    waitForSortableUniqueId,
  );

  sendJson(res, 200, result.items);
}

async function handleCreateReservationDraft(req, res) {
  const body = await readJson(req);
  const reservationId = normalizeId(body.reservationId);
  const roomId = requiredId(body.roomId, "roomId");
  const organizerId = normalizeId(body.organizerId);
  const organizerName = String(body.organizerName ?? "Sample User");
  const [reservationState, roomState] = await Promise.all([
    appState.runtime.getTagState(tagGroups.reservation, reservationId),
    appState.runtime.getTagState(tagGroups.room, roomId),
  ]);

  const wasmResult = appState.wasm.createReservationDraft(
    reservationState.payloadJson,
    reservationState.version,
    roomState.payloadJson,
    roomState.version,
    {
      reservationId,
      roomId,
      organizerId,
      organizerName,
      startTime: String(body.startTime ?? ""),
      endTime: String(body.endTime ?? ""),
      purpose: String(body.purpose ?? ""),
      selectedEquipment: normalizeStringArray(body.selectedEquipment),
    },
  );

  const command = await finalizeCommand(wasmResult, [reservationState, roomState]);
  if (!command.ok) {
    sendJson(res, command.status, {
      success: false,
      reservationId,
      organizerId: null,
      organizerName: null,
      requiresApproval: null,
      approvalRequestId: null,
      sortableUniqueId: command.sortableUniqueId ?? null,
      error: command.error,
    });
    return;
  }

  sendJson(res, 200, {
    success: true,
    reservationId,
    organizerId,
    organizerName,
    requiresApproval: false,
    approvalRequestId: null,
    sortableUniqueId: command.sortableUniqueId,
    error: null,
  });
}

async function handleQuickReservation(req, res) {
  const body = await readJson(req);
  const roomId = requiredId(body.roomId, "roomId");
  const roomState = await appState.runtime.getTagState(tagGroups.room, roomId);
  if (!roomState.payload?.roomId) {
    sendJson(res, 400, { error: "Room not found" });
    return;
  }

  const reservationId = randomUUID();
  const organizerId = randomUUID();
  const organizerName = "Sample User";

  const draft = await createReservationDraftCommand({
    reservationId,
    roomId,
    organizerId,
    organizerName,
    startTime: String(body.startTime ?? ""),
    endTime: String(body.endTime ?? ""),
    purpose: String(body.purpose ?? ""),
    selectedEquipment: normalizeStringArray(body.selectedEquipment),
  });
  if (!draft.ok) {
    sendJson(res, draft.status, reservationFailureBody(reservationId, draft.error, draft.sortableUniqueId));
    return;
  }

  let approvalRequestId = null;
  if (Boolean(roomState.payload.requiresApproval)) {
    approvalRequestId = randomUUID();
    const approval = await startApprovalFlowCommand({
      approvalRequestId,
      reservationId,
      roomId,
      requesterId: organizerId,
      approverIds: [],
      requestComment: body.approvalRequestComment == null ? null : String(body.approvalRequestComment),
    });
    if (!approval.ok) {
      sendJson(res, approval.status, reservationFailureBody(reservationId, approval.error, approval.sortableUniqueId));
      return;
    }
  }

  const hold = await commitReservationHoldCommand({
    reservationId,
    roomId,
    requiresApproval: Boolean(roomState.payload.requiresApproval),
    approvalRequestId,
    approvalRequestComment: body.approvalRequestComment == null ? null : String(body.approvalRequestComment),
  });
  if (!hold.ok) {
    sendJson(res, hold.status, reservationFailureBody(reservationId, hold.error, hold.sortableUniqueId));
    return;
  }

  let sortableUniqueId = hold.sortableUniqueId;
  if (!Boolean(roomState.payload.requiresApproval)) {
    const confirm = await confirmReservationCommand({
      reservationId,
      roomId,
    });
    if (!confirm.ok) {
      sendJson(res, confirm.status, reservationFailureBody(reservationId, confirm.error, confirm.sortableUniqueId));
      return;
    }
    sortableUniqueId = confirm.sortableUniqueId ?? sortableUniqueId;
  }

  sendJson(res, 200, {
    success: true,
    reservationId,
    organizerId,
    organizerName,
    requiresApproval: Boolean(roomState.payload.requiresApproval),
    approvalRequestId,
    sortableUniqueId,
    error: null,
  });
}

async function handleCommitReservationHold(reservationIdSegment, req, res) {
  const reservationId = decodeURIComponent(reservationIdSegment);
  const body = await readJson(req);
  const result = await commitReservationHoldCommand({
    reservationId,
    roomId: requiredId(body.roomId, "roomId"),
    requiresApproval: Boolean(body.requiresApproval),
    approvalRequestId: body.approvalRequestId == null ? null : String(body.approvalRequestId),
    approvalRequestComment: body.approvalRequestComment == null ? null : String(body.approvalRequestComment),
  });

  if (!result.ok) {
    sendJson(res, result.status, reservationFailureBody(reservationId, result.error, result.sortableUniqueId));
    return;
  }

  sendJson(res, 200, {
    success: true,
    reservationId,
    organizerId: null,
    organizerName: null,
    requiresApproval: Boolean(body.requiresApproval),
    approvalRequestId: body.approvalRequestId == null ? null : String(body.approvalRequestId),
    sortableUniqueId: result.sortableUniqueId,
    error: null,
  });
}

async function handleConfirmReservation(reservationIdSegment, req, res) {
  const reservationId = decodeURIComponent(reservationIdSegment);
  const body = await readJson(req);
  const result = await confirmReservationCommand({
    reservationId,
    roomId: requiredId(body.roomId, "roomId"),
  });

  if (!result.ok) {
    sendJson(res, result.status, reservationFailureBody(reservationId, result.error, result.sortableUniqueId));
    return;
  }

  sendJson(res, 200, {
    success: true,
    reservationId,
    organizerId: null,
    organizerName: null,
    requiresApproval: null,
    approvalRequestId: null,
    sortableUniqueId: result.sortableUniqueId,
    error: null,
  });
}

async function handleCancelReservation(reservationIdSegment, req, res) {
  const reservationId = decodeURIComponent(reservationIdSegment);
  const body = await readJson(req);
  const result = await cancelReservationCommand({
    reservationId,
    roomId: requiredId(body.roomId, "roomId"),
    reason: String(body.reason ?? ""),
  });

  if (!result.ok) {
    sendJson(res, result.status, reservationFailureBody(reservationId, result.error, result.sortableUniqueId));
    return;
  }

  sendJson(res, 200, {
    success: true,
    reservationId,
    organizerId: null,
    organizerName: null,
    requiresApproval: null,
    approvalRequestId: null,
    sortableUniqueId: result.sortableUniqueId,
    error: null,
  });
}

async function handleRejectReservation(reservationIdSegment, req, res) {
  const reservationId = decodeURIComponent(reservationIdSegment);
  const body = await readJson(req);
  const result = await rejectReservationCommand({
    reservationId,
    roomId: requiredId(body.roomId, "roomId"),
    approvalRequestId: requiredId(body.approvalRequestId, "approvalRequestId"),
    reason: String(body.reason ?? ""),
  });

  if (!result.ok) {
    sendJson(res, result.status, reservationFailureBody(reservationId, result.error, result.sortableUniqueId));
    return;
  }

  sendJson(res, 200, {
    success: true,
    reservationId,
    organizerId: null,
    organizerName: null,
    requiresApproval: true,
    approvalRequestId: String(body.approvalRequestId),
    sortableUniqueId: result.sortableUniqueId,
    error: null,
  });
}

async function handleGetApprovals(url, res) {
  const waitForSortableUniqueId = url.searchParams.get("waitForSortableUniqueId");
  const pendingOnly = parseOptionalBool(url.searchParams.get("pendingOnly")) ?? true;
  const approvals = await appState.runtime.executeListQuery(
    "GetApprovalInboxQuery",
    {
      pageNumber: parseOptionalInt(url.searchParams.get("pageNumber")),
      pageSize: parseOptionalInt(url.searchParams.get("pageSize")),
      waitForSortableUniqueId,
      pendingOnly,
    },
    waitForSortableUniqueId,
  );

  const views = [];
  for (const item of approvals.items) {
    const [roomState, reservationState] = await Promise.all([
      appState.runtime.getTagState(tagGroups.room, item.roomId).catch(() => null),
      appState.runtime.getTagState(tagGroups.reservation, item.reservationId).catch(() => null),
    ]);

    let status = item.status;
    if (
      reservationState?.payload?.status &&
      (reservationState.payload.status === "Cancelled" || reservationState.payload.status === "Rejected") &&
      status === "Pending"
    ) {
      status = "Cancelled";
    }

    if (pendingOnly && status !== "Pending") {
      continue;
    }

    views.push({
      approvalRequestId: item.approvalRequestId,
      reservationId: item.reservationId,
      roomId: item.roomId,
      roomName: roomState?.payload?.name || null,
      requesterId: item.requesterId,
      requestComment: item.requestComment ?? null,
      organizerId: reservationState?.payload?.organizerId || null,
      organizerName: reservationState?.payload?.organizerName || null,
      purpose: reservationState?.payload?.purpose || null,
      startTime: reservationState?.payload?.startTime || null,
      endTime: reservationState?.payload?.endTime || null,
      approverIds: item.approverIds ?? [],
      requestedAt: item.requestedAt,
      status,
    });
  }

  sendJson(res, 200, views);
}

async function handleRecordApprovalDecision(approvalRequestIdSegment, req, res) {
  const approvalRequestId = decodeURIComponent(approvalRequestIdSegment);
  const body = await readJson(req);
  const approvalState = await appState.runtime.getTagState(tagGroups.approvalRequest, approvalRequestId);

  if (!approvalState.payload?.approvalRequestId || approvalState.payload.status !== "Pending") {
    sendJson(res, 400, { error: "Approval request is not pending" });
    return;
  }

  const approverId = randomUUID();
  const decision = String(body.decision ?? "");
  const record = await recordApprovalDecisionCommand({
    approvalRequestId,
    reservationId: approvalState.payload.reservationId,
    approverId,
    decision,
    comment: body.comment == null ? null : String(body.comment),
  });

  if (!record.ok) {
    sendJson(res, record.status, {
      success: false,
      approvalRequestId,
      reservationId: approvalState.payload.reservationId,
      error: record.error,
    });
    return;
  }

  let reservationResult;
  if (decision === "Approved") {
    reservationResult = await confirmReservationCommand({
      reservationId: approvalState.payload.reservationId,
      roomId: approvalState.payload.roomId,
    });
  } else {
    reservationResult = await rejectReservationCommand({
      reservationId: approvalState.payload.reservationId,
      roomId: approvalState.payload.roomId,
      approvalRequestId,
      reason: body.comment == null ? "Rejected" : String(body.comment),
    });
  }

  if (!reservationResult.ok) {
    sendJson(res, reservationResult.status, { error: reservationResult.error });
    return;
  }

  sendJson(res, 200, {
    success: true,
    approvalRequestId,
    reservationId: approvalState.payload.reservationId,
    decision: decision === "Approved" ? "Approved" : "Rejected",
    sortableUniqueId: record.sortableUniqueId,
    reservationSortableUniqueId: reservationResult.sortableUniqueId,
  });
}

async function handleGenerateTestData(url, res) {
  const timeZoneOffsetMinutes = parseOptionalInt(url.searchParams.get("timeZoneOffsetMinutes"));
  const [userId, userName] = await generateUser();
  const roomIds = await generateRooms();
  const { reservationIds, errors } = await generateReservations(roomIds, userId, userName, timeZoneOffsetMinutes);

  sendJson(res, 200, {
    userId,
    userName,
    roomsCreated: roomIds.length,
    roomIds,
    reservationsCreated: reservationIds.length,
    reservationIds,
    errors,
    sortableUniqueId: null,
  });
}

async function handleGenerateRoomsOnly(res) {
  const roomIds = await generateRooms();
  sendJson(res, 200, {
    roomsCreated: roomIds.length,
    roomIds,
  });
}

async function handleGenerateReservationsOnly(url, res) {
  const timeZoneOffsetMinutes = parseOptionalInt(url.searchParams.get("timeZoneOffsetMinutes"));
  const roomId = url.searchParams.get("roomId");
  const [userId, userName] = await generateUser();
  const roomIds = roomId ? [decodeURIComponent(roomId)] : await generateRooms();
  const { reservationIds, errors } = await generateReservations(roomIds, userId, userName, timeZoneOffsetMinutes);

  sendJson(res, 200, {
    reservationsCreated: reservationIds.length,
    reservationIds,
    errors,
  });
}

async function finalizeCommand(wasmResult, loadedStates) {
  if (!wasmResult?.ok) {
    return {
      ok: false,
      status: wasmResult?.status ?? 400,
      error: wasmResult?.message ?? wasmResult?.error ?? "Command failed",
      sortableUniqueId: null,
    };
  }

  const commit = await appState.runtime.commitCommandOutput(wasmResult.output, loadedStates);
  return {
    ok: true,
    status: 200,
    eventId: commit.eventId,
    sortableUniqueId: commit.sortableUniqueId,
  };
}

async function createReservationDraftCommand(request) {
  const [reservationState, roomState] = await Promise.all([
    appState.runtime.getTagState(tagGroups.reservation, request.reservationId),
    appState.runtime.getTagState(tagGroups.room, request.roomId),
  ]);

  const wasmResult = appState.wasm.createReservationDraft(
    reservationState.payloadJson,
    reservationState.version,
    roomState.payloadJson,
    roomState.version,
    request,
  );

  return finalizeCommand(wasmResult, [reservationState, roomState]);
}

async function startApprovalFlowCommand(request) {
  const approvalState = await appState.runtime.getTagState(tagGroups.approvalRequest, request.approvalRequestId);
  const wasmResult = appState.wasm.startApprovalFlow(approvalState.payloadJson, approvalState.version, {
    ...request,
    nowIso: new Date().toISOString(),
  });
  return finalizeCommand(wasmResult, [approvalState]);
}

async function commitReservationHoldCommand(request) {
  const reservationState = await appState.runtime.getTagState(tagGroups.reservation, request.reservationId);
  const wasmResult = appState.wasm.commitReservationHold(
    reservationState.payloadJson,
    reservationState.version,
    request,
  );
  return finalizeCommand(wasmResult, [reservationState]);
}

async function confirmReservationCommand(request) {
  const reservationState = await appState.runtime.getTagState(tagGroups.reservation, request.reservationId);
  const wasmResult = appState.wasm.confirmReservation(
    reservationState.payloadJson,
    reservationState.version,
    {
      ...request,
      nowIso: new Date().toISOString(),
    },
  );
  return finalizeCommand(wasmResult, [reservationState]);
}

async function cancelReservationCommand(request) {
  const reservationState = await appState.runtime.getTagState(tagGroups.reservation, request.reservationId);
  const wasmResult = appState.wasm.cancelReservation(
    reservationState.payloadJson,
    reservationState.version,
    {
      ...request,
      nowIso: new Date().toISOString(),
    },
  );
  return finalizeCommand(wasmResult, [reservationState]);
}

async function rejectReservationCommand(request) {
  const reservationState = await appState.runtime.getTagState(tagGroups.reservation, request.reservationId);
  const wasmResult = appState.wasm.rejectReservation(
    reservationState.payloadJson,
    reservationState.version,
    {
      ...request,
      nowIso: new Date().toISOString(),
    },
  );
  return finalizeCommand(wasmResult, [reservationState]);
}

async function recordApprovalDecisionCommand(request) {
  const approvalState = await appState.runtime.getTagState(tagGroups.approvalRequest, request.approvalRequestId);
  const wasmResult = appState.wasm.recordApprovalDecision(
    approvalState.payloadJson,
    approvalState.version,
    {
      ...request,
      nowIso: new Date().toISOString(),
    },
  );
  return finalizeCommand(wasmResult, [approvalState]);
}

async function generateUser() {
  const userId = randomUUID();
  const userName = "Sample User";
  const userState = await appState.runtime.getTagState(tagGroups.user, userId);
  const register = await finalizeCommand(
    appState.wasm.registerUser(userState.payloadJson, userState.version, {
      userId,
      displayName: userName,
      email: `sample.user.${userId.slice(0, 8)}@example.com`,
      department: "Engineering",
      monthlyReservationLimit: 10,
      nowIso: new Date().toISOString(),
    }),
    [userState],
  );

  if (!register.ok) {
    throw new Error(register.error);
  }

  try {
    const accessState = await appState.runtime.getTagState(tagGroups.userAccess, userId);
    await finalizeCommand(
      appState.wasm.grantUserAccess(accessState.payloadJson, accessState.version, {
        userId,
        initialRole: "Admin",
        nowIso: new Date().toISOString(),
      }),
      [accessState],
    );
  } catch {
    // Keep parity with the existing sample behavior: test data generation should continue even if access grant fails.
  }

  return [userId, userName];
}

async function generateRooms() {
  const definitions = [
    ["Conference Room A", 20, "Building 1, Floor 2", ["Projector", "Whiteboard", "Video Conference"], false],
    ["Meeting Room B", 8, "Building 1, Floor 3", ["TV Screen", "Whiteboard"], false],
    ["Executive Boardroom", 16, "Building 2, Floor 5", ["Projector", "Video Conference", "Sound System", "Recording"], true],
    ["Huddle Space 1", 4, "Building 1, Floor 1", ["TV Screen"], false],
    ["Training Room", 30, "Building 3, Floor 1", ["Projector", "Multiple Screens", "Recording", "Microphones"], true],
    ["Small Meeting Room C", 6, "Building 1, Floor 2", ["Whiteboard"], false],
  ];

  const roomIds = [];
  for (const [name, capacity, location, equipment, requiresApproval] of definitions) {
    const roomId = randomUUID();
    const roomState = await appState.runtime.getTagState(tagGroups.room, roomId);
    const result = await finalizeCommand(
      appState.wasm.createRoom(roomState.payloadJson, roomState.version, {
        roomId,
        name,
        capacity,
        location,
        equipment,
        requiresApproval,
      }),
      [roomState],
    );

    if (!result.ok) {
      throw new Error(result.error);
    }
    roomIds.push(roomId);
  }

  return roomIds;
}

async function generateReservations(roomIds, organizerId, organizerName, timeZoneOffsetMinutes) {
  const baseDate = resolveLocalBaseDate(timeZoneOffsetMinutes);
  const startDate = new Date(Date.UTC(baseDate.year, baseDate.month, baseDate.day + 1, 0, 0, 0));

  const definitions = [
    [0, 0, 9, 10, "Team Standup"],
    [0, 0, 14, 16, "Sprint Planning"],
    [1, 1, 10, 11, "1:1 Meeting"],
    [2, 1, 13, 15, "Board Meeting"],
    [4, 3, 9, 17, "All-hands Training"],
  ];

  const reservationIds = [];
  const errors = [];

  for (const [roomIndex, dayOffset, startHour, endHour, purpose] of definitions) {
    if (roomIndex >= roomIds.length) {
      continue;
    }

    const roomId = roomIds[roomIndex];
    const roomState = await appState.runtime.getTagState(tagGroups.room, roomId).catch((error) => {
      errors.push(`Failed to load room for '${purpose}': ${normalizeError(error)}`);
      return null;
    });
    if (!roomState?.payload?.roomId) {
      continue;
    }

    const reservationId = randomUUID();
    const startTime = toUtcIso(startDate, dayOffset, startHour, timeZoneOffsetMinutes ?? 0);
    const endTime = toUtcIso(startDate, dayOffset, endHour, timeZoneOffsetMinutes ?? 0);

    const draft = await createReservationDraftCommand({
      reservationId,
      roomId,
      organizerId,
      organizerName,
      startTime,
      endTime,
      purpose,
      selectedEquipment: [],
    });
    if (!draft.ok) {
      errors.push(`Failed to create reservation '${purpose}': ${draft.error}`);
      continue;
    }

    let approvalRequestId = null;
    if (Boolean(roomState.payload.requiresApproval)) {
      approvalRequestId = randomUUID();
      const approval = await startApprovalFlowCommand({
        approvalRequestId,
        reservationId,
        roomId,
        requesterId: organizerId,
        approverIds: [],
        requestComment: null,
      });
      if (!approval.ok) {
        errors.push(`Failed to start approval flow '${purpose}': ${approval.error}`);
        continue;
      }
    }

    const hold = await commitReservationHoldCommand({
      reservationId,
      roomId,
      requiresApproval: Boolean(roomState.payload.requiresApproval),
      approvalRequestId,
      approvalRequestComment: null,
    });
    if (!hold.ok) {
      errors.push(`Failed to hold reservation '${purpose}': ${hold.error}`);
      continue;
    }

    if (!Boolean(roomState.payload.requiresApproval)) {
      const confirm = await confirmReservationCommand({ reservationId, roomId });
      if (!confirm.ok) {
        errors.push(`Failed to confirm reservation '${purpose}': ${confirm.error}`);
        continue;
      }
    }

    reservationIds.push(reservationId);
  }

  return { reservationIds, errors };
}

function reservationFailureBody(reservationId, error, sortableUniqueId) {
  return {
    success: false,
    reservationId,
    organizerId: null,
    organizerName: null,
    requiresApproval: null,
    approvalRequestId: null,
    sortableUniqueId: sortableUniqueId ?? null,
    error,
  };
}

async function readJson(req) {
  const chunks = [];
  for await (const chunk of req) {
    chunks.push(chunk);
  }

  if (chunks.length === 0) {
    return {};
  }

  const text = Buffer.concat(chunks).toString("utf8").trim();
  return text ? JSON.parse(text) : {};
}

function normalizeId(value) {
  const candidate = typeof value === "string" ? value.trim() : "";
  if (!candidate || candidate === "00000000-0000-0000-0000-000000000000") {
    return randomUUID();
  }
  return candidate;
}

function requiredId(value, fieldName) {
  const candidate = typeof value === "string" ? value.trim() : "";
  if (!candidate || candidate === "00000000-0000-0000-0000-000000000000") {
    throw new Error(`${fieldName} is required`);
  }
  return candidate;
}

function normalizeStringArray(value) {
  return Array.isArray(value) ? value.map((item) => String(item)) : [];
}

function parseOptionalInt(value) {
  if (value == null || value === "") {
    return null;
  }
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : null;
}

function parseOptionalBool(value) {
  if (value == null || value === "") {
    return null;
  }
  return value.toLowerCase() === "true";
}

function resolveLocalBaseDate(timeZoneOffsetMinutes) {
  const offsetMinutes = timeZoneOffsetMinutes ?? 0;
  const now = new Date(Date.now() + offsetMinutes * 60_000);
  return {
    year: now.getUTCFullYear(),
    month: now.getUTCMonth(),
    day: now.getUTCDate(),
  };
}

function toUtcIso(baseDate, dayOffset, hour, offsetMinutes) {
  const utcMillis =
    Date.UTC(
      baseDate.getUTCFullYear(),
      baseDate.getUTCMonth(),
      baseDate.getUTCDate() + dayOffset,
      hour,
      0,
      0,
      0,
    ) -
    offsetMinutes * 60_000;
  return new Date(utcMillis).toISOString();
}

function sendJson(res, status, body) {
  const payload = JSON.stringify(body);
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "access-control-allow-origin": "*",
    "access-control-allow-headers": "content-type, authorization",
    "access-control-allow-methods": "GET,POST,PUT,OPTIONS",
  });
  res.end(payload);
}

function sendNoContent(res) {
  res.writeHead(204, {
    "access-control-allow-origin": "*",
    "access-control-allow-headers": "content-type, authorization",
    "access-control-allow-methods": "GET,POST,PUT,OPTIONS",
  });
  res.end();
}

function sendError(res, error) {
  if (error instanceof HttpError) {
    sendJson(res, error.status || 502, {
      error: normalizeError(error),
      details: error.body ?? null,
    });
    return;
  }

  const message = normalizeError(error);
  const status = message.endsWith("is required") ? 400 : 500;
  sendJson(res, status, { error: message });
}

function normalizeError(error) {
  if (error instanceof Error) {
    return error.message;
  }
  return String(error ?? "Unknown error");
}

function resolveWasmServerBase() {
  for (const key of [
    "WASM_SERVER_URL",
    "services__wasmserver__https__0",
    "services__wasmserver__http__0",
    "services__wasmserver__0",
    "WASMSERVER_BASE_URL",
  ]) {
    const value = process.env[key]?.trim();
    if (value) {
      return value.replace(/\/+$/, "");
    }
  }

  return "http://localhost:5000";
}

function resolveMoonBitWasmPath() {
  for (const key of [
    "MOONBIT_WASM_PATH",
    "MOONBIT_CLIENTAPI_WASM_PATH",
    "WASM_MODULE_PATH",
  ]) {
    const value = process.env[key]?.trim();
    if (value && existsSync(value)) {
      return value;
    }
  }

  const candidate = path.resolve(
    __dirname,
    "../../moonbit/runtime/_build/wasm/release/build/sekiban-dcb-decider-moonbit.wasm",
  );
  if (existsSync(candidate)) {
    return candidate;
  }

  throw new Error(
    "MoonBit wasm module not found. Set MOONBIT_WASM_PATH or build moonbit/runtime first.",
  );
}
