import Foundation
import Crypto

// Minimal HMAC-SHA256 JWT implementation — enough to round-trip a user id + email + expiry
// through a cookie without pulling in a full JWT framework. The format follows RFC 7519's
// three-base64url-segment layout (`header.payload.signature`) so standard JWT decoders can
// read the tokens too if the benchmark/test tooling wants to.

public struct AuthTokenClaims: Codable, Sendable, Equatable {
    public var sub: String
    public var email: String
    public var name: String?
    public var iat: Int64
    public var exp: Int64

    public init(sub: String, email: String, name: String?, iat: Int64, exp: Int64) {
        self.sub = sub
        self.email = email
        self.name = name
        self.iat = iat
        self.exp = exp
    }
}

public enum AuthTokenError: Error, CustomStringConvertible {
    case encodingFailed
    case malformedToken
    case signatureMismatch
    case expired

    public var description: String {
        switch self {
        case .encodingFailed: return "failed to encode JWT payload"
        case .malformedToken: return "malformed JWT (expected 3 segments)"
        case .signatureMismatch: return "JWT signature did not verify"
        case .expired: return "JWT has expired"
        }
    }
}

public struct AuthTokenCodec: Sendable {
    public let secret: SymmetricKey

    /// `secret` is the raw shared-secret bytes (any length ≥ 32 bytes recommended).
    public init(secret: Data) {
        self.secret = SymmetricKey(data: secret)
    }

    public init(secretString: String) {
        let bytes = Array(secretString.utf8)
        // Pad/truncate to at least 32 bytes so short env values don't cripple HMAC keyspace.
        var material = bytes
        while material.count < 32 {
            material.append(contentsOf: bytes)
        }
        material = Array(material.prefix(max(32, material.count)))
        self.secret = SymmetricKey(data: Data(material))
    }

    public func issue(_ claims: AuthTokenClaims) throws -> String {
        let header = #"{"alg":"HS256","typ":"JWT"}"#
        let headerSegment = AuthTokenCodec.base64url(Data(header.utf8))
        let payloadData = try JSONEncoder().encode(claims)
        let payloadSegment = AuthTokenCodec.base64url(payloadData)
        let signingInput = "\(headerSegment).\(payloadSegment)"
        let signature = HMAC<SHA256>.authenticationCode(
            for: Data(signingInput.utf8),
            using: secret)
        let signatureSegment = AuthTokenCodec.base64url(Data(signature))
        return "\(signingInput).\(signatureSegment)"
    }

    public func verify(
        _ token: String,
        now: Date = Date()
    ) throws -> AuthTokenClaims {
        let segments = token.split(separator: ".", omittingEmptySubsequences: false)
        guard segments.count == 3 else { throw AuthTokenError.malformedToken }
        let signingInput = "\(segments[0]).\(segments[1])"
        guard let sigData = AuthTokenCodec.base64urlDecode(String(segments[2])) else {
            throw AuthTokenError.signatureMismatch
        }
        let expected = HMAC<SHA256>.authenticationCode(
            for: Data(signingInput.utf8),
            using: secret)
        guard HMAC<SHA256>.isValidAuthenticationCode(sigData, authenticating: Data(signingInput.utf8), using: secret)
              || Data(expected) == sigData
        else {
            throw AuthTokenError.signatureMismatch
        }
        guard let payloadData = AuthTokenCodec.base64urlDecode(String(segments[1])) else {
            throw AuthTokenError.malformedToken
        }
        let claims = try JSONDecoder().decode(AuthTokenClaims.self, from: payloadData)
        if Int64(now.timeIntervalSince1970) >= claims.exp {
            throw AuthTokenError.expired
        }
        return claims
    }

    // ---------------- base64url helpers ----------------

    private static func base64url(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    private static func base64urlDecode(_ string: String) -> Data? {
        var s = string
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        while s.count % 4 != 0 { s.append("=") }
        return Data(base64Encoded: s)
    }
}

// ---------------- Password hashing (PBKDF2-style via repeated HMAC-SHA256) ----------------

public struct PasswordHasher: Sendable {
    public let iterations: Int
    public let keyLength: Int

    public init(iterations: Int = 50_000, keyLength: Int = 32) {
        self.iterations = iterations
        self.keyLength = keyLength
    }

    /// Stores hash as `pbkdf2$<iterations>$<salt-base64>$<hash-base64>`. Matches the
    /// C# ASP.NET Identity `Version3` hash conceptually but with Swift-native primitives.
    public func hash(password: String) -> String {
        var salt = Data(count: 16)
        _ = salt.withUnsafeMutableBytes { raw in
            SecRandomCopyBytes(kSecRandomDefault, raw.count, raw.baseAddress!)
        }
        let derived = derive(password: password, salt: salt)
        return "pbkdf2$\(iterations)$\(salt.base64EncodedString())$\(derived.base64EncodedString())"
    }

    public func verify(password: String, encoded: String) -> Bool {
        let parts = encoded.split(separator: "$")
        guard parts.count == 4, parts[0] == "pbkdf2" else { return false }
        guard let iters = Int(parts[1]),
              let salt = Data(base64Encoded: String(parts[2])),
              let expected = Data(base64Encoded: String(parts[3]))
        else { return false }
        let actual = derive(password: password, salt: salt, iterations: iters)
        return actual == expected
    }

    private func derive(password: String, salt: Data, iterations: Int? = nil) -> Data {
        let rounds = iterations ?? self.iterations
        let passwordData = Data(password.utf8)
        var previous = Data()
        previous.append(salt)
        previous.append(0)
        previous.append(0)
        previous.append(0)
        previous.append(1)
        var u = HMAC<SHA256>.authenticationCode(for: previous, using: SymmetricKey(data: passwordData))
        var result = Data(u)
        for _ in 1..<rounds {
            u = HMAC<SHA256>.authenticationCode(for: Data(u), using: SymmetricKey(data: passwordData))
            for i in 0..<min(keyLength, result.count) {
                result[i] ^= Data(u)[i]
            }
        }
        return result.prefix(keyLength)
    }
}
