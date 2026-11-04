import Combine
import Foundation

/// The user's explicit decisions on health-data processing and usage analytics (GDPR). Loaded
/// from the server after sign-in and cached on disk so the app knows offline whether it may
/// sync or send telemetry. "Undecided" never counts as consent.
enum ConsentDecision: String, Codable, Equatable, Sendable {
    case undecided
    case granted
    case denied
}

@MainActor
final class ConsentStore: ObservableObject {
    typealias Decision = ConsentDecision

    @Published private(set) var decisions: [ConsentKind: Decision] = [:]
    @Published private(set) var hasLoaded = false
    @Published private(set) var errorMessage: String?

    private let repository: (any AccountRepositoryProtocol)?
    private let fileURL: URL?
    private let onAnalyticsChange: @MainActor (Decision) -> Void

    init(
        repository: (any AccountRepositoryProtocol)?,
        fileURL: URL? = ConsentStore.defaultFileURL(),
        onAnalyticsChange: @escaping @MainActor (Decision) -> Void = { decision in
            Task { await AppTelemetry.shared.setUploadDecision(decision) }
        }
    ) {
        self.repository = repository
        self.fileURL = fileURL
        self.onAnalyticsChange = onAnalyticsChange
        if let fileURL, let data = try? Data(contentsOf: fileURL),
           let stored = try? JSONDecoder().decode([String: Decision].self, from: data) {
            decisions = Dictionary(uniqueKeysWithValues: stored.compactMap { key, value in
                ConsentKind(rawValue: key).map { ($0, value) }
            })
        }
        onAnalyticsChange(decision(.analytics))
    }

    func decision(_ kind: ConsentKind) -> Decision {
        decisions[kind] ?? .undecided
    }

    func isGranted(_ kind: ConsentKind) -> Bool {
        decision(kind) == .granted
    }

    /// Reads the server's record (the source of truth across devices and reinstalls).
    func load() async {
        guard let repository else { return }
        do {
            let records = try await repository.consents()
            var loaded: [ConsentKind: Decision] = [:]
            for record in records {
                guard let kind = ConsentKind(rawValue: record.kind) else { continue }
                // A record for an older text version must be asked again.
                loaded[kind] = record.version < kind.textVersion ? .undecided : (record.granted ? .granted : .denied)
            }
            apply(loaded)
            hasLoaded = true
            errorMessage = nil
        } catch {
            guard !error.isCancellation else { return }
            // Offline: keep the cached decisions.
            hasLoaded = !decisions.isEmpty
        }
    }

    /// Records the decision on the server first: consent only counts once it is stored.
    @discardableResult
    func set(_ kind: ConsentKind, granted: Bool) async -> Bool {
        guard let repository else { return false }
        do {
            try await repository.setConsent(kind, granted: granted)
            var updated = decisions
            updated[kind] = granted ? .granted : .denied
            apply(updated)
            errorMessage = nil
            AppTelemetry.shared.track("consent_changed", ["kind": kind.rawValue, "granted": granted ? "true" : "false"])
            return true
        } catch {
            guard !error.isCancellation else { return false }
            errorMessage = error.displayMessage
            error.track(area: "consent_\(kind.rawValue)")
            return false
        }
    }

    /// The server already withdrew the consent (for example when its data was deleted).
    func markWithdrawn(_ kind: ConsentKind) {
        var updated = decisions
        updated[kind] = .denied
        apply(updated)
    }

    /// Decisions belong to the account: forget them when it signs out or is deleted.
    func reset() {
        apply([:])
        hasLoaded = false
        errorMessage = nil
    }

    private func apply(_ updated: [ConsentKind: Decision]) {
        let previousAnalytics = decision(.analytics)
        decisions = updated
        persist()
        if decision(.analytics) != previousAnalytics {
            onAnalyticsChange(decision(.analytics))
        }
    }

    private func persist() {
        guard let fileURL else { return }
        let stored = Dictionary(uniqueKeysWithValues: decisions.map { ($0.key.rawValue, $0.value) })
        guard let data = try? JSONEncoder().encode(stored) else { return }
        try? data.write(to: fileURL, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    nonisolated static func defaultFileURL() -> URL? {
        guard var directory = try? FileManager.default.url(
            for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        else { return nil }
        directory.append(path: "Consents", directoryHint: .isDirectory)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory.appending(path: "consents.json")
    }
}
