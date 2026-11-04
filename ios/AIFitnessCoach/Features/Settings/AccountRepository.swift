import Foundation

enum AccountError: Error, LocalizedError, Equatable {
    case invalidResponse
    case unauthorized
    case validation(String)
    case rateLimited
    case server(statusCode: Int)

    var errorDescription: String? {
        switch self {
        case .invalidResponse: "El servidor devolvió una respuesta no válida."
        case .unauthorized: "La sesión ha caducado. Inicia sesión de nuevo."
        case let .validation(message): message
        case .rateLimited: "Has hecho demasiadas solicitudes seguidas. Espera un minuto e inténtalo de nuevo."
        case let .server(statusCode): "No se pudo completar la operación (HTTP \(statusCode))."
        }
    }
}

enum SupportReportKind: String, Codable, CaseIterable, Identifiable, Sendable {
    case feedback
    case bug
    case support

    var id: String { rawValue }

    var title: String {
        switch self {
        case .feedback: "Enviar comentarios"
        case .bug: "Informar de un error"
        case .support: "Contactar con soporte"
        }
    }
}

struct SupportReportDraft: Equatable, Sendable {
    var kind: SupportReportKind
    var category: String
    var message: String
    var rating: Int?
    var contactEmail: String
    var includeDiagnostics: Bool

    static let maximumMessageLength = 4000

    var trimmedMessage: String { message.trimmingCharacters(in: .whitespacesAndNewlines) }

    var validationError: String? {
        if trimmedMessage.isEmpty { return "Escribe un mensaje." }
        if trimmedMessage.count > Self.maximumMessageLength {
            return "El mensaje no puede superar \(Self.maximumMessageLength) caracteres."
        }
        let email = contactEmail.trimmingCharacters(in: .whitespaces)
        if !email.isEmpty {
            let parts = email.split(separator: "@", omittingEmptySubsequences: false)
            if parts.count != 2 || parts[0].isEmpty || !parts[1].contains(".") {
                return "El correo de contacto no es válido."
            }
        }
        return nil
    }
}

/// Consents recorded on the server (GDPR art. 7 and 9). AI consent has its own endpoint.
enum ConsentKind: String, Codable, CaseIterable, Sendable {
    case healthData = "health_data"
    case analytics

    /// Version of the consent text shown in the app. Bump it when the text changes so users are
    /// asked again.
    var textVersion: Int { 1 }
}

struct ConsentRecord: Codable, Equatable, Sendable {
    let kind: String
    let granted: Bool
    let version: Int
}

protocol AccountRepositoryProtocol: Sendable {
    /// Downloads the JSON export to a protected temporary file and returns its URL.
    func exportData() async throws -> URL
    func deleteAccount(appleAuthorizationCode: String?) async throws
    func submit(_ draft: SupportReportDraft, diagnostics: [String: String]) async throws
    func consents() async throws -> [ConsentRecord]
    func setConsent(_ kind: ConsentKind, granted: Bool) async throws
    /// Withdraws AI consent and deletes conversations and context sent to the AI provider.
    func deleteAIData() async throws
    /// Withdraws the health-data consent and deletes every synced Apple Health sample.
    func deleteHealthData() async throws
}

