import Combine
import Foundation

/// Account, privacy and support actions shown in Ajustes.
@MainActor
final class SettingsViewModel: ObservableObject {
    enum ExportState: Equatable {
        case idle
        case exporting
        case ready(URL)
        case failed(String)
    }

    enum SubmitState: Equatable {
        case idle
        case sending
        case sent
        case failed(String)
    }

    @Published private(set) var exportState: ExportState = .idle
    @Published private(set) var isDeleting = false
    @Published private(set) var deletionError: String?
    @Published private(set) var submitState: SubmitState = .idle
    @Published private(set) var isRevokingAIConsent = false
    @Published private(set) var aiConsentMessage: String?

    private let repository: any AccountRepositoryProtocol
    private let diagnostics: @MainActor () async -> [String: String]
    private let onAccountDeleted: @MainActor () async -> Void
    private let revokeAIConsent: (@MainActor () async throws -> Void)?
    let consents: ConsentStore?
    private let onHealthDataDeleted: @MainActor () async -> Void

    enum DataDeletion: Equatable {
        case health
        case ai
    }

    @Published private(set) var deletingData: DataDeletion?
    @Published private(set) var dataDeletionMessage: String?

    init(
        repository: any AccountRepositoryProtocol,
        diagnostics: @escaping @MainActor () async -> [String: String],
        onAccountDeleted: @escaping @MainActor () async -> Void,
        revokeAIConsent: (@MainActor () async throws -> Void)?,
        consents: ConsentStore? = nil,
        onHealthDataDeleted: @escaping @MainActor () async -> Void = {}
    ) {
        self.repository = repository
        self.diagnostics = diagnostics
        self.onAccountDeleted = onAccountDeleted
        self.revokeAIConsent = revokeAIConsent
        self.consents = consents
        self.onHealthDataDeleted = onHealthDataDeleted
    }

    // MARK: Erasure by category (GDPR art. 17) without closing the account

    /// Withdraws the health-data consent, deletes every synced sample on the server and stops
    /// syncing on this iPhone. Apple Health itself is not touched.
    func deleteHealthData() async {
        guard deletingData == nil else { return }
        deletingData = .health
        dataDeletionMessage = nil
        defer { deletingData = nil }
        do {
            try await BackgroundWork.run("health-data-deletion") { try await repository.deleteHealthData() }
            consents?.markWithdrawn(.healthData)
            await onHealthDataDeleted()
            AppTelemetry.shared.track("data_deleted", ["category": "health"])
            dataDeletionMessage = "Salud desconectada y datos sincronizados borrados. Los datos de Apple Health de tu iPhone no se han tocado."
        } catch {
            guard !error.isCancellation else { return }
            error.track(area: "health_data_deletion")
            dataDeletionMessage = error.displayMessage
        }
    }

    /// Withdraws AI consent and deletes conversations and the context sent to the AI provider.
    func deleteAIData() async {
        guard deletingData == nil else { return }
        deletingData = .ai
        dataDeletionMessage = nil
        defer { deletingData = nil }
        do {
            try await BackgroundWork.run("ai-data-deletion") { try await repository.deleteAIData() }
            AppTelemetry.shared.track("data_deleted", ["category": "ai"])
            dataDeletionMessage = "Conversaciones del Coach y datos enviados a la IA borrados. El Coach te pedirá consentimiento de nuevo."
        } catch {
            guard !error.isCancellation else { return }
            error.track(area: "ai_data_deletion")
            dataDeletionMessage = error.displayMessage
        }
    }

    var canRevokeAIConsent: Bool { revokeAIConsent != nil }

    // MARK: Data export

    func exportData() {
        guard exportState != .exporting else { return }
        exportState = .exporting
        AppTelemetry.shared.track("data_export_requested")
        Task { [weak self] in
            guard let self else { return }
            do {
                let url = try await repository.exportData()
                exportState = .ready(url)
            } catch {
                guard !error.isCancellation else {
                    exportState = .idle
                    return
                }
                error.track(area: "data_export")
                exportState = .failed(error.displayMessage)
            }
        }
    }

    // MARK: Account deletion

