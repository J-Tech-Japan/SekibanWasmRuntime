import Foundation
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// /auth/* endpoints. Session state is carried in an HTTP-only cookie named
// `sekiban_session` that holds a HMAC-SHA256 JWT issued by the Swift ClientApi itself
// (no external IDP). Matches the Web frontend's AuthApiClient expectations:
//
//   POST /auth/register { email, password, displayName? }          → UserInfoResponse
//   POST /auth/login    { email, password, useCookies: bool }       → UserInfoResponse
//   POST /auth/logout                                              → 204
//   GET  /auth/status                                              → UserInfoResponse (unauth → isAuthenticated:false)
//   GET  /auth/me                                                  → UserInfoResponse or 401

public let SekibanSessionCookieName = "sekiban_session"
private let DefaultSessionTTLSeconds: Int64 = 60 * 60 * 24 * 7  // 7 days

func registerAuthRoutes(
    _ router: Router<BasicRequestContext>,
    store: AuthStore,
    codec: AuthTokenCodec,
    logger: Logger
) {
    router.post("/auth/register") { request, _ in
        let body: RegisterBody
        do {
            let collected = try await request.body.collect(upTo: 64 * 1024)
            body = try JSONDecoder().decode(RegisterBody.self, from: Data(buffer: collected))
        } catch {
            return authErrorResponse(.badRequest, "invalid register body: \(error)")
        }
        guard !body.email.isEmpty, !body.password.isEmpty else {
            return authErrorResponse(.badRequest, "email and password are required")
        }
        do {
            let user = try await store.register(
                email: body.email,
                password: body.password,
                displayName: body.displayName)
            return try await issueSessionResponse(for: user, codec: codec, setCookie: true)
        } catch AuthStoreError.emailAlreadyRegistered {
            return authErrorResponse(.conflict, "email already registered")
        } catch {
            logger.error("register failed: \(error)")
            return authErrorResponse(.internalServerError, "register failed: \(error)")
        }
    }

    router.post("/auth/login") { request, _ in
        let body: LoginBody
        do {
            let collected = try await request.body.collect(upTo: 64 * 1024)
            body = try JSONDecoder().decode(LoginBody.self, from: Data(buffer: collected))
        } catch {
            return authErrorResponse(.badRequest, "invalid login body: \(error)")
        }
        guard !body.email.isEmpty, !body.password.isEmpty else {
            return authErrorResponse(.badRequest, "email and password are required")
        }
        do {
            guard let user = try await store.authenticate(
                email: body.email, password: body.password)
            else {
                return authErrorResponse(.unauthorized, "invalid email or password")
            }
            let useCookies = body.useCookies ?? true
            return try await issueSessionResponse(
                for: user,
                codec: codec,
                setCookie: useCookies,
                returnTokens: !useCookies)
        } catch {
            logger.error("login failed: \(error)")
            return authErrorResponse(.internalServerError, "login failed: \(error)")
        }
    }

    router.post("/auth/refresh") { request, _ in
        // The Next.js BFF calls this when an access token is expired. We don't implement
        // a separate refresh-token rotation scheme — we simply re-verify the supplied
        // access token (which may already be expired, but retains its claims when the
        // signature is intact) and re-issue. Good enough for sample parity.
        struct RefreshBody: Decodable {
            let accessToken: String?
            let refreshToken: String?
        }
        let body: RefreshBody
        do {
            let collected = try await request.body.collect(upTo: 16 * 1024)
            body = try JSONDecoder().decode(RefreshBody.self, from: Data(buffer: collected))
        } catch {
            return authErrorResponse(.badRequest, "invalid refresh body: \(error)")
        }
        // Prefer the refresh token since an expired access token verify would throw.
        let token = body.refreshToken?.isEmpty == false ? body.refreshToken! :
            (body.accessToken ?? "")
        guard !token.isEmpty else {
            return authErrorResponse(.badRequest, "token required")
        }
        do {
            let claims = try codec.verifyAllowingExpired(token)
            let user = AuthStore.AuthUser(
                id: claims.sub, email: claims.email, displayName: claims.name ?? claims.email)
            return try await issueSessionResponse(
                for: user, codec: codec, setCookie: false, returnTokens: true)
        } catch {
            return authErrorResponse(.unauthorized, "invalid refresh token")
        }
    }

    router.post("/auth/logout") { _, _ in
        // Clear the cookie by emitting an expired one. Response body is empty (template's
        // AuthApiClient only checks IsSuccessStatusCode).
        var response = Response(status: .noContent)
        response.headers[values: .setCookie].append(
            "\(SekibanSessionCookieName)=; Max-Age=0; Path=/; HttpOnly; SameSite=Lax")
        return response
    }

    router.get("/auth/status") { request, _ in
        if let claims = try? claimsFromRequest(request, codec: codec) {
            return try jsonAuthResponse(
                UserInfoResponse(
                    id: claims.sub,
                    email: claims.email,
                    displayName: claims.name,
                    roles: [],
                    isAuthenticated: true),
                status: .ok)
        }
        return try jsonAuthResponse(
            UserInfoResponse(
                id: "",
                email: "",
                displayName: nil,
                roles: [],
                isAuthenticated: false),
            status: .ok)
    }

    router.get("/auth/me") { request, _ in
        guard let claims = try? claimsFromRequest(request, codec: codec) else {
            return authErrorResponse(.unauthorized, "not authenticated")
        }
        return try jsonAuthResponse(
            UserInfoResponse(
                id: claims.sub,
                email: claims.email,
                displayName: claims.name,
                roles: [],
                isAuthenticated: true),
            status: .ok)
    }
}

