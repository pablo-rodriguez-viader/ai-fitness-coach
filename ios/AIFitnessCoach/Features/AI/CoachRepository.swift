import Foundation

actor CoachRepository: ChatRepositoryProtocol, CoachConversationRepository {
    private let chatRepository: ChatRepository

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        chatRepository = ChatRepository(
            baseURL: baseURL,
            sessionManager: sessionManager,
            session: session)
    }

    func sendMessage(_ content: String, conversationId: UUID?) async throws -> AIChatResponse {
        try await chatRepository.sendCoachMessage(content, conversationId: conversationId)
    }

    func fetchConversations(limit: Int) async throws -> [AIConversationSummary] {
        try await chatRepository.fetchConversations(limit: limit)
    }

    func fetchConversation(id: UUID) async throws -> AIConversation {
        try await chatRepository.fetchConversation(id: id)
    }

    func deleteConversation(id: UUID) async throws {
        try await chatRepository.deleteConversation(id: id)
    }

    func coachConsent() async throws -> Bool {
        try await chatRepository.fetchCoachConsent().granted
    }

    func setCoachConsent(granted: Bool) async throws {
        try await chatRepository.setCoachConsent(granted: granted)
    }

    func createCoachConversation() async throws -> UUID {
        try await chatRepository.createCoachConversation().conversationId
    }

    func sendCoachV1Message(
        _ message: String,
        conversationId: UUID,
        clientRequestId: UUID
    ) async throws -> CoachMessageResponse {
        try await chatRepository.sendCoachV1Message(
            message,
            conversationId: conversationId,
            clientRequestId: clientRequestId)
    }

    func submitCoachFeedback(
        conversationId: UUID,
        feedback: CoachFeedbackRequest
    ) async throws {
        try await chatRepository.submitCoachFeedback(
            conversationId: conversationId,
            feedback: feedback)
    }

    func fetchCoachConversations(limit: Int) async throws -> [CoachConversationSummary] {
        try await chatRepository.fetchCoachConversationsV1(limit: limit)
    }

    func fetchCoachConversation(id: UUID) async throws -> CoachConversation {
        try await chatRepository.fetchCoachConversationV1(id: id)
    }

    func deleteCoachConversation(id: UUID) async throws {
        try await chatRepository.deleteCoachConversationV1(id: id)
    }
}
