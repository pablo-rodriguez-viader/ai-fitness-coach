import Foundation

struct AuthenticationTokenPair: Decodable, Sendable {
    let userId: String
    let accessToken: String
    let refreshToken: String
    let accessTokenExpiresAt: String

    var accessTokenExpiry: Date? {
        ISO8601DateFormatter.withFractionalSeconds.date(from: accessTokenExpiresAt)
            ?? ISO8601DateFormatter.withoutFractionalSeconds.date(from: accessTokenExpiresAt)
    }
}

private struct AppleSignInPayload: Encodable {
    let identityToken: String
    let nonce: String
    let displayName: String?
}

private struct RefreshPayload: Encodable {
    let refreshToken: String
}

actor AuthenticationService {
    private let baseURL: URL
    private let session: URLSession
    private let encoder: JSONEncoder
    private let decoder = JSONDecoder()

    init(baseURL: URL, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.session = session
        encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
    }

    func signInWithApple(
        identityToken: String,
        nonce: String,
        displayName: String?
    ) async throws -> AuthenticationTokenPair {
        try await send(
            path: "api/v1/auth/apple",
            payload: AppleSignInPayload(
                identityToken: identityToken,
                nonce: nonce,
                displayName: displayName)
        )
    }

    /// Personal install without Sign in with Apple (free Apple ID): the owner's secret code.
    func signInWithPersonalCode(_ code: String) async throws -> AuthenticationTokenPair {
        struct Payload: Encodable { let code: String }
        do {
            return try await send(path: "api/v1/auth/personal", payload: Payload(code: code))
        } catch AuthenticationError.unauthorized {
            throw AuthenticationError.invalidAccessCode
        } catch AuthenticationError.server(statusCode: 404) {
            throw AuthenticationError.personalSignInDisabled
        }
    }

    func refresh(refreshToken: String) async throws -> AuthenticationTokenPair {
        try await send(path: "api/v1/auth/refresh", payload: RefreshPayload(refreshToken: refreshToken))
    }

    func revoke(refreshToken: String) async throws {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/auth/revoke"))
        request.timeoutInterval = 20
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try encoder.encode(RefreshPayload(refreshToken: refreshToken))
        let (_, response) = try await session.data(for: request)
        try validate(response)
    }

    private func send<Payload: Encodable>(
        path: String,
        payload: Payload
    ) async throws -> AuthenticationTokenPair {
        var request = URLRequest(url: baseURL.appending(path: path))
        request.timeoutInterval = 20
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try encoder.encode(payload)
        let (data, response) = try await session.data(for: request)
        try validate(response, body: data)
        return try decoder.decode(AuthenticationTokenPair.self, from: data)
    }

    private func validate(_ response: URLResponse, body: Data? = nil) throws {
        guard let httpResponse = response as? HTTPURLResponse else {
            throw AuthenticationError.invalidResponse
        }
        guard (200..<300).contains(httpResponse.statusCode) else {
            if httpResponse.statusCode == 401 {
                throw AuthenticationError.unauthorized
            }
            if httpResponse.statusCode == 403, let body, Self.isBetaFull(body) {
                throw AuthenticationError.betaFull
            }
            throw AuthenticationError.server(statusCode: httpResponse.statusCode)
        }
    }

    /// The API answers 403 with `"code": "beta_full"` once Beta:MaxUsers accounts exist.
    nonisolated static func isBetaFull(_ body: Data) -> Bool {
        struct Problem: Decodable { let code: String? }
        return (try? JSONDecoder().decode(Problem.self, from: body))?.code == "beta_full"
    }
}

enum AuthenticationError: Error, LocalizedError {
    case invalidResponse
    case unauthorized
    case server(statusCode: Int)
    case missingTokens
    case invalidAppleCredential
    case notConfigured(String)
    case betaFull
    case invalidAccessCode
    case personalSignInDisabled

    var errorDescription: String? {
        switch self {
        case .invalidAccessCode:
            "El código de acceso no es correcto."
        case .personalSignInDisabled:
            "El servidor no tiene activado el acceso personal. Configura PERSONAL_ACCESS_CODE en el backend."
        case .betaFull:
            "La beta privada está completa por ahora. Te avisaremos cuando se abran nuevas plazas."
        case .invalidResponse:
            "El servidor devolvió una respuesta no válida."
        case .unauthorized:
            "La sesión ha caducado. Inicia sesión de nuevo."
        case let .server(statusCode):
            "El servidor rechazó la operación (HTTP \(statusCode))."
        case .missingTokens:
            "No hay una sesión guardada."
        case .invalidAppleCredential:
            "Apple no devolvió una credencial válida."
        case let .notConfigured(message):
            message
        }
    }
}

private extension ISO8601DateFormatter {
    static let withFractionalSeconds: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    static let withoutFractionalSeconds: ISO8601DateFormatter = {
        ISO8601DateFormatter()
    }()
}
