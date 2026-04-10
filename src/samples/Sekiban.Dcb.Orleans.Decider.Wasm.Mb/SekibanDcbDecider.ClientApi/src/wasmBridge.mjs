import fs from "node:fs";

function unpackPtrLen(packed) {
  const value = BigInt(packed);
  return {
    ptr: Number((value >> 32n) & 0xffff_ffffn),
    len: Number(value & 0xffff_ffffn),
  };
}

export class MoonBitClientApiWasm {
  #instance;
  #memory;
  #encoder = new TextEncoder();
  #decoder = new TextDecoder("utf-8");

  constructor(wasmPath) {
    const bytes = fs.readFileSync(wasmPath);
    const module = new WebAssembly.Module(bytes);
    this.#instance = new WebAssembly.Instance(module, {});

    const exports = this.#instance.exports;
    if (!(exports.memory instanceof WebAssembly.Memory)) {
      throw new Error("MoonBit wasm does not export memory");
    }

    this.#memory = exports.memory;

    for (const name of [
      "alloc",
      "dealloc",
      "create_weather",
      "update_weather_location",
      "delete_weather",
      "create_student",
      "create_classroom",
      "enroll",
      "drop",
      "register_user",
      "update_user_monthly_limit",
      "grant_user_access",
      "grant_user_role",
      "create_room",
      "update_room",
      "create_reservation_draft",
      "create_quick_reservation",
      "commit_reservation_hold",
      "confirm_reservation",
      "cancel_reservation",
      "reject_reservation",
      "start_approval_flow",
      "record_approval_decision",
    ]) {
      if (typeof exports[name] !== "function") {
        throw new Error(`MoonBit wasm is missing export '${name}'`);
      }
    }
  }

  #writeUtf8(value) {
    if (!value) {
      return { ptr: 0, len: 0 };
    }

    const bytes = this.#encoder.encode(value);
    const ptr = Number(this.#instance.exports.alloc(bytes.byteLength));
    new Uint8Array(this.#memory.buffer, ptr, bytes.byteLength).set(bytes);
    return { ptr, len: bytes.byteLength };
  }

  #readUtf8(ptr, len) {
    if (!ptr || !len) {
      return "";
    }

    return this.#decoder.decode(new Uint8Array(this.#memory.buffer, ptr, len));
  }

  #free(ptr, len) {
    if (!ptr) {
      return;
    }

