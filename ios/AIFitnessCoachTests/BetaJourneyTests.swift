import XCTest
@testable import AIFitnessCoach

/// User journeys a beta tester hits in the first days: offline launches, reconnection,
/// failed loads that must be retryable, and signing out.
@MainActor
final class BetaJourneyTests: XCTestCase {
    func testOfflineRelaunchShowsTheLastSavedSummary() async throws {
        let cache = MemorySummaryCache()
        let saved = HealthSummary(steps: 5_400, activeEnergyKilocalories: 320, walkingDistanceKilometers: nil,
                                  averageHeartRateBeatsPerMinute: 66, latestWeightKilograms: 72)
        cache.save(saved, userId: "user-1", fetchedAt: Date(timeIntervalSinceNow: -3_600))
        let repository = ScriptedHealthRepository(summaryResults: [.failure(URLError(.notConnectedToInternet))])
        let viewModel = DashboardViewModel(repository: repository, cache: cache, userIdProvider: { "user-1" })

        viewModel.resumeHealthSync()
        try await waitUntil { viewModel.isShowingCachedData }

        XCTAssertEqual(viewModel.summary, saved)
        XCTAssertEqual(viewModel.summaryState, .available)
        XCTAssertNotNil(viewModel.lastUpdatedAt)
    }

    func testOfflineWithoutCacheShowsARetryableOfflineError() async throws {
        let repository = ScriptedHealthRepository(summaryResults: [
            .failure(URLError(.notConnectedToInternet)),
            .success(HealthSummary(steps: 800, activeEnergyKilocalories: nil, walkingDistanceKilometers: nil,
                                   averageHeartRateBeatsPerMinute: nil, latestWeightKilograms: nil))
        ])
        let viewModel = DashboardViewModel(repository: repository, cache: MemorySummaryCache(), userIdProvider: { "user-1" })

        viewModel.resumeHealthSync()
        try await waitUntil {
            if case .failed = viewModel.summaryState { return true }
            return false
        }
        XCTAssertEqual(viewModel.summaryState, .failed(URLError(.notConnectedToInternet).userMessage))

        viewModel.retryHealthConnection()
        try await waitUntil { viewModel.summaryState == .available }
        XCTAssertEqual(viewModel.summary.steps, 800)
        XCTAssertFalse(viewModel.isShowingCachedData)
    }

    // Regression: connecting Health while offline ended in an error even though the samples
    // were already queued locally.
    func testConnectingHealthOfflineIsNotAFailure() async throws {
        let repository = ScriptedHealthRepository(
            summaryResults: [.success(.empty)],
            enableError: HealthSyncError.offline)
        let viewModel = DashboardViewModel(repository: repository)

        viewModel.connectHealth()
        try await waitUntil { viewModel.summaryState == .noData }

        XCTAssertEqual(viewModel.connectionState, .connected)
    }

    func testFreshSummaryIsCachedAndSignOutClearsIt() async throws {
        let cache = MemorySummaryCache()
        let live = HealthSummary(steps: 1_000, activeEnergyKilocalories: nil, walkingDistanceKilometers: nil,
                                 averageHeartRateBeatsPerMinute: nil, latestWeightKilograms: nil)
        let repository = ScriptedHealthRepository(summaryResults: [.success(live)])
        let viewModel = DashboardViewModel(repository: repository, cache: cache, userIdProvider: { "user-1" })

        viewModel.resumeHealthSync()
        try await waitUntil { viewModel.summaryState == .available }
        XCTAssertEqual(cache.load(userId: "user-1")?.summary, live)
        XCTAssertNil(cache.load(userId: "someone-else"), "A cache is never shown to another account.")

        viewModel.resetForSignOut()
        XCTAssertNil(cache.load(userId: "user-1"))
        XCTAssertEqual(viewModel.summary, .empty)
        XCTAssertEqual(viewModel.summaryState, .notLoaded)
    }

    // Regression: when loading consent failed, "Reintentar" did nothing (no pending message).
    func testCoachRetryReloadsConsentAfterAFailedLoad() async throws {
        let repository = FlakyConsentCoachRepository()
        let viewModel = CoachViewModel(repository: repository)

        viewModel.loadConsent()
        try await waitUntil { viewModel.state == .offline }
        XCTAssertFalse(viewModel.hasLoadedConsent)

        viewModel.retry()
        try await waitUntil { viewModel.state == .consentRequired }
        XCTAssertTrue(viewModel.hasLoadedConsent)
        XCTAssertNil(viewModel.errorMessage)
    }

