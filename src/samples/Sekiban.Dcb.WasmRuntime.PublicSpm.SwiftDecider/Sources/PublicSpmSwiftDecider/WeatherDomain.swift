import Foundation
import SekibanWasm

// WeatherForecast Decider domain for the Swift SPM external-consumer proof.
// Mirrors the crates.io Rust and Go published-module samples (same events,
// states, and queries) so the evidence is comparable across SDK languages.

struct WeatherForecastCreated: Codable {
    var forecastId: String
    var location: String
    var temperatureC: Int32
    var summary: String
    var createdAt: String
}

struct WeatherForecastLocationUpdated: Codable {
    var forecastId: String
    var newLocation: String
    var updatedAt: String
}

struct WeatherForecastState: Codable {
    var forecastId: String
    var location: String
    var temperatureC: Int32
    var summary: String
    var createdAt: String
    var updatedAt: String?
}

// Tag projection: single-forecast state keyed by the manifest name
// `WeatherForecastProjector`.
final class WeatherForecastProjection: MultiProjection {
    static var projectorName: String { "WeatherForecastProjector" }

    private var state: WeatherForecastState?

    init() {}

    func applyEvent(eventType: String, payload: String, tags: [String]) {
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "WeatherForecastCreated":
            if let created = try? JSONDecoder().decode(WeatherForecastCreated.self, from: data) {
                state = WeatherForecastState(
                    forecastId: created.forecastId,
                    location: created.location,
                    temperatureC: created.temperatureC,
                    summary: created.summary,
                    createdAt: created.createdAt,
                    updatedAt: nil)
            }
        case "WeatherForecastLocationUpdated":
            if let updated = try? JSONDecoder().decode(WeatherForecastLocationUpdated.self, from: data) {
                state?.location = updated.newLocation
                state?.updatedAt = updated.updatedAt
            }
        default:
            break
        }
    }

    func serializeState() -> String {
        guard let state else { return "{}" }
        return encodeOrDefault(state, fallback: "{}")
    }

    func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != "{}", trimmed != "null",
              let data = trimmed.data(using: .utf8),
              let restored = try? JSONDecoder().decode(WeatherForecastState.self, from: data)
        else {
            state = nil
            return
        }
        state = restored
    }

    func executeQuery(type: String, params: String) -> String {
        _ = type
        _ = params
        return "{}"
    }

    func executeListQuery(type: String, params: String) -> String {
        _ = type
        _ = params
        return "[]"
    }
}

// In-memory multi-projection keyed by the manifest name
// `WeatherForecastMultiProjection`; answers the list and count queries.
final class WeatherForecastMultiProjection: MultiProjection {
    static var projectorName: String { "WeatherForecastMultiProjection" }

    private var forecasts: [String: WeatherForecastState] = [:]

    init() {}

    func applyEvent(eventType: String, payload: String, tags: [String]) {
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "WeatherForecastCreated":
            if let created = try? JSONDecoder().decode(WeatherForecastCreated.self, from: data) {
                forecasts[created.forecastId] = WeatherForecastState(
                    forecastId: created.forecastId,
                    location: created.location,
                    temperatureC: created.temperatureC,
                    summary: created.summary,
                    createdAt: created.createdAt,
                    updatedAt: nil)
            }
        case "WeatherForecastLocationUpdated":
            if let updated = try? JSONDecoder().decode(WeatherForecastLocationUpdated.self, from: data) {
                if var item = forecasts[updated.forecastId] {
                    item.location = updated.newLocation
                    item.updatedAt = updated.updatedAt
                    forecasts[updated.forecastId] = item
                }
            }
        default:
            break
        }
    }

    func serializeState() -> String {
        encodeOrDefault(PersistedState(forecasts: forecasts), fallback: "{\"forecasts\":{}}")
    }

    func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != "{}",
              let data = trimmed.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(PersistedState.self, from: data)
        else {
            forecasts = [:]
            return
        }
        forecasts = snapshot.forecasts
    }

    func executeQuery(type: String, params: String) -> String {
        _ = params
        switch type {
        case "GetWeatherForecastCountQuery":
            return encodeOrDefault(CountResult(count: forecasts.count),
                                   fallback: "{\"count\":\(forecasts.count)}")
        default:
            return "{}"
        }
    }

    func executeListQuery(type: String, params: String) -> String {
        guard type == "GetWeatherForecastListQuery" else { return "[]" }
        var items = Array(forecasts.values)
        if let data = params.data(using: .utf8),
           let query = try? JSONDecoder().decode(ListQueryParams.self, from: data),
           let filterId = query.forecastId, !filterId.isEmpty {
            items = items.filter { $0.forecastId == filterId }
        }
        items.sort { $0.createdAt > $1.createdAt }
        return encodeArrayOrDefault(items)
    }

    private struct PersistedState: Codable {
        var forecasts: [String: WeatherForecastState]
    }

    private struct ListQueryParams: Codable {
        var forecastId: String?
    }
}

struct CountResult: Codable {
    var count: Int
}

func encodeOrDefault<T: Encodable>(_ value: T, fallback: String) -> String {
    guard let data = try? JSONEncoder().encode(value),
          let json = String(data: data, encoding: .utf8)
    else { return fallback }
    return json
}

func encodeArrayOrDefault<T: Encodable>(_ array: [T]) -> String {
    guard let data = try? JSONEncoder().encode(array),
          let json = String(data: data, encoding: .utf8)
    else { return "[]" }
    return json
}