    /// `appleAuthorizationCode` comes from re-authenticating with Apple right before deleting,
    /// so the server can revoke the Sign in with Apple grant.
    func deleteAccount(appleAuthorizationCode: String?) async -> Bool {
        guard !isDeleting else { return false }
        isDeleting = true
        deletionError = nil
        defer { isDeleting = false }
        do {
            try await BackgroundWork.run("account-deletion") {
                try await repository.deleteAccount(appleAuthorizationCode: appleAuthorizationCode)
            }
            AppTelemetry.shared.track("account_deleted", [
                "apple_code": appleAuthorizationCode == nil ? "missing" : "present"
            ])
            await AppTelemetry.shared.flush()
            AccountRepository.removeExports()
            await onAccountDeleted()
            return true
        } catch {
            guard !error.isCancellation else { return false }
            error.track(area: "account_deletion")
            deletionError = error.displayMessage
            return false
        }
    }

    func reportDeletionProblem(_ message: String) {
        deletionError = message
    }

    // MARK: AI consent

    func withdrawAIConsent() {
        guard let revokeAIConsent, !isRevokingAIConsent else { return }
        isRevokingAIConsent = true
        aiConsentMessage = nil
        Task { [weak self] in
            guard let self else { return }
            defer { isRevokingAIConsent = false }
            do {
                try await revokeAIConsent()
                AppTelemetry.shared.track("ai_consent_changed", ["feature": "settings", "granted": "false"])
                aiConsentMessage = "Consentimiento retirado. El Coach y el Análisis te lo volverán a pedir antes de usar tus datos."
            } catch {
                guard !error.isCancellation else { return }
                error.track(area: "ai_consent_settings")
                aiConsentMessage = error.displayMessage
            }
        }
    }

    // MARK: Feedback Center / bug reports / support

    func submit(_ draft: SupportReportDraft) async -> Bool {
        if let validationError = draft.validationError {
            submitState = .failed(validationError)
            return false
        }
        guard submitState != .sending else { return false }
        submitState = .sending
        do {
            var values: [String: String] = [:]
            if draft.includeDiagnostics {
                values = await diagnostics()
            }
            let report = values
            try await BackgroundWork.run("support-report") {
                try await repository.submit(draft, diagnostics: report)
            }
            AppTelemetry.shared.track("support_report_sent", [
                "kind": draft.kind.rawValue,
                "category": draft.category,
                "diagnostics": draft.includeDiagnostics ? "true" : "false"
            ])
            submitState = .sent
            return true
        } catch {
            guard !error.isCancellation else {
                submitState = .idle
                return false
            }
            error.track(area: "support_report")
            submitState = .failed(error.displayMessage)
            return false
        }
    }

    func resetSubmitState() {
        submitState = .idle
    }
}

/// Technical context attached to a bug report when the tester agrees. No health values, no
/// message content: build, device, connectivity, sync status, flags and recent event names.
enum DiagnosticsSnapshot {
    @MainActor
    static func collect(
        isOnline: Bool,
        healthConnection: String,
        syncOverview: HealthSyncOverview,
        config: AppConfig,
        now: Date = .now
    ) async -> [String: String] {
        var values: [String: String] = [
            "environment": AppEnvironment.current.rawValue,
            "app_version": AppTelemetry.appVersion,
            "build": AppTelemetry.buildNumber,
            "os": AppTelemetry.osVersion,
            "device": AppTelemetry.deviceModel,
            "locale": Locale.current.identifier,
            "network": isOnline ? "online" : "offline",
            "health_connection": healthConnection,
            "sync_state": String(describing: syncOverview.state).prefix(80).description,
            "sync_pending": String(syncOverview.pendingCount)
        ]
        if let lastSync = syncOverview.lastSuccessAt {
            values["sync_last_success_minutes_ago"] = String(Int(now.timeIntervalSince(lastSync) / 60))
        }
        let disabled = FeatureFlag.allCases.filter { !config.isEnabled($0) }.map(\.rawValue)
        values["flags_disabled"] = disabled.isEmpty ? "none" : disabled.joined(separator: ",")
        let recent = await AppTelemetry.shared.pendingEvents().suffix(20).map(\.name)
        if !recent.isEmpty {
            values["recent_events"] = String(recent.joined(separator: ",").prefix(300))
        }
        return values
    }
}