    func testCoachCancellationIsNotShownAsAnError() async throws {
        let repository = FlakyConsentCoachRepository(consentError: URLError(.cancelled))
        let viewModel = CoachViewModel(repository: repository)

        viewModel.loadConsent()
        try await Task.sleep(for: .milliseconds(50))

        XCTAssertNil(viewModel.errorMessage)
        XCTAssertNotEqual(viewModel.state, .failed)
    }

    func testAnalysisRetryReloadsConsentAfterAFailedLoad() async throws {
        let repository = FlakyAnalysisRepository()
        let viewModel = ActivityAnalysisViewModel(repository: repository)

        viewModel.loadConsent()
        try await waitUntil { viewModel.consentLoadFailed }
        XCTAssertNotNil(viewModel.errorMessage)

        viewModel.retry(question: "¿Cómo voy?", period: "last7Days")
        try await waitUntil { viewModel.hasLoadedConsent }
        XCTAssertTrue(viewModel.consentGranted)
        XCTAssertNil(viewModel.errorMessage)
    }

    private func waitUntil(
        timeout: Duration = .seconds(2),
        condition: @MainActor () -> Bool
    ) async throws {
        let start = ContinuousClock.now
        while !condition() {
            if ContinuousClock.now - start > timeout {
                XCTFail("Timed out waiting for the expected state.")
                return
            }
            try await Task.sleep(for: .milliseconds(10))
        }
    }
}

@MainActor
private final class MemorySummaryCache: HealthSummaryCaching {
    private var stored: CachedHealthSummary?

    func load(userId: String) -> CachedHealthSummary? {
        stored?.userId == userId ? stored : nil
    }

    func save(_ summary: HealthSummary, userId: String, fetchedAt: Date) {
        stored = CachedHealthSummary(userId: userId, summary: summary, fetchedAt: fetchedAt)
    }

    func clear() {
        stored = nil
    }
}

/// Returns the scripted summary results in order; the last one repeats.
@MainActor
private final class ScriptedHealthRepository: HealthDataRepository {
    let isAvailable = true
    private var summaryResults: [Result<HealthSummary, Error>]
    private let enableError: Error?

    init(summaryResults: [Result<HealthSummary, Error>], enableError: Error? = nil) {
        self.summaryResults = summaryResults
        self.enableError = enableError
    }

    func requestReadAuthorization() async throws {}

    func fetchTodaySummary() async throws -> HealthSummary {
        let next = summaryResults.count > 1 ? summaryResults.removeFirst() : summaryResults[0]
        return try next.get()
    }

    func enableAutomaticSync(onUpdate: @escaping @MainActor @Sendable () async -> Void) async throws {
        if let enableError { throw enableError }
    }

    func resumeAutomaticSync(onUpdate: @escaping @MainActor @Sendable () async -> Void) async throws -> Bool {
        true
    }

    func clearPendingSync() async throws {}

    func retryPendingSync() async throws {}
}

private actor FlakyConsentCoachRepository: CoachConversationRepository {
    private var consentError: Error?

    init(consentError: Error? = URLError(.notConnectedToInternet)) {
        self.consentError = consentError
    }

    func coachConsent() async throws -> Bool {
        if let consentError {
            self.consentError = nil
            throw consentError
        }
        return false
    }

    func setCoachConsent(granted: Bool) async throws {}
    func createCoachConversation() async throws -> UUID { UUID() }

    func sendCoachV1Message(_ message: String, conversationId: UUID, clientRequestId: UUID) async throws -> CoachMessageResponse {
        throw AIChatError.unavailable
    }

    func submitCoachFeedback(conversationId: UUID, feedback: CoachFeedbackRequest) async throws {}
    func fetchCoachConversations(limit: Int) async throws -> [CoachConversationSummary] { [] }

    func fetchCoachConversation(id: UUID) async throws -> CoachConversation {
        throw AIChatError.conversationNotFound
    }

    func deleteCoachConversation(id: UUID) async throws {}
}

private actor FlakyAnalysisRepository: ActivityAnalysisRepository {
    private var failNextConsent = true

    func analyzeActivity(_ question: String, conversationId: UUID?, period: String) async throws -> ActivityAnalysisResponse {
        throw AIChatError.unavailable
    }

    func fetchCoachConsent() async throws -> CoachConsent {
        if failNextConsent {
            failNextConsent = false
            throw URLError(.notConnectedToInternet)
        }
        return CoachConsent(granted: true, version: 1, grantedAt: nil)
    }

    func setCoachConsent(granted: Bool) async throws {}
}
