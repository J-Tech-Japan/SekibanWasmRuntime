import Foundation
import SekibanWasm

/// In-memory multi-projection for weather forecasts. Populated by `WeatherForecastCreated`
/// events; the benchmark driver hits `GET /api/weatherforecast` (→ GetWeatherForecastListQuery)
/// and `GET /api/weatherforecast/count` (→ GetWeatherForecastCountQuery) against this.
public final class WeatherForecastListProjection: MultiProjection {
    public static var projectorName: String { "WeatherForecastListProjection" }

    private var forecasts: [String: WeatherForecastListItem] = [:]

    public init() {}

    public func applyEvent(eventType: String, payload: String, tags: [String]) {
        _ = tags
        guard let data = payload.data(using: .utf8) else { return }
        switch eventType {
        case "WeatherForecastCreated":
            if let created = try? JSONDecoder().decode(WeatherForecastCreated.self, from: data) {
                let key = created.forecastId.uuidString.lowercased()
                forecasts[key] = WeatherForecastListItem(
                    forecastId: key,
                    location: created.location,
                    date: created.date,
                    temperatureC: created.temperatureC,
                    summary: created.summary,
                    createdAt: created.createdAt)
            }
        default:
            break
        }
    }

    public func serializeState() -> String {
        let snapshot = PersistedState(forecasts: forecasts)
        guard let data = try? JSONEncoder().encode(snapshot),
              let json = String(data: data, encoding: .utf8)
        else { return "{}" }
        return json
    }

    public func restoreState(_ json: String) {
        let trimmed = json.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty || trimmed == "{}" {
            forecasts = [:]
            return
        }
        guard let data = trimmed.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(PersistedState.self, from: data)
        else {
            forecasts = [:]
            return
        }
        forecasts = snapshot.forecasts
    }

    public func executeQuery(type: String, params: String) -> String {
        _ = params
        switch type {
        case "GetWeatherForecastCountQuery":
            return encodeOrDefault(CountResult(count: forecasts.count),
                                   fallback: "{\"count\":\(forecasts.count)}")
        default:
            return "{}"
        }
    }

    public func executeListQuery(type: String, params: String) -> String {
        _ = params
        guard type == "GetWeatherForecastListQuery" else { return "[]" }
        let items = forecasts.values.sorted { $0.forecastId < $1.forecastId }
        return encodeArrayOrDefault(Array(items))
    }

    private struct PersistedState: Codable {
        var forecasts: [String: WeatherForecastListItem]
    }
}

public struct WeatherForecastListItem: Codable, Sendable {
    public var forecastId: String
    public var location: String
    public var date: String
    public var temperatureC: Int32
    public var summary: String
    public var createdAt: String

    public init(
        forecastId: String,
        location: String,
        date: String,
        temperatureC: Int32,
        summary: String,
        createdAt: String
    ) {
        self.forecastId = forecastId
        self.location = location
        self.date = date
        self.temperatureC = temperatureC
        self.summary = summary
        self.createdAt = createdAt
    }
}
