import Foundation

/// Fluent builder that mirrors the Rust `MvParamBuilder` so Swift projectors can write
/// `MvParamBuilder().guid("Id", id).string("Name", name).int32("Max", max).build()`.
/// Each typed method encodes a single JSON scalar and stores it as the parameter's
/// `valueJson`; the host-side parameter bridge then parses that token as the declared kind.
public final class MvParamBuilder {
    private var params: [MvParam] = []

    public init() {}

    public func build() -> [MvParam] { params }

    public func null(_ name: String) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .null, valueJson: nil))
        return self
    }

    public func string(_ name: String, _ value: String) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .string, valueJson: Self.jsonString(value)))
        return self
    }

    public func guid(_ name: String, _ value: UUID) -> MvParamBuilder {
        // Match host/DB expectations: lowercase 8-4-4-4-12 UUID form. Swift's `UUID.uuidString`
        // defaults to uppercase, so normalise.
        params.append(MvParam(name: name, kind: .guid,
                              valueJson: Self.jsonString(value.uuidString.lowercased())))
        return self
    }

    public func int32(_ name: String, _ value: Int32) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .int32, valueJson: String(value)))
        return self
    }

    public func int64(_ name: String, _ value: Int64) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .int64, valueJson: String(value)))
        return self
    }

    public func bool(_ name: String, _ value: Bool) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .boolean, valueJson: value ? "true" : "false"))
        return self
    }

    public func decimal(_ name: String, _ value: String) -> MvParamBuilder {
        // Decimals ride as JSON strings to avoid f64 drift.
        params.append(MvParam(name: name, kind: .decimal, valueJson: Self.jsonString(value)))
        return self
    }

    public func double(_ name: String, _ value: Double) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .double, valueJson: String(value)))
        return self
    }

    public func datetimeOffset(_ name: String, _ iso8601: String) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .dateTimeOffset,
                              valueJson: Self.jsonString(iso8601)))
        return self
    }

    public func bytesBase64(_ name: String, _ base64: String) -> MvParamBuilder {
        params.append(MvParam(name: name, kind: .bytes,
                              valueJson: Self.jsonString(base64)))
        return self
    }

    /// Emit a JSON string literal ("foo" with escaping). Avoids pulling in JSONEncoder for
    /// every single scalar parameter; the output is small enough to hand-encode.
    private static func jsonString(_ s: String) -> String {
        var out = "\""
        for scalar in s.unicodeScalars {
            switch scalar {
            case "\"": out.append("\\\"")
            case "\\": out.append("\\\\")
            case "\n": out.append("\\n")
            case "\r": out.append("\\r")
            case "\t": out.append("\\t")
            default:
                if scalar.value < 0x20 {
                    out.append(String(format: "\\u%04x", scalar.value))
                } else {
                    out.append(Character(scalar))
                }
            }
        }
        out.append("\"")
        return out
    }
}
