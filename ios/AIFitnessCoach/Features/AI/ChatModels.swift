import Foundation

struct AIMessage: Codable, Equatable, Identifiable, Sendable {
    let id: UUID
    let role: String
    let content: String
    let createdAt: Date
}

struct AIConversationSummary: Codable, Equatable, Identifiable, Sendable {
    let id: UUID
    let createdAt: Date
    let updatedAt: Date
    let contextVersion: Int
    let inputTokens: Int
    let outputTokens: Int
    let estimatedCost: Decimal
    let lastMessagePreview: String?
}

struct AIConversation: Codable, Equatable, Identifiable, Sendable {
    let id: UUID
    let createdAt: Date
    let updatedAt: Date
    let contextVersion: Int
    let inputTokens: Int
    let outputTokens: Int
    let estimatedCost: Decimal
    let messages: [AIMessage]
}

struct AIChatResponse: Decodable, Sendable {
    let conversationId: UUID
    let message: AIMessage
    let inputTokens: Int
    let outputTokens: Int
    let estimatedCost: Decimal
}

struct ActivityAnalysisResponse: Decodable, Sendable {
    let conversationId: UUID
    let analysis: ActivityAnalysisAnswer
    let generatedAt: Date
    let contextPeriod: ActivityAnalysisContextPeriod?
    let correlationId: String?
    let usage: ActivityAnalysisUsage?
}

struct ActivityAnalysisUsage: Decodable, Sendable {
    let inputTokens: Int
    let outputTokens: Int
}

struct ActivityAnalysisContextPeriod: Decodable, Sendable {
    let start: Date
    let end: Date
}

struct ActivityAnalysisAnswer: Decodable, Sendable {
    let summary: String
    let observations: [ActivityObservation]
    let limitations: String
    let suggestions: [ActivitySuggestion]?
    let missingData: [String]?
    let safetyNotice: String?
}

struct ActivityObservation: Decodable, Identifiable, Sendable {
    var id: String { "\(category)-\(trend)-\(description)" }
    let category: String
    let trend: String
    let description: String
    let title: String?
    let evidence: [String]?
    let confidence: String?
}

struct ActivitySuggestion: Decodable, Identifiable, Sendable {
    var id: String { title + description }
    let title: String
    let description: String
    let priority: String
}

enum AIChatError: Error, LocalizedError {
    case invalidResponse
    case unauthorized
    case consentRequired
    case server(statusCode: Int)
    case conversationNotFound
    case quotaExceeded
    case unavailable

    var errorDescription: String? {
        switch self {
        case .invalidResponse:
            "El servidor devolvió una respuesta de chat no válida."
        case .unauthorized:
            "La sesión ha caducado. Inicia sesión de nuevo."
        case .consentRequired:
            "Debes dar tu consentimiento para que el Coach utilice datos de salud agregados."
        case let .server(statusCode):
            "No se pudo completar la solicitud de chat (HTTP \(statusCode))."
        case .conversationNotFound:
            "No se encontró la conversación solicitada."
        case .quotaExceeded:
            "Has alcanzado el límite diario de análisis. Inténtalo de nuevo mañana."
        case .unavailable:
            "El servicio de chat no está disponible temporalmente."
        }
    }
}
