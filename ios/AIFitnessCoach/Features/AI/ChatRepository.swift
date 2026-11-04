import Foundation

protocol ChatRepositoryProtocol: Sendable {
    func sendMessage(_ content: String, conversationId: UUID?) async throws -> AIChatResponse
    func fetchConversations(limit: Int) async throws -> [AIConversationSummary]
    func fetchConversation(id: UUID) async throws -> AIConversation
    func deleteConversation(id: UUID) async throws
}

protocol ActivityAnalysisRepository: Sendable {
    func analyzeActivity(_ question: String, conversationId: UUID?, period: String) async throws -> ActivityAnalysisResponse
    func fetchCoachConsent() async throws -> CoachConsent
    func setCoachConsent(granted: Bool) async throws
}

private struct CoachMessageBody: Encodable {
    let message: String
    let clientRequestId: UUID
    let period: String
}

actor ChatRepository: ChatRepositoryProtocol, ActivityAnalysisRepository {
    private struct ChatRequest: Encodable {
        let conversationId: UUID?
        let content: String
    }

    private struct CoachRequest: Encodable {
        let conversationId: UUID?
        let question: String
    }

    private struct ActivityAnalysisRequest: Encodable {
        let conversationId: UUID?
        let question: String
        let period: String
    }

    private struct ConsentRequest: Encodable {
        let granted: Bool
    }

    private struct EmptyRequest: Encodable {}

    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
        encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            try container.encode(formatter.string(from: date))
        }
        decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let value = try container.decode(String.self)
            let fractional = ISO8601DateFormatter()
            fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            let standard = ISO8601DateFormatter()
            guard let date = fractional.date(from: value) ?? standard.date(from: value) else {
                throw DecodingError.dataCorruptedError(
                    in: container,
                    debugDescription: "Invalid ISO-8601 timestamp.")
            }
            return date
        }
    }

    func sendMessage(_ content: String, conversationId: UUID?) async throws -> AIChatResponse {
        try await sendMessage(content, conversationId: conversationId, endpoint: "chat", questionKey: "content")
    }

    func sendCoachMessage(_ question: String, conversationId: UUID?) async throws -> AIChatResponse {
        try await sendMessage(question, conversationId: conversationId, endpoint: "coach", questionKey: "question")
    }

    func analyzeActivity(
        _ question: String,
        conversationId: UUID?,
        period: String = "last7Days"
    ) async throws -> ActivityAnalysisResponse {
        guard !question.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              question.count <= 1_000
        else {
            throw AIChatError.server(statusCode: 400)
        }
        let body = try encoder.encode(ActivityAnalysisRequest(
            conversationId: conversationId,
            question: question,
            period: period))
        let request = try makeRequest(
            path: "api/v1/ai/activity-analysis",
            method: "POST",
            body: body)
        return try await send(request, as: ActivityAnalysisResponse.self)
    }

    func fetchCoachConsent() async throws -> CoachConsent {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/coach/consent"))
        request.timeoutInterval = 20
        request.httpMethod = "GET"
        return try await send(request, as: CoachConsent.self)
    }

    func setCoachConsent(granted: Bool) async throws {
        let body = try encoder.encode(ConsentRequest(granted: granted))
        let request = try makeRequest(
            path: "api/v1/coach/consent",
            method: "PUT",
            body: body)
        _ = try await perform(request)
    }

    func createCoachConversation() async throws -> CoachConversationCreated {
        let body = try encoder.encode(EmptyRequest())
        let request = try makeRequest(
            path: "api/v1/coach/conversations",
            method: "POST",
            body: body)
        return try await send(request, as: CoachConversationCreated.self)
    }

    func sendCoachV1Message(
        _ message: String,
        conversationId: UUID,
        clientRequestId: UUID
    ) async throws -> CoachMessageResponse {
        guard !message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              message.count <= 1_000
        else {
            throw AIChatError.server(statusCode: 400)
        }
        let body = try encoder.encode(CoachMessageBody(
            message: message,
            clientRequestId: clientRequestId,
            period: "auto"))
        let request = try makeRequest(
            path: "api/v1/coach/conversations/\(conversationId.uuidString)/messages",
            method: "POST",
            body: body)
        return try await send(request, as: CoachMessageResponse.self)
    }

    func submitCoachFeedback(
        conversationId: UUID,
        feedback: CoachFeedbackRequest
    ) async throws {
        let body = try encoder.encode(feedback)
        let request = try makeRequest(
            path: "api/v1/coach/conversations/\(conversationId.uuidString)/feedback",
            method: "POST",
            body: body)
        _ = try await perform(request)
    }

    func fetchSuggestedCoachQuestions() async throws -> [String] {
        var request = URLRequest(url: baseURL.appending(path: "api/v1/coach/suggested-questions"))
        request.timeoutInterval = 20
        request.httpMethod = "GET"
        return try await send(request, as: [String].self)
    }

    func fetchCoachConversationsV1(limit: Int = 50) async throws -> [CoachConversationSummary] {
        var components = URLComponents(
            url: baseURL.appending(path: "api/v1/coach/conversations"),
            resolvingAgainstBaseURL: false)
        components?.queryItems = [URLQueryItem(name: "limit", value: String(min(max(limit, 1), 100)))]
        guard let url = components?.url else { throw AIChatError.invalidResponse }
        var request = URLRequest(url: url)
        request.timeoutInterval = 20
        request.httpMethod = "GET"
        return try await send(request, as: [CoachConversationSummary].self)
    }

    func fetchCoachConversationV1(id: UUID) async throws -> CoachConversation {
        var request = URLRequest(
            url: baseURL.appending(path: "api/v1/coach/conversations/\(id.uuidString)"))
        request.httpMethod = "GET"
        return try await send(request, as: CoachConversation.self)
    }

    func deleteCoachConversationV1(id: UUID) async throws {
        var request = URLRequest(
            url: baseURL.appending(path: "api/v1/coach/conversations/\(id.uuidString)"))
        request.httpMethod = "DELETE"
        _ = try await perform(request)
    }

    private func sendMessage(
        _ content: String,
        conversationId: UUID?,
        endpoint: String,
        questionKey: String
    ) async throws -> AIChatResponse {
        guard !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              content.count <= 4_000
        else {
            throw AIChatError.server(statusCode: 400)
        }
        let body: Data
        if questionKey == "question" {
            body = try encoder.encode(CoachRequest(conversationId: conversationId, question: content))
        } else {
            body = try encoder.encode(ChatRequest(conversationId: conversationId, content: content))
        }
        let request = try makeRequest(path: "api/v1/ai/\(endpoint)", method: "POST", body: body)
        return try await send(request, as: AIChatResponse.self)
    }

    func fetchConversations(limit: Int = 50) async throws -> [AIConversationSummary] {
        var components = URLComponents(
            url: baseURL.appending(path: "api/v1/ai/conversations"),
            resolvingAgainstBaseURL: false)
        components?.queryItems = [URLQueryItem(name: "limit", value: String(min(max(limit, 1), 100)))]
        guard let url = components?.url else { throw AIChatError.invalidResponse }
        var request = URLRequest(url: url)
        request.timeoutInterval = 20
        request.httpMethod = "GET"
        return try await send(request, as: [AIConversationSummary].self)
    }

    func fetchConversation(id: UUID) async throws -> AIConversation {
        var request = URLRequest(
            url: baseURL.appending(path: "api/v1/ai/conversations/\(id.uuidString)"))
        request.httpMethod = "GET"
        return try await send(request, as: AIConversation.self)
    }

    func deleteConversation(id: UUID) async throws {
        var request = URLRequest(
            url: baseURL.appending(path: "api/v1/ai/conversations/\(id.uuidString)"))
        request.httpMethod = "DELETE"
        _ = try await perform(request)
    }

    private func makeRequest(path: String, method: String, body: Data) throws -> URLRequest {
        var request = URLRequest(url: baseURL.appending(path: path))
        request.timeoutInterval = 20
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        return request
    }

    private func send<Response: Decodable>(
        _ request: URLRequest,
        as type: Response.Type
    ) async throws -> Response {
        let (data, _) = try await perform(request)
        do {
            return try decoder.decode(type, from: data)
        } catch {
            throw AIChatError.invalidResponse
        }
    }

    private func perform(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var authorizedRequest = request
        let token = try await sessionManager.validAccessToken()
        authorizedRequest.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")

        var result = try await execute(authorizedRequest)
        if result.1.statusCode == 401 {
            let token = try await sessionManager.validAccessToken(forceRefresh: true)
            authorizedRequest.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            result = try await execute(authorizedRequest)
        }

        guard (200..<300).contains(result.1.statusCode) else {
            if result.1.statusCode == 401 { throw AIChatError.unauthorized }
            if result.1.statusCode == 403 { throw AIChatError.consentRequired }
            if result.1.statusCode == 404 { throw AIChatError.conversationNotFound }
            if result.1.statusCode == 429 { throw AIChatError.quotaExceeded }
            if result.1.statusCode == 502 || result.1.statusCode == 503 {
                throw AIChatError.unavailable
            }
            throw AIChatError.server(statusCode: result.1.statusCode)
        }
        return result
    }

    private func execute(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let (data, response) = try await ResilientURLSession.data(for: request, using: session)
        guard let response = response as? HTTPURLResponse else {
            throw AIChatError.invalidResponse
        }
        return (data, response)
    }
}