// ---------------------------------------------------------------------------
// Body DTOs
// ---------------------------------------------------------------------------

fileprivate struct RegisterBody: Decodable {
    let email: String
    let password: String
    let displayName: String?
}

fileprivate struct LoginBody: Decodable {
    let email: String
    let password: String
    let useCookies: Bool?
}

struct UserInfoResponse: Codable {
    let id: String
    let email: String
    let displayName: String?
    let roles: [String]
    let isAuthenticated: Bool
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

private func issueSessionResponse(
    for user: AuthStore.AuthUser,
    codec: AuthTokenCodec,
    setCookie: Bool,
    returnTokens: Bool = false
) async throws -> Response {
    let now = Int64(Date().timeIntervalSince1970)
    let expiresAt = now + DefaultSessionTTLSeconds
    let claims = AuthTokenClaims(
        sub: user.id,
        email: user.email,
        name: user.displayName,
        iat: now,
        exp: expiresAt)
    let token = try codec.issue(claims)

    // ISO-8601 timestamps for the Next.js BFF's tokenResponseSchema.
    let isoFormatter = ISO8601DateFormatter()
    isoFormatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    let expiresIso = isoFormatter.string(from: Date(timeIntervalSince1970: TimeInterval(expiresAt)))

    var response: Response
    if returnTokens {
        let tokens = TokenResponse(
            accessToken: token,
            refreshToken: token,
            accessTokenExpires: expiresIso,
            refreshTokenExpires: expiresIso)
        response = try jsonAuthResponse(tokens, status: .ok)
    } else {
        let info = UserInfoResponse(
            id: user.id,
            email: user.email,
            displayName: user.displayName,
            roles: [],
            isAuthenticated: true)
        response = try jsonAuthResponse(info, status: .ok)
    }
    if setCookie {
        response.headers[values: .setCookie].append(
            "\(SekibanSessionCookieName)=\(token); Max-Age=\(DefaultSessionTTLSeconds); Path=/; HttpOnly; SameSite=Lax")
    }
    return response
}

private struct TokenResponse: Codable {
    let accessToken: String
    let refreshToken: String
    let accessTokenExpires: String
    let refreshTokenExpires: String
}

private func claimsFromRequest(_ request: Request, codec: AuthTokenCodec) throws -> AuthTokenClaims {
    // Cookie header might be multi-value; merge and split.
    let cookieHeaders = request.headers[values: .cookie]
    let raw = cookieHeaders.joined(separator: "; ")
    for pair in raw.split(separator: ";") {
        let trimmed = pair.trimmingCharacters(in: .whitespaces)
        if trimmed.hasPrefix("\(SekibanSessionCookieName)=") {
            let token = String(trimmed.dropFirst(SekibanSessionCookieName.count + 1))
            return try codec.verify(token)
        }
    }
    // Also accept Bearer token for programmatic access.
    if let auth = request.headers[values: .authorization].first,
       auth.lowercased().hasPrefix("bearer ") {
        let token = String(auth.dropFirst("bearer ".count))
        return try codec.verify(token)
    }
    throw AuthTokenError.malformedToken
}

private func jsonAuthResponse<T: Encodable>(_ value: T, status: HTTPResponse.Status) throws -> Response {
    let data = try JSONEncoder().encode(value)
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return Response(
        status: status,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

private func authErrorResponse(_ status: HTTPResponse.Status, _ message: String) -> Response {
    let data = (try? JSONSerialization.data(withJSONObject: ["error": message]))
        ?? Data("{\"error\":\"\(message)\"}".utf8)
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return Response(
        status: status,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}
