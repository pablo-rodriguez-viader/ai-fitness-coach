import Foundation

actor ConversationStore {
    private var conversations: [UUID: AIConversation] = [:]
    private var summaries: [AIConversationSummary] = []

    func conversation(id: UUID) -> AIConversation? {
        conversations[id]
    }

    func allSummaries() -> [AIConversationSummary] {
        summaries
    }

    func update(_ conversation: AIConversation) {
        conversations[conversation.id] = conversation
    }

    func updateSummaries(_ summaries: [AIConversationSummary]) {
        self.summaries = summaries
    }

    func remove(id: UUID) {
        conversations.removeValue(forKey: id)
        summaries.removeAll { $0.id == id }
    }

    func clear() {
        conversations.removeAll()
        summaries.removeAll()
    }
}
