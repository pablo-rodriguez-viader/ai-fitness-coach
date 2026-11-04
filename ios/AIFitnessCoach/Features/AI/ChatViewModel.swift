import Combine
import Foundation

@MainActor
final class ChatViewModel: ObservableObject {
    @Published private(set) var conversationId: UUID?
    @Published private(set) var messages: [AIMessage] = []
    @Published private(set) var conversations: [AIConversationSummary] = []
    @Published private(set) var isLoading = false
    @Published private(set) var isSending = false
    @Published private(set) var errorMessage: String?

    private let service: AIChatService
    private var loadTask: Task<Void, Never>?
    private var sendTask: Task<Void, Never>?

    init(service: AIChatService) {
        self.service = service
    }

    func loadConversations() {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            do {
                conversations = try await service.conversations()
                errorMessage = nil
            } catch is CancellationError {
                return
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    func openConversation(id: UUID) {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            do {
                let conversation = try await service.conversation(id: id, refresh: true)
                conversationId = conversation.id
                messages = conversation.messages
                errorMessage = nil
            } catch is CancellationError {
                return
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    func send(_ content: String) {
        guard !isSending,
              !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        else { return }
        sendTask?.cancel()
        sendTask = Task { [weak self] in
            guard let self else { return }
            isSending = true
            defer { isSending = false }
            do {
                let response = try await service.sendMessage(content, conversationId: conversationId)
                let userMessage = AIMessage(
                    id: UUID(),
                    role: "user",
                    content: content,
                    createdAt: .now)
                conversationId = response.conversationId
                messages.append(userMessage)
                messages.append(response.message)
                errorMessage = nil
                loadConversations()
            } catch is CancellationError {
                return
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    func deleteConversation(id: UUID) {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            do {
                try await service.deleteConversation(id: id)
                conversations.removeAll { $0.id == id }
                if conversationId == id {
                    conversationId = nil
                    messages = []
                }
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    func startNewConversation() {
        loadTask?.cancel()
        conversationId = nil
        messages = []
        errorMessage = nil
    }

    func cancelTasks() {
        loadTask?.cancel()
        sendTask?.cancel()
        loadTask = nil
        sendTask = nil
    }

    func resetForSignOut() {
        cancelTasks()
        conversationId = nil
        messages = []
        conversations = []
        isLoading = false
        isSending = false
        errorMessage = nil
    }
}
