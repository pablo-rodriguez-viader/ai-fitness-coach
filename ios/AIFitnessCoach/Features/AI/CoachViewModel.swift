import Combine
import Foundation

enum CoachViewState: Equatable {
    case idle
    case loadingConversation
    case ready
    case sending
    case rateLimited
    case consentRequired
    case offline
    case failed
    case deleting
}

@MainActor
final class CoachViewModel: ObservableObject {
    @Published private(set) var state: CoachViewState = .idle
    @Published private(set) var consentGranted = false
    @Published private(set) var conversationId: UUID?
    @Published private(set) var messages: [CoachDisplayMessage] = []
    @Published private(set) var conversations: [CoachConversationSummary] = []
    @Published private(set) var errorMessage: String?
    @Published private(set) var isCheckingConsent = false
    @Published private(set) var hasLoadedConsent = false

    private struct PendingMessage {
        let content: String
        let clientRequestId: UUID
    }

    private let repository: any CoachConversationRepository
    private var pendingMessage: PendingMessage?
    private var loadTask: Task<Void, Never>?
    private var sendTask: Task<Void, Never>?

    var isSending: Bool { state == .sending }
    var isLoading: Bool { state == .loadingConversation }
    /// The question being sent, so it stays visible until the answer arrives.
    var pendingQuestion: String? { isSending ? pendingMessage?.content : nil }

    init(repository: any CoachConversationRepository) {
        self.repository = repository
    }

    func loadConsent() {
        loadTask?.cancel()
        isCheckingConsent = true
        loadTask = Task { [weak self] in
            guard let self else { return }
            defer { isCheckingConsent = false }
            do {
                consentGranted = try await repository.coachConsent()
                hasLoadedConsent = true
                state = consentGranted ? .ready : .consentRequired
                errorMessage = nil
            } catch {
                setError(error)
            }
        }
    }

