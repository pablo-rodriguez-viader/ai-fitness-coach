import Foundation

/// How a failed request should be presented: the same rules for every screen.
enum AppErrorKind: Equatable, Sendable {
    /// The user or the system cancelled the work; show nothing.
    case cancelled
    /// No connection or the server could not be reached.
    case offline
    /// The session is gone; the app returns to sign-in.
    case sessionExpired
    /// The service answered but is temporarily unavailable (AI disabled, 502/503).
    case unavailable
    /// Anything else.
    case failure
}

extension Error {
    var appErrorKind: AppErrorKind {
        if self is CancellationError { return .cancelled }
        if let urlError = self as? URLError {
            switch urlError.code {
            case .cancelled:
                return .cancelled
            case .notConnectedToInternet, .networkConnectionLost, .cannotConnectToHost,
                 .cannotFindHost, .dnsLookupFailed, .timedOut, .dataNotAllowed,
                 .internationalRoamingOff, .resourceUnavailable:
                return .offline
            default:
                return .failure
            }
        }
        if let syncError = self as? HealthSyncError {
            switch syncError {
            case .offline: return .offline
            case .unauthorized: return .sessionExpired
            default: return .failure
            }
        }
        if let authError = self as? AuthenticationError {
            switch authError {
            case .unauthorized, .missingTokens: return .sessionExpired
            default: return .failure
            }
        }
        if let chatError = self as? AIChatError {
            switch chatError {
            case .unauthorized: return .sessionExpired
            case .unavailable: return .unavailable
            default: return .failure
            }
        }
        if let workoutError = self as? WorkoutRepositoryError {
            switch workoutError {
            case .unauthorized: return .sessionExpired
            case .unavailable: return .unavailable
            default: return .failure
            }
        }
        if let wellnessError = self as? WellnessError, case .unauthorized = wellnessError {
            return .sessionExpired
        }
        if let engagementError = self as? EngagementError, case .unauthorized = engagementError {
            return .sessionExpired
        }
        if let accountError = self as? AccountError {
            switch accountError {
            case .unauthorized: return .sessionExpired
            case let .server(statusCode) where statusCode == 502 || statusCode == 503: return .unavailable
            default: return .failure
            }
        }
        return .failure
    }

    var isCancellation: Bool { appErrorKind == .cancelled }
    var isOffline: Bool { appErrorKind == .offline }

    /// Message for the screen. Offline and unavailable errors get one consistent wording.
    var userMessage: String {
        switch appErrorKind {
        case .cancelled:
            return ""
        case .offline:
            return "Sin conexión. Revisa tu red e inténtalo de nuevo."
        case .sessionExpired:
            return "Tu sesión ha caducado. Inicia sesión de nuevo."
        case .unavailable:
            return "El servicio no está disponible en este momento. Inténtalo más tarde."
        case .failure:
            return localizedDescription
        }
    }

    /// Server-specific text for ordinary failures, the shared wording for offline/unavailable.
    var displayMessage: String {
        appErrorKind == .failure ? localizedDescription : userMessage
    }

    /// Records the failure without any user content: only the screen and the error category.
    func track(area: String) {
        guard appErrorKind != .cancelled else { return }
        var properties = ["area": area, "kind": "\(appErrorKind)"]
        let nsError = self as NSError
        properties["domain"] = String(nsError.domain.suffix(60))
        properties["code"] = String(nsError.code)
        AppTelemetry.shared.track("error", properties)
    }
}

/// Validation problem returned by the API (`errors` dictionary of ProblemDetails).
struct APIValidationProblem: Decodable, Sendable {
    let errors: [String: [String]]?
    let title: String?

    var firstMessage: String? {
        errors?.values.first?.first ?? title
    }

    static func decode(from data: Data) -> APIValidationProblem? {
        try? JSONDecoder().decode(APIValidationProblem.self, from: data)
    }
}
