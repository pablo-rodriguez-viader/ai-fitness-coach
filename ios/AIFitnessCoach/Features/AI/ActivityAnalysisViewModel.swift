import Combine
import Foundation

@MainActor
final class ActivityAnalysisViewModel: ObservableObject {
    @Published private(set) var response: ActivityAnalysisResponse?
    @Published private(set) var isLoading = false
    @Published private(set) var errorMessage: String?
    @Published private(set) var consentGranted = false
    @Published private(set) var hasLoadedConsent = false
    /// The last failure was loading consent, so "retry" must reload it rather than analyse.
    @Published private(set) var consentLoadFailed = false

    private let repository: any ActivityAnalysisRepository
    private var conversationId: UUID?
    private var task: Task<Void, Never>?

    init(repository: any ActivityAnalysisRepository) {
        self.repository = repository
    }

    var isCheckingConsent: Bool { !hasLoadedConsent && !consentLoadFailed }

    func loadConsent() {
        task?.cancel()
        consentLoadFailed = false
        task = Task { [weak self] in
            guard let self else { return }
            do {
                consentGranted = try await repository.fetchCoachConsent().granted
                hasLoadedConsent = true
                errorMessage = nil
            } catch {
                guard !error.isCancellation else { return }
                consentLoadFailed = true
                errorMessage = error.userMessage
                error.track(area: "analysis_consent")
            }
        }
    }

    func grantConsent() {
        task?.cancel()
        task = Task { [weak self] in
            guard let self else { return }
            do {
                try await repository.setCoachConsent(granted: true)
                consentGranted = true
                hasLoadedConsent = true
                consentLoadFailed = false
                AppTelemetry.shared.track("ai_consent_changed", ["feature": "activity_analysis", "granted": "true"])
                errorMessage = nil
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.userMessage
                error.track(area: "analysis_consent")
            }
        }
    }

    func revokeConsent() {
        task?.cancel()
        task = Task { [weak self] in
            guard let self else { return }
            do {
                try await repository.setCoachConsent(granted: false)
                consentGranted = false
                response = nil
                AppTelemetry.shared.track("ai_consent_changed", ["feature": "activity_analysis", "granted": "false"])
                conversationId = nil
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.userMessage
            }
        }
    }

    func analyze(question: String, period: String = "last7Days") {
        guard !isLoading, consentGranted else { return }
        task?.cancel()
        task = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            let startedAt = Date()
            do {
                let result = try await repository.analyzeActivity(
                    question,
                    conversationId: conversationId,
                    period: period)
                response = result
                conversationId = result.conversationId
                errorMessage = nil
                ObservabilityEvents.aiRequest(feature: "activity_analysis", outcome: "ok", startedAt: startedAt)
            } catch {
                ObservabilityEvents.aiRequest(
                    feature: "activity_analysis", outcome: ObservabilityEvents.aiOutcome(for: error),
                    startedAt: startedAt, error: error)
                guard !error.isCancellation else { return }
                errorMessage = error.appErrorKind == .failure ? error.localizedDescription : error.userMessage
                error.track(area: "analysis")
            }
        }
    }

    func retry(question: String, period: String) {
        if consentLoadFailed || !hasLoadedConsent {
            loadConsent()
        } else {
            analyze(question: question, period: period)
        }
    }

    func cancel() {
        task?.cancel()
        task = nil
        isLoading = false
    }

    func resetForSignOut() {
        task?.cancel()
        task = nil
        response = nil
        conversationId = nil
        consentGranted = false
        hasLoadedConsent = false
        consentLoadFailed = false
        isLoading = false
        errorMessage = nil
    }
}