    this.#instance.exports.dealloc(ptr, len);
  }

  #callUnary(exportName, stateJson, version, request) {
    const state = this.#writeUtf8(stateJson ?? "{}");
    const req = this.#writeUtf8(JSON.stringify(request ?? {}));

    try {
      const packed = this.#instance.exports[exportName](
        state.ptr,
        state.len,
        version ?? 0,
        req.ptr,
        req.len,
      );
      const { ptr, len } = unpackPtrLen(packed);
      const json = this.#readUtf8(ptr, len);
      this.#free(ptr, len);
      return JSON.parse(json || '{"ok":false,"status":500,"error":"EmptyResponse","message":"Empty wasm response"}');
    } finally {
      this.#free(state.ptr, state.len);
      this.#free(req.ptr, req.len);
    }
  }

  #callBinary(exportName, firstStateJson, firstVersion, secondStateJson, secondVersion, request) {
    const first = this.#writeUtf8(firstStateJson ?? "{}");
    const second = this.#writeUtf8(secondStateJson ?? "{}");
    const req = this.#writeUtf8(JSON.stringify(request ?? {}));

    try {
      const packed = this.#instance.exports[exportName](
        first.ptr,
        first.len,
        firstVersion ?? 0,
        second.ptr,
        second.len,
        secondVersion ?? 0,
        req.ptr,
        req.len,
      );
      const { ptr, len } = unpackPtrLen(packed);
      const json = this.#readUtf8(ptr, len);
      this.#free(ptr, len);
      return JSON.parse(json || '{"ok":false,"status":500,"error":"EmptyResponse","message":"Empty wasm response"}');
    } finally {
      this.#free(first.ptr, first.len);
      this.#free(second.ptr, second.len);
      this.#free(req.ptr, req.len);
    }
  }

  #callTernary(
    exportName,
    firstStateJson,
    firstVersion,
    secondStateJson,
    secondVersion,
    thirdStateJson,
    thirdVersion,
    request,
  ) {
    const first = this.#writeUtf8(firstStateJson ?? "{}");
    const second = this.#writeUtf8(secondStateJson ?? "{}");
    const third = this.#writeUtf8(thirdStateJson ?? "{}");
    const req = this.#writeUtf8(JSON.stringify(request ?? {}));

    try {
      const packed = this.#instance.exports[exportName](
        first.ptr,
        first.len,
        firstVersion ?? 0,
        second.ptr,
        second.len,
        secondVersion ?? 0,
        third.ptr,
        third.len,
        thirdVersion ?? 0,
        req.ptr,
        req.len,
      );
      const { ptr, len } = unpackPtrLen(packed);
      const json = this.#readUtf8(ptr, len);
      this.#free(ptr, len);
      return JSON.parse(json || '{"ok":false,"status":500,"error":"EmptyResponse","message":"Empty wasm response"}');
    } finally {
      this.#free(first.ptr, first.len);
      this.#free(second.ptr, second.len);
      this.#free(third.ptr, third.len);
      this.#free(req.ptr, req.len);
    }
  }

  createWeather(stateJson, version, request) {
    return this.#callUnary("create_weather", stateJson, version, request);
  }

  updateWeatherLocation(stateJson, version, request) {
    return this.#callUnary("update_weather_location", stateJson, version, request);
  }

  deleteWeather(stateJson, version, request) {
    return this.#callUnary("delete_weather", stateJson, version, request);
  }

  createStudent(stateJson, version, request) {
    return this.#callUnary("create_student", stateJson, version, request);
  }

  createClassRoom(stateJson, version, request) {
    return this.#callUnary("create_classroom", stateJson, version, request);
  }

  enroll(studentStateJson, studentVersion, classStateJson, classVersion, request) {
    return this.#callBinary("enroll", studentStateJson, studentVersion, classStateJson, classVersion, request);
  }

  drop(studentStateJson, studentVersion, classStateJson, classVersion, request) {
    return this.#callBinary("drop", studentStateJson, studentVersion, classStateJson, classVersion, request);
  }

  registerUser(stateJson, version, request) {
    return this.#callUnary("register_user", stateJson, version, request);
  }

  updateUserMonthlyLimit(stateJson, version, request) {
    return this.#callUnary("update_user_monthly_limit", stateJson, version, request);
  }

  grantUserAccess(stateJson, version, request) {
    return this.#callUnary("grant_user_access", stateJson, version, request);
  }

  grantUserRole(stateJson, version, request) {
    return this.#callUnary("grant_user_role", stateJson, version, request);
  }

  createRoom(stateJson, version, request) {
    return this.#callUnary("create_room", stateJson, version, request);
  }

  updateRoom(stateJson, version, request) {
    return this.#callUnary("update_room", stateJson, version, request);
  }

  createReservationDraft(reservationStateJson, reservationVersion, roomStateJson, roomVersion, request) {
    return this.#callBinary(
      "create_reservation_draft",
      reservationStateJson,
      reservationVersion,
      roomStateJson,
      roomVersion,
      request,
    );
  }

  createQuickReservation(
    reservationStateJson,
    reservationVersion,
    roomStateJson,
    roomVersion,
    roomReservationsStateJson,
    roomReservationsVersion,
    request,
  ) {
    return this.#callTernary(
      "create_quick_reservation",
      reservationStateJson,
      reservationVersion,
      roomStateJson,
      roomVersion,
      roomReservationsStateJson,
      roomReservationsVersion,
      request,
    );
  }

  commitReservationHold(stateJson, version, request) {
    return this.#callUnary("commit_reservation_hold", stateJson, version, request);
  }

  confirmReservation(stateJson, version, request) {
    return this.#callUnary("confirm_reservation", stateJson, version, request);
  }

  cancelReservation(stateJson, version, request) {
    return this.#callUnary("cancel_reservation", stateJson, version, request);
  }

  rejectReservation(stateJson, version, request) {
    return this.#callUnary("reject_reservation", stateJson, version, request);
  }

  startApprovalFlow(stateJson, version, request) {
    return this.#callUnary("start_approval_flow", stateJson, version, request);
  }

  recordApprovalDecision(stateJson, version, request) {
    return this.#callUnary("record_approval_decision", stateJson, version, request);
  }
}
