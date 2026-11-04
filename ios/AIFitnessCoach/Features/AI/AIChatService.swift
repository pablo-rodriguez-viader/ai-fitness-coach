import Foundation

actor AIChatService {
    private let repository: any ChatRepositoryProtocol
    private let store: ConversationStore

    init(repository: any ChatRepositoryProtocol, store: ConversationStore) {
        self.repository = repository
        self.store = store
    }

    func sendMessage(_ content: String, conversationId: UUID?) async throws -> AIChatResponse {
        try await repository.sendMessage(content, conversationId: conversationId)
    }

    func conversations(limit: Int = 50) async throws -> [AIConversationSummary] {
        let items = try await repository.fetchConversations(limit: limit)
        await store.updateSummaries(items)
        return items
    }

    func conversation(id: UUID, refresh: Bool = false) async throws -> AIConversation {
        if !refresh, let cached = await store.conversation(id: id) {
            return cached
        }
        let conversation = try await repository.fetchConversation(id: id)
        await store.update(conversation)
        return conversation
    }

    func deleteConversation(id: UUID) async throws {
        try await repository.deleteConversation(id: id)
        await store.remove(id: id)
    }
}
