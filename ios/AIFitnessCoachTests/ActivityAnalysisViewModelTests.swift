import XCTest
@testable import AIFitnessCoach

@MainActor
final class ActivityAnalysisViewModelTests: XCTestCase {
    func testInitialStateAndConsentFlow() async throws {
        let repository = StubActivityAnalysisRepository(consent: false)
        let viewModel = ActivityAnalysisViewModel(repository: repository)

        XCTAssertNil(viewModel.response)
        XCTAssertFalse(viewModel.isLoading)
        XCTAssertFalse(viewModel.consentGranted)

        viewModel.loadConsent()
        try await waitUntil { viewModel.hasLoadedConsent }
        XCTAssertFalse(viewModel.consentGranted)

        viewModel.grantConsent()
        try await waitUntil { viewModel.consentGranted }
        XCTAssertTrue(viewModel.consentGranted)
    }

    func testSuccessfulAnalysisDisplaysMissingDataAndSelectedPeriod() async throws {
        let repository = StubActivityAnalysisRepository(consent: true)
        let viewModel = ActivityAnalysisViewModel(repository: repository)

        viewModel.loadConsent()
        try await waitUntil { viewModel.hasLoadedConsent && viewModel.consentGranted }
        viewModel.analyze(question: "¿Cómo van mis pasos?", period: "last30Days")
        try await waitUntil { viewModel.response != nil }

        XCTAssertEqual(viewModel.response?.analysis.missingData, ["Faltan datos de sueño"])
        let lastPeriod = await repository.lastPeriod
        XCTAssertEqual(lastPeriod, "last30Days")
        XCTAssertNil(viewModel.errorMessage)
    }

    func testCancellationStopsLoadingAndDoesNotSetAnError() async throws {
        let repository = StubActivityAnalysisRepository(consent: true, shouldDelay: true)
        let viewModel = ActivityAnalysisViewModel(repository: repository)
        viewModel.loadConsent()
        try await waitUntil { viewModel.hasLoadedConsent && viewModel.consentGranted }

        viewModel.analyze(question: "Analiza mi semana")
        try await waitUntil { viewModel.isLoading }
        viewModel.cancel()

        XCTAssertFalse(viewModel.isLoading)
        XCTAssertNil(viewModel.errorMessage)
    }

    func testQuotaErrorRemainsRecoverable() async throws {
        let repository = StubActivityAnalysisRepository(consent: true, failure: AIChatError.quotaExceeded)
        let viewModel = ActivityAnalysisViewModel(repository: repository)
        viewModel.loadConsent()
        try await waitUntil { viewModel.hasLoadedConsent && viewModel.consentGranted }

        viewModel.analyze(question: "Analiza mi semana")
        try await waitUntil { viewModel.errorMessage != nil }

        XCTAssertTrue(viewModel.errorMessage?.contains("límite diario") == true)
        XCTAssertFalse(viewModel.isLoading)
    }

    private func waitUntil(
        timeout: Duration = .seconds(2),
        condition: @MainActor () -> Bool
    ) async throws {
        let start = ContinuousClock.now
        while !condition() {
            if ContinuousClock.now - start > timeout {
                XCTFail("Timed out waiting for analysis view model state.")
                return
            }
            try await Task.sleep(for: .milliseconds(10))
        }
    }
}

private actor StubActivityAnalysisRepository: ActivityAnalysisRepository {
    private var consent: Bool
    private let shouldDelay: Bool
    private let failure: Error?
    private(set) var lastPeriod: String?

    init(consent: Bool, shouldDelay: Bool = false, failure: Error? = nil) {
        self.consent = consent
        self.shouldDelay = shouldDelay
        self.failure = failure
    }

    func fetchCoachConsent() async throws -> CoachConsent {
        CoachConsent(granted: consent, version: 1, grantedAt: nil)
    }

    func setCoachConsent(granted: Bool) async throws {
        consent = granted
    }

    func analyzeActivity(
        _ question: String,
        conversationId: UUID?,
        period: String
    ) async throws -> ActivityAnalysisResponse {
        lastPeriod = period
        if shouldDelay { try await Task.sleep(for: .seconds(10)) }
        if let failure { throw failure }
        let answer = ActivityAnalysisAnswer(
            summary: "Tus pasos se mantuvieron estables.",
            observations: [],
            limitations: "Datos agregados.",
            suggestions: [],
            missingData: ["Faltan datos de sueño"],
            safetyNotice: "No es consejo médico.")
        return ActivityAnalysisResponse(
            conversationId: conversationId ?? UUID(),
            analysis: answer,
            generatedAt: .now,
            contextPeriod: nil,
            correlationId: UUID().uuidString,
            usage: ActivityAnalysisUsage(inputTokens: 40, outputTokens: 20))
    }
}
