import Foundation

protocol EngagementRepositoryProtocol: Sendable {
    func summary() async throws -> EngagementSummary
}

enum EngagementError: Error, LocalizedError {
    case invalidResponse
    case unauthorized
    case server(statusCode: Int)

    var errorDescription: String? {
        switch self {
        case .invalidResponse: "El servidor devolvió un resumen no válido."
        case .unauthorized: "La sesión ha caducado. Inicia sesión de nuevo."
        case let .server(statusCode): "No se pudo cargar tu progreso (HTTP \(statusCode))."
        }
    }
}

actor EngagementRepository: EngagementRepositoryProtocol {
    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession
    private let decoder: JSONDecoder

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let value = try container.decode(String.self)
            guard let date = EngagementRepository.parseDate(value) else {
                throw DecodingError.dataCorruptedError(in: container, debugDescription: "Invalid ISO-8601 timestamp.")
            }
            return date
        }
        self.decoder = decoder
    }

    /// .NET writes up to 7 fractional digits; ISO8601DateFormatter is only reliable with 3.
    nonisolated static func parseDate(_ value: String) -> Date? {
        var normalized = value
        if let dot = value.firstIndex(of: "."),
           let zoneStart = value[dot...].firstIndex(where: { $0 == "Z" || $0 == "+" || $0 == "-" }) {
            let digits = value[value.index(after: dot)..<zoneStart]
            normalized = String(value[..<dot]) + "." + String(digits.prefix(3)).padding(toLength: 3, withPad: "0", startingAt: 0)
                + String(value[zoneStart...])
        }
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return fractional.date(from: normalized) ?? ISO8601DateFormatter().date(from: normalized)
    }

    nonisolated static func decode(_ data: Data) throws -> EngagementSummary {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let value = try container.decode(String.self)
            guard let date = parseDate(value) else {
                throw DecodingError.dataCorruptedError(in: container, debugDescription: "Invalid ISO-8601 timestamp.")
            }
            return date
        }
        return try decoder.decode(EngagementSummary.self, from: data)
    }

    func summary() async throws -> EngagementSummary {
        var components = URLComponents(
            url: baseURL.appending(path: "api/v1/engagement/summary"),
            resolvingAgainstBaseURL: false)
        // "Today", streaks and reminders follow the user's calendar, not UTC.
        let offsetMinutes = TimeZone.current.secondsFromGMT() / 60
        components?.queryItems = [URLQueryItem(name: "utcOffsetMinutes", value: String(offsetMinutes))]
        guard let url = components?.url else { throw EngagementError.invalidResponse }
        var request = URLRequest(url: url)
        request.timeoutInterval = 20
        request.httpMethod = "GET"
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        var result = try await execute(request)
        if result.1.statusCode == 401 {
            _ = try await sessionManager.validAccessToken(forceRefresh: true)
            result = try await execute(request)
        }
        guard (200..<300).contains(result.1.statusCode) else {
            if result.1.statusCode == 401 { throw EngagementError.unauthorized }
            throw EngagementError.server(statusCode: result.1.statusCode)
        }
        do {
            return try decoder.decode(EngagementSummary.self, from: result.0)
        } catch {
            throw EngagementError.invalidResponse
        }
    }

    private func execute(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var authorized = request
        let token = try await sessionManager.validAccessToken()
        authorized.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await ResilientURLSession.data(for: authorized, using: session)
        guard let http = response as? HTTPURLResponse else { throw EngagementError.invalidResponse }
        return (data, http)
    }
}
