import Combine
import Foundation

/// Kill switches for existing screens, served by `GET /api/v1/app-config`. A flag the app has not
/// received yet (first launch offline, or a new flag) counts as on, so a config outage never hides
/// the product from testers. Account deletion is never behind a flag (App Store requirement).
enum FeatureFlag: String, CaseIterable, Sendable {
    case coach
    case activityAnalysis = "activity_analysis"
    case workouts
    case nutrition
    case goals
    case progress
    case engagement
    case feedbackCenter = "feedback_center"
    case dataExport = "data_export"
    case betaFeedback = "beta_feedback"
}

struct AppConfig: Codable, Equatable, Sendable {
    var environment: String
    var minimumSupportedBuild: Int
    var supportEmail: String?
    var privacyPolicyUrl: URL?
    var termsUrl: URL?
    var features: [String: Bool]

    static let defaults = AppConfig(
        environment: AppEnvironment.current.rawValue,
        minimumSupportedBuild: 0,
        supportEmail: nil,
        privacyPolicyUrl: nil,
        termsUrl: nil,
        features: [:])

    func isEnabled(_ flag: FeatureFlag) -> Bool {
        features[flag.rawValue] ?? true
    }
}

/// Build flavour, from `APP_ENVIRONMENT` in the build configuration (Debug, Beta, Release).
enum AppEnvironment: String, Sendable {
    case development
    case beta
    /// Free personal install (Config/Personal.xcconfig): access-code login, no Sign in with Apple.
    case personal
    case production

    static var current: AppEnvironment {
        let value = Bundle.main.object(forInfoDictionaryKey: "AppEnvironment") as? String
        return value.flatMap(AppEnvironment.init(rawValue:)) ?? .development
    }

    var displayName: String {
        switch self {
        case .development: "Desarrollo"
        case .beta: "Beta privada (TestFlight)"
        case .personal: "Instalación personal"
        case .production: "App Store"
        }
    }
}

protocol AppConfigFetching: Sendable {
    func fetch() async throws -> AppConfig
}

struct AppConfigAPIClient: AppConfigFetching {
    let baseURL: URL
    var session: URLSession = .shared

    func fetch() async throws -> AppConfig {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/app-config"))
        request.timeoutInterval = 10
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw URLError(.badServerResponse)
        }
        return try JSONDecoder().decode(AppConfig.self, from: data)
    }
}

/// Last config received, kept on disk so flags survive offline launches.
@MainActor
final class AppConfigStore: ObservableObject {
    @Published private(set) var config: AppConfig
    private let fetcher: (any AppConfigFetching)?
    private let fileURL: URL?
    private let buildNumber: Int
    private var lastFetchAt: Date?
    private var fetchTask: Task<Void, Never>?
    static let minimumRefreshInterval: TimeInterval = 5 * 60

    init(
        fetcher: (any AppConfigFetching)?,
        fileURL: URL? = AppConfigStore.defaultFileURL(),
        buildNumber: Int = Int(AppTelemetry.buildNumber) ?? 0
    ) {
        self.fetcher = fetcher
        self.fileURL = fileURL
        self.buildNumber = buildNumber
        config = Self.load(from: fileURL) ?? .defaults
    }

    func isEnabled(_ flag: FeatureFlag) -> Bool {
        config.isEnabled(flag)
    }

    /// True when this build is older than the minimum the server still supports.
    var requiresUpdate: Bool {
        config.minimumSupportedBuild > 0 && buildNumber > 0 && buildNumber < config.minimumSupportedBuild
    }

    func refresh(force: Bool = false, now: Date = .now) {
        guard let fetcher, fetchTask == nil else { return }
        if !force, let lastFetchAt, now.timeIntervalSince(lastFetchAt) < Self.minimumRefreshInterval { return }
        fetchTask = Task { [weak self] in
            defer { self?.fetchTask = nil }
            do {
                let fetched = try await fetcher.fetch()
                guard let self else { return }
                lastFetchAt = now
                if fetched != config {
                    config = fetched
                    persist(fetched)
                }
                let disabled = FeatureFlag.allCases.filter { !fetched.isEnabled($0) }.map(\.rawValue)
                AppTelemetry.shared.track("app_config_loaded", [
                    "disabled": disabled.isEmpty ? "none" : String(disabled.joined(separator: ",").prefix(110))
                ])
            } catch {
                guard !error.isCancellation else { return }
                AppTelemetry.shared.track("app_config_failed", ["kind": "\(error.appErrorKind)"])
            }
        }
    }

    /// Waits for an in-flight refresh; used by tests.
    func waitForRefresh() async {
        await fetchTask?.value
    }

    private func persist(_ config: AppConfig) {
        guard let fileURL, let data = try? JSONEncoder().encode(config) else { return }
        try? data.write(to: fileURL, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    private static func load(from fileURL: URL?) -> AppConfig? {
        guard let fileURL, let data = try? Data(contentsOf: fileURL) else { return nil }
        return try? JSONDecoder().decode(AppConfig.self, from: data)
    }

    nonisolated static func defaultFileURL() -> URL? {
        guard var directory = try? FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true)
        else { return nil }
        directory.append(path: "AppConfig", directoryHint: .isDirectory)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory.appending(path: "app-config.json")
    }
}
