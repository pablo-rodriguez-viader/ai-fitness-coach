import XCTest
@testable import AIFitnessCoach

@MainActor
final class CoachViewModelTests: XCTestCase {
    func testRequiresConsentAndReusesClientRequestIdOnRetry() async throws {
        let repository = FakeCoachRepository(granted: false, failFirstMessage: true)
        let viewModel = CoachViewModel(repository: repository)

        viewModel.loadConsent()
        try await waitUntil { viewModel.state == .consentRequired }
        viewModel.send("¿Cómo ha ido mi semana?")
        try await Task.sleep(for: .milliseconds(20))
        XCTAssertTrue(viewModel.messages.isEmpty)

        viewModel.grantConsent()
        try await waitUntil { viewModel.consentGranted && viewModel.state == .ready }
        viewModel.send("¿Cómo ha ido mi semana?")
        try await waitUntil { viewModel.state == .failed }
        viewModel.retry()
        try await waitUntil { viewModel.messages.count == 2 && viewModel.state == .ready }

        let requestIds = await repository.requestIds
        XCTAssertEqual(requestIds.count, 2)
        XCTAssertEqual(requestIds[0], requestIds[1])
        XCTAssertEqual(viewModel.messages.last?.analysis?.answer, "Actividad registrada.")
    }

    private func waitUntil(
        timeout: Duration = .seconds(2),
        condition: @MainActor () -> Bool
    ) async throws {
        let start = ContinuousClock.now
        while !condition() {
            if ContinuousClock.now - start > timeout {
                XCTFail("Timed out waiting for view model state.")
                return
            }
            try await Task.sleep(for: .milliseconds(10))
        }
    }
}

private actor FakeCoachRepository: CoachConversationRepository {
    private var granted: Bool
    private var failFirstMessage: Bool
    private(set) var requestIds: [UUID] = []
    private let conversationId = UUID()

    init(granted: Bool, failFirstMessage: Bool) {
        self.granted = granted
        self.failFirstMessage = failFirstMessage
    }

    func coachConsent() async throws -> Bool { granted }

    func setCoachConsent(granted: Bool) async throws { self.granted = granted }

    func createCoachConversation() async throws -> UUID { conversationId }

    func sendCoachV1Message(
        _ message: String,
        conversationId: UUID,
        clientRequestId: UUID
    ) async throws -> CoachMessageResponse {
        requestIds.append(clientRequestId)
        if failFirstMessage {
            failFirstMessage = false
            throw AIChatError.unavailable
        }
        let analysis = CoachAnalysis(
            answer: "Actividad registrada.",
            intent: "WeeklyActivityAnalysis",
            observations: [],
            suggestions: [],
            missingData: [],
            followUpQuestions: [],
            safetyNotice: nil)
        return CoachMessageResponse(
            conversationId: self.conversationId,
            messageId: UUID(),
            clientRequestId: clientRequestId,
            analysis: analysis,
            contextPeriod: CoachContextPeriod(start: .now, end: .now),
            generatedAt: .now,
            usage: CoachTokenUsage(inputTokens: 20, outputTokens: 8),
            correlationId: UUID().uuidString)
    }

    func submitCoachFeedback(
        conversationId: UUID,
        feedback: CoachFeedbackRequest
    ) async throws {}

    func fetchCoachConversations(limit: Int) async throws -> [CoachConversationSummary] { [] }

    func fetchCoachConversation(id: UUID) async throws -> CoachConversation {
        CoachConversation(
            id: id,
            createdAt: .now,
            updatedAt: .now,
            contextVersion: 1,
            messages: [])
    }

    func deleteCoachConversation(id: UUID) async throws {}
}