actor AccountRepository: AccountRepositoryProtocol {
    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
    }

    func exportData() async throws -> URL {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/account/export"))
        request.httpMethod = "GET"
        request.timeoutInterval = 60
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        let (data, _) = try await send(request)
        guard (try? JSONSerialization.jsonObject(with: data)) != nil else { throw AccountError.invalidResponse }
        let fileURL = try Self.exportFileURL()
        try data.write(to: fileURL, options: [.atomic, .completeFileProtection])
        return fileURL
    }

    func deleteAccount(appleAuthorizationCode: String?) async throws {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/account/delete"))
        request.httpMethod = "POST"
        request.timeoutInterval = 30
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONEncoder().encode(["authorizationCode": appleAuthorizationCode])
        do {
            _ = try await send(request)
        } catch AccountError.server(statusCode: 404) {
            // Already deleted (a retry after a lost response): nothing left on the server.
            return
        }
    }

    func submit(_ draft: SupportReportDraft, diagnostics: [String: String]) async throws {
        struct Payload: Encodable {
            let kind: String
            let category: String
            let message: String
            let rating: Int?
            let contactEmail: String?
            let appVersion: String
            let buildNumber: String
            let osVersion: String
            let deviceModel: String
            let diagnostics: [String: String]?
        }
        let email = draft.contactEmail.trimmingCharacters(in: .whitespaces)
        var request = URLRequest(url: baseURL.appending(path: "api/v1/support/reports"))
        request.httpMethod = "POST"
        request.timeoutInterval = 20
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONEncoder().encode(Payload(
            kind: draft.kind.rawValue,
            category: draft.category,
            message: draft.trimmedMessage,
            rating: draft.kind == .feedback ? draft.rating : nil,
            contactEmail: email.isEmpty ? nil : email,
            appVersion: AppTelemetry.appVersion,
            buildNumber: AppTelemetry.buildNumber,
            osVersion: AppTelemetry.osVersion,
            deviceModel: AppTelemetry.deviceModel,
            diagnostics: draft.includeDiagnostics ? diagnostics : nil))
        _ = try await send(request)
    }

    func consents() async throws -> [ConsentRecord] {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/consents"))
        request.httpMethod = "GET"
        request.timeoutInterval = 15
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        let (data, _) = try await send(request)
        do {
            return try JSONDecoder().decode([ConsentRecord].self, from: data)
        } catch {
            throw AccountError.invalidResponse
        }
    }

    func setConsent(_ kind: ConsentKind, granted: Bool) async throws {
        struct Payload: Encodable { let granted: Bool; let version: Int }
        var request = URLRequest(url: baseURL.appending(path: "api/v1/consents/\(kind.rawValue)"))
        request.httpMethod = "PUT"
        request.timeoutInterval = 15
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONEncoder().encode(Payload(granted: granted, version: kind.textVersion))
        _ = try await send(request)
    }

    func deleteAIData() async throws {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/account/ai-data"))
        request.httpMethod = "DELETE"
        request.timeoutInterval = 30
        _ = try await send(request)
    }

    func deleteHealthData() async throws {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/account/health-data"))
        request.httpMethod = "DELETE"
        request.timeoutInterval = 60
        _ = try await send(request)
    }

    private func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var result = try await execute(request)
        if result.1.statusCode == 401 {
            _ = try await sessionManager.validAccessToken(forceRefresh: true)
            result = try await execute(request)
        }
        switch result.1.statusCode {
        case 200..<300:
            return result
        case 400, 422:
            let problem = try? JSONDecoder().decode(APIValidationProblem.self, from: result.0)
            throw AccountError.validation(problem?.firstMessage ?? "Revisa los datos e inténtalo de nuevo.")
        case 401:
            throw AccountError.unauthorized
        case 429:
            throw AccountError.rateLimited
        default:
            throw AccountError.server(statusCode: result.1.statusCode)
        }
    }

    private func execute(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var authorized = request
        let token = try await sessionManager.validAccessToken()
        authorized.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await ResilientURLSession.data(for: authorized, using: session)
        guard let http = response as? HTTPURLResponse else { throw AccountError.invalidResponse }
        return (data, http)
    }

    /// One export at a time, in a folder that is excluded from backups and emptied on sign-out.
    static func exportFileURL(now: Date = .now) throws -> URL {
        let directory = exportDirectory()
        try? FileManager.default.removeItem(at: directory)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyyMMdd-HHmm"
        return directory.appending(path: "aifitnesscoach-datos-\(formatter.string(from: now)).json")
    }

    nonisolated static func exportDirectory() -> URL {
        FileManager.default.temporaryDirectory.appending(path: "AccountExport", directoryHint: .isDirectory)
    }

    nonisolated static func removeExports() {
        try? FileManager.default.removeItem(at: exportDirectory())
    }
}
