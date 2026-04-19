import XCTest
@testable import SekibanDcbDeciderSwiftClientApiCore

final class AuthTokenTests: XCTestCase {
    private let codec = AuthTokenCodec(secretString: "unit-test-secret-xxxxxxxxxxxxxxxxxxxxx")

    func testIssueAndVerifyRoundTripsClaims() throws {
        let now = Int64(Date().timeIntervalSince1970)
        let claims = AuthTokenClaims(
            sub: "user-1",
            email: "e2e@example.com",
            name: "Test User",
            iat: now,
            exp: now + 3600)
        let token = try codec.issue(claims)
        XCTAssertEqual(token.split(separator: ".").count, 3,
                       "JWT must have three base64url segments")
        let decoded = try codec.verify(token)
        XCTAssertEqual(decoded, claims)
    }

    func testVerifyRejectsTamperedSignature() throws {
        let now = Int64(Date().timeIntervalSince1970)
        let claims = AuthTokenClaims(
            sub: "u", email: "a@b", name: nil, iat: now, exp: now + 60)
        let token = try codec.issue(claims)
        // Replace the last char of the signature segment to break HMAC.
        let tampered = String(token.dropLast()) + (token.hasSuffix("a") ? "b" : "a")
        XCTAssertThrowsError(try codec.verify(tampered)) { error in
            XCTAssertEqual("\(error)", "\(AuthTokenError.signatureMismatch)")
        }
    }

    func testVerifyRejectsExpiredToken() throws {
        let now = Int64(Date().timeIntervalSince1970)
        let claims = AuthTokenClaims(
            sub: "u", email: "a@b", name: nil, iat: now - 3600, exp: now - 10)
        let token = try codec.issue(claims)
        XCTAssertThrowsError(try codec.verify(token)) { error in
            XCTAssertEqual("\(error)", "\(AuthTokenError.expired)")
        }
    }

    func testVerifyRejectsMalformedToken() {
        XCTAssertThrowsError(try codec.verify("not-a-jwt"))
        XCTAssertThrowsError(try codec.verify("one.two"))
    }

    func testVerifyAllowingExpiredReturnsClaimsForExpiredToken() throws {
        let now = Int64(Date().timeIntervalSince1970)
        let claims = AuthTokenClaims(
            sub: "u-refresh",
            email: "refresh@example.com",
            name: "Refresh User",
            iat: now - 3600,
            exp: now - 10)
        let token = try codec.issue(claims)
        // Regular verify rejects — it's past `exp`.
        XCTAssertThrowsError(try codec.verify(token)) { error in
            XCTAssertEqual("\(error)", "\(AuthTokenError.expired)")
        }
        // verifyAllowingExpired still returns the decoded claims as long as the
        // signature holds. This is what /auth/refresh relies on.
        let decoded = try codec.verifyAllowingExpired(token)
        XCTAssertEqual(decoded, claims)
    }

    func testVerifyAllowingExpiredStillRejectsTamperedSignature() throws {
        let now = Int64(Date().timeIntervalSince1970)
        let claims = AuthTokenClaims(
            sub: "u", email: "a@b", name: nil, iat: now - 3600, exp: now - 10)
        let token = try codec.issue(claims)
        let tampered = String(token.dropLast()) + (token.hasSuffix("a") ? "b" : "a")
        XCTAssertThrowsError(try codec.verifyAllowingExpired(tampered)) { error in
            XCTAssertEqual("\(error)", "\(AuthTokenError.signatureMismatch)")
        }
    }

    func testPasswordHasherRoundTrips() {
        let hasher = PasswordHasher(iterations: 500)  // faster for tests
        let encoded = hasher.hash(password: "correct horse battery staple")
        XCTAssertTrue(encoded.hasPrefix("pbkdf2$500$"),
                      "hash must be pbkdf2 format: \(encoded)")
        XCTAssertTrue(hasher.verify(password: "correct horse battery staple", encoded: encoded))
        XCTAssertFalse(hasher.verify(password: "wrong password", encoded: encoded))
    }

    func testPasswordHasherRejectsMalformedHash() {
        let hasher = PasswordHasher()
        XCTAssertFalse(hasher.verify(password: "x", encoded: "not-a-hash"))
        XCTAssertFalse(hasher.verify(password: "x", encoded: "pbkdf2$50000$badsalt"))
    }
}
