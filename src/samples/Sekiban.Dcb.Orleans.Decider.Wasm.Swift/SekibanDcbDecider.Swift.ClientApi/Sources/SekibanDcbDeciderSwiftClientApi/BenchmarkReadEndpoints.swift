import Foundation
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// Benchmark-driver read endpoints. Pairs with `BenchmarkWriteEndpoints.swift`. Each GET
// forwards a `SerializableQueryRequest` to the wasmserver's serialized query/list-query
// endpoints and returns the projector's items JSON verbatim (the benchmark driver decodes
// it as an array). The HTTP round-trip logic lives in `QueryForwarder.swift` and is shared
// with the domain-CRUD routes.
//
// Routes:
//   GET /api/rooms                                  → GetRoomListQuery (list)
//   GET /api/reservations?pageNumber=&pageSize=     → GetReservationListQuery (list)
//   GET /api/reservations/by-room/{roomId}          → GetReservationsByRoomQuery (list)
//   GET /api/weatherforecast                        → GetWeatherForecastListQuery (list)
//   GET /api/weatherforecast/count                  → GetWeatherForecastCountQuery (scalar)

func registerBenchmarkReadRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    let forwarder = QueryForwarder(wasmServerUrl: wasmServerUrl, logger: logger)

    router.get("/api/rooms") { request, _ in
        try await forwarder.listQuery(
            request: request,
            queryType: "GetRoomListQuery",
            params: "{}")
    }

    router.get("/api/reservations") { request, _ in
        try await forwarder.listQuery(
            request: request,
            queryType: "GetReservationListQuery",
            params: "{}")
    }

    router.get("/api/reservations/by-room/:roomId") { request, context in
        let roomId = context.parameters.get("roomId", as: String.self) ?? ""
        let params = try jsonObject(["roomId": roomId])
        return try await forwarder.listQuery(
            request: request,
            queryType: "GetReservationsByRoomQuery",
            params: params)
    }

    router.get("/api/weatherforecast") { request, _ in
        try await forwarder.listQuery(
            request: request,
            queryType: "GetWeatherForecastListQuery",
            params: "{}")
    }

    router.get("/api/weatherforecast/count") { request, _ in
        try await forwarder.scalarQuery(
            request: request,
            queryType: "GetWeatherForecastCountQuery",
            params: "{}")
    }
}
