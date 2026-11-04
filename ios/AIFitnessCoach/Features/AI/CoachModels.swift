import Foundation

protocol CoachConversationRepository: Sendable {
    func coachConsent() async throws -> Bool
    func setCoachConsent(granted: Bool) async throws
    func createCoachConversation() async throws -> UUID
    func sendCoachV1Message(
        _ message: String,
        conversationId: UUID,
        clientRequestId: UUID
    ) async throws -> CoachMessageResponse
    func submitCoachFeedback(
        conversationId: UUID,
        feedback: CoachFeedbackRequest
    ) async throws
    func fetchCoachConversations(limit: Int) async throws -> [CoachConversationSummary]
    func fetchCoachConversation(id: UUID) async throws -> CoachConversation
    func deleteCoachConversation(id: UUID) async throws
}

struct CoachConversationSummary: Decodable, Equatable, Identifiable, Sendable {
    let id: UUID
    let createdAt: Date
    let updatedAt: Date
    let contextVersion: Int
    let lastMessagePreview: String?
}

struct CoachConversation: Decodable, Sendable {
    let id: UUID
    let createdAt: Date
    let updatedAt: Date
    let contextVersion: Int
    let messages: [CoachStoredMessage]
}

struct CoachStoredMessage: Decodable, Sendable {
    let id: UUID
    let role: String
    let content: String
    let analysis: CoachAnalysis?
    let createdAt: Date
}

struct CoachConsent: Decodable, Sendable {
    let granted: Bool
    let version: Int
    let grantedAt: Date?
}

struct CoachConversationCreated: Decodable, Sendable {
    let conversationId: UUID
    let createdAt: Date
}

struct CoachMessageResponse: Decodable, Sendable {
    let conversationId: UUID
    let messageId: UUID
    let clientRequestId: UUID
    let analysis: CoachAnalysis
    let contextPeriod: CoachContextPeriod
    let generatedAt: Date
    let usage: CoachTokenUsage
    let correlationId: String
}

struct CoachContextPeriod: Decodable, Sendable {
    let start: Date
    let end: Date
}

struct CoachTokenUsage: Decodable, Sendable {
    let inputTokens: Int
    let outputTokens: Int
}

struct CoachAnalysis: Decodable, Equatable, Sendable {
    let answer: String
    let intent: String
    let observations: [CoachObservation]
    let suggestions: [CoachSuggestion]
    let missingData: [String]
    let followUpQuestions: [String]
    let safetyNotice: String?
}

struct CoachObservation: Decodable, Equatable, Sendable, Identifiable {
    var id: String { title + description }
    let title: String
    let description: String
    let evidence: [CoachEvidence]
    let confidence: String
}

struct CoachEvidence: Decodable, Equatable, Sendable, Identifiable {
    var id: String { metric + period + value }
    let metric: String
    let value: String
    let period: String
}

struct CoachSuggestion: Decodable, Equatable, Sendable, Identifiable {
    var id: String { title + description }
    let title: String
    let description: String
    let priority: String
    let requiresConfirmation: Bool
}

struct CoachDisplayMessage: Identifiable, Equatable, Sendable {
    let id: UUID
    let role: String
    let content: String
    let analysis: CoachAnalysis?
    let createdAt: Date
}

struct CoachFeedbackRequest: Encodable, Sendable {
    let messageId: UUID
    let rating: String
    let reasonCode: String?
    let comment: String?
}
