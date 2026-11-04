import Foundation

protocol UserProfileRepositoryProtocol: Sendable {
    func fetch() async throws -> UserProfile
    func save(_ profile: UserProfileRequest) async throws -> UserProfile
}

actor UserProfileRepository: UserProfileRepositoryProtocol {
    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
    }

    func fetch() async throws -> UserProfile {
        try await request(method: "GET", body: nil)
    }

    func save(_ profile: UserProfileRequest) async throws -> UserProfile {
        try await request(method: "PUT", body: encoder.encode(profile))
    }

    private func request(method: String, body: Data?) async throws -> UserProfile {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/profile/me"))
        request.timeoutInterval = 20
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.httpBody = body
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        let first = try await perform(request)
        let result: (Data, HTTPURLResponse)
        if first.1.statusCode == 401 {
            _ = try await sessionManager.validAccessToken(forceRefresh: true)
            result = try await perform(request)
        } else {
            result = first
        }
        guard (200..<300).contains(result.1.statusCode) else {
            throw UserProfileError.server(statusCode: result.1.statusCode)
        }
        do { return try decoder.decode(UserProfile.self, from: result.0) }
        catch { throw UserProfileError.invalidResponse }
    }

    private func perform(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var authorized = request
        authorized.setValue("Bearer \(try await sessionManager.validAccessToken())", forHTTPHeaderField: "Authorization")
        let (data, response) = try await ResilientURLSession.data(for: authorized, using: session)
        guard let response = response as? HTTPURLResponse else { throw UserProfileError.invalidResponse }
        return (data, response)
    }
}

enum UserProfileError: Error, LocalizedError {
    case invalidResponse
    case server(statusCode: Int)

    var errorDescription: String? {
        switch self {
        case .invalidResponse: "El servidor devolvió un perfil no válido."
        case .server(404): "Completa tu perfil para continuar."
        case .server(let status): "No se pudo guardar el perfil (HTTP \(status))."
        }
    }

    var isMissing: Bool {
        if case .server(404) = self { return true }
        return false
    }
}