    func grantConsent() {
        guard !isSending else { return }
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            do {
                try await repository.setCoachConsent(granted: true)
                AppTelemetry.shared.track("ai_consent_changed", ["feature": "coach", "granted": "true"])
                consentGranted = true
                hasLoadedConsent = true
                state = .ready
                errorMessage = nil
            } catch {
                setError(error)
            }
        }
    }

    func revokeConsent() {
        guard !isSending else { return }
        sendTask?.cancel()
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            do {
                try await repository.setCoachConsent(granted: false)
                AppTelemetry.shared.track("ai_consent_changed", ["feature": "coach", "granted": "false"])
                consentGranted = false
                pendingMessage = nil
                state = .consentRequired
                errorMessage = nil
            } catch {
                setError(error)
            }
        }
    }

    func loadConversations() {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            state = .loadingConversation
            defer { if state == .loadingConversation { state = .ready } }
            do {
                conversations = try await repository.fetchCoachConversations(limit: 50)
                errorMessage = nil
            } catch is CancellationError {
                return
            } catch {
                setError(error)
            }
        }
    }

    func openConversation(id: UUID) {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            state = .loadingConversation
            defer { if state == .loadingConversation { state = .ready } }
            do {
                let conversation = try await repository.fetchCoachConversation(id: id)
                conversationId = conversation.id
                messages = conversation.messages.map(displayMessage)
                pendingMessage = nil
                errorMessage = nil
            } catch is CancellationError {
                return
            } catch {
                setError(error)
            }
        }
    }

    func send(_ content: String) {
        let trimmed = content.trimmingCharacters(in: .whitespacesAndNewlines)
        guard consentGranted, !isSending, !trimmed.isEmpty, trimmed.count <= 1_000 else { return }
        if pendingMessage?.content != trimmed {
            pendingMessage = PendingMessage(content: trimmed, clientRequestId: UUID())
        }
        sendPendingMessage()
    }

    func retry() {
        guard !isSending else { return }
        guard pendingMessage != nil, consentGranted else {
            // Nothing to resend: the failure was loading consent or the history.
            if !hasLoadedConsent || !consentGranted {
                loadConsent()
            } else {
                loadConversations()
            }
            return
        }
        sendPendingMessage()
    }

    private func sendPendingMessage() {
        guard let pendingMessage else { return }
        sendTask?.cancel()
        sendTask = Task { [weak self] in
            guard let self else { return }
            state = .sending
            defer { if state == .sending { state = .ready } }
            let startedAt = Date()
            do {
                let id: UUID
                if let conversationId {
                    id = conversationId
                } else {
                    id = try await repository.createCoachConversation()
                    conversationId = id
                }
                let response = try await repository.sendCoachV1Message(
                    pendingMessage.content,
                    conversationId: id,
                    clientRequestId: pendingMessage.clientRequestId)
                messages.append(CoachDisplayMessage(
                    id: UUID(),
                    role: "user",
                    content: pendingMessage.content,
                    analysis: nil,
                    createdAt: .now))
                messages.append(CoachDisplayMessage(
                    id: response.messageId,
                    role: "assistant",
                    content: response.analysis.answer,
                    analysis: response.analysis,
                    createdAt: response.generatedAt))
                self.pendingMessage = nil
                errorMessage = nil
                state = .ready
                AppTelemetry.shared.track("coach_message_sent", ["intent": response.analysis.intent])
                ObservabilityEvents.aiRequest(feature: "coach", outcome: "ok", startedAt: startedAt)
                loadConversations()
            } catch {
                ObservabilityEvents.aiRequest(
                    feature: "coach", outcome: ObservabilityEvents.aiOutcome(for: error),
                    startedAt: startedAt, error: error)
                setError(error)
            }
        }
    }

    func submitFeedback(messageId: UUID, helpful: Bool) {
        guard let conversationId else { return }
        let rating = helpful ? "Helpful" : "NotHelpful"
        Task { [weak self] in
            guard let self else { return }
            do {
                try await repository.submitCoachFeedback(
                    conversationId: conversationId,
                    feedback: CoachFeedbackRequest(
                        messageId: messageId,
                        rating: rating,
                        reasonCode: nil,
                        comment: nil))
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    func deleteConversation(id: UUID) {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            state = .deleting
            defer { if state == .deleting { state = .ready } }
            do {
                try await repository.deleteCoachConversation(id: id)
                conversations.removeAll { $0.id == id }
                if conversationId == id {
                    conversationId = nil
                    messages = []
                    pendingMessage = nil
                }
                errorMessage = nil
            } catch {
                setError(error)
            }
        }
    }

    func startNewConversation() {
        sendTask?.cancel()
        conversationId = nil
        messages = []
        pendingMessage = nil
        errorMessage = nil
        state = consentGranted ? .ready : .consentRequired
    }

    func cancelTasks() {
        loadTask?.cancel()
        sendTask?.cancel()
        loadTask = nil
        sendTask = nil
        if state == .sending || state == .loadingConversation {
            state = .ready
        }
    }

    func resetForSignOut() {
        cancelTasks()
        conversationId = nil
        messages = []
        conversations = []
        consentGranted = false
        hasLoadedConsent = false
        isCheckingConsent = false
        pendingMessage = nil
        errorMessage = nil
        state = .idle
    }

    private func displayMessage(_ message: CoachStoredMessage) -> CoachDisplayMessage {
        return CoachDisplayMessage(
            id: message.id,
            role: message.role,
            content: message.analysis?.answer ?? message.content,
            analysis: message.analysis,
            createdAt: message.createdAt)
    }

    private func setError(_ error: Error) {
        // Cancelling (closing the sheet, tapping Cancel) is not a failure to show.
        if error.isCancellation {
            if state == .sending || state == .loadingConversation || state == .deleting {
                state = consentGranted ? .ready : (hasLoadedConsent ? .consentRequired : .idle)
            }
            return
        }
        if let chatError = error as? AIChatError {
            switch chatError {
            case .quotaExceeded:
                state = .rateLimited
            case .consentRequired:
                consentGranted = false
                hasLoadedConsent = true
                state = .consentRequired
            default:
                state = .failed
            }
            errorMessage = error.localizedDescription
        } else if error.isOffline {
            state = .offline
            errorMessage = error.userMessage
        } else {
            state = .failed
            errorMessage = error.userMessage
        }
        error.track(area: "coach")
    }
}
