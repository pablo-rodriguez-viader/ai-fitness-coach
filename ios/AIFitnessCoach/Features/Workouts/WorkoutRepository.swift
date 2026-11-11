import Foundation

actor WorkoutRepository {
    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession
    private let encoder = JSONEncoder()
    private let decoder: JSONDecoder

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let value = try container.decode(String.self)
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            let standard = ISO8601DateFormatter()
            let dateFormatter = DateFormatter()
            dateFormatter.calendar = Calendar(identifier: .iso8601)
            dateFormatter.timeZone = TimeZone(secondsFromGMT: 0)
            dateFormatter.dateFormat = "yyyy-MM-dd"
            guard let date = formatter.date(from: value)
                ?? standard.date(from: value)
                ?? dateFormatter.date(from: value)
            else {
                throw DecodingError.dataCorruptedError(
                    in: container,
                    debugDescription: "Invalid ISO-8601 timestamp.")
            }
            return date
        }
        self.decoder = decoder
    }

    func generate(
        goal: WorkoutGoal,
        days: Int,
        availableMinutes: Int = 45,
        equipment: [String] = ["Bodyweight"],
        needs: String? = nil
    ) async throws -> WorkoutPlan {
        try await send(
            path: "api/v1/workouts/generate",
            method: "POST",
            body: encoder.encode(WorkoutPlanRequest(
                goal: goal.rawValue,
                days: days,
                availableMinutes: availableMinutes,
                equipment: equipment,
                needs: needs)),
            as: WorkoutPlan.self)
    }

    func latest() async throws -> WorkoutPlan? {
        do {
            return try await send(path: "api/v1/workouts/current", method: "GET", as: WorkoutPlan.self)
        } catch WorkoutRepositoryError.server(statusCode: 404) {
            return nil
        }
    }

    func history() async throws -> [WorkoutHistory] {
        try await send(path: "api/v1/workouts/history", method: "GET", as: [WorkoutHistory].self)
    }

    func recommendations() async throws -> [WorkoutRecommendation] {
        try await send(path: "api/v1/coach/recommendations", method: "GET", as: [WorkoutRecommendation].self)
    }

    func submit(_ feedback: WorkoutFeedbackRequest) async throws -> WorkoutFeedbackResponse {
        try await send(
            path: "api/v1/workouts/feedback",
            method: "POST",
            body: encoder.encode(feedback),
            as: WorkoutFeedbackResponse.self)
    }

    func complete(dayId: UUID) async throws -> WorkoutCompletionResponse {
        struct CompletionRequest: Encodable { let workoutDayId: UUID; let completed: Bool }
        return try await send(
            path: "api/v1/workouts/complete",
            method: "POST",
            body: encoder.encode(CompletionRequest(workoutDayId: dayId, completed: true)),
            as: WorkoutCompletionResponse.self)
    }

    func coachInsights() async throws -> CoachInsightsResponse {
        try await send(path: "api/v1/coach/insights", method: "GET", as: CoachInsightsResponse.self)
    }

    func progressAnalysis() async throws -> ProgressAnalysisResponse {
        try await send(path: "api/v1/coach/progress-analysis", method: "GET", as: ProgressAnalysisResponse.self)
    }

    func adherence() async throws -> TrainingAdherenceSummary {
        try await send(path: "api/v1/coach/adherence", method: "GET", as: TrainingAdherenceSummary.self)
    }

    func recommendationHistory() async throws -> [CoachRecommendationRecord] {
        try await send(path: "api/v1/coach/recommendation-history", method: "GET", as: [CoachRecommendationRecord].self)
    }

    func rateRecommendation(_ feedback: RecommendationRatingRequest) async throws {
        let response = try await send(
            path: "api/v1/coach/recommendation-feedback",
            method: "POST",
            body: encoder.encode(feedback),
            as: RecommendationFeedbackResponse.self)
        guard response.accepted else { throw WorkoutRepositoryError.invalidResponse }
    }

    private func send<Response: Decodable>(
        path: String,
        method: String,
        body: Data? = nil,
        as type: Response.Type
    ) async throws -> Response {
        var request = URLRequest(url: baseURL.appending(path: path))
        request.timeoutInterval = 20
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = body
        }
        var result = try await execute(request)
        if result.response.statusCode == 401 {
            _ = try await sessionManager.validAccessToken(forceRefresh: true)
            result = try await execute(request)
        }
        guard (200..<300).contains(result.response.statusCode) else {
            if result.response.statusCode == 401 { throw WorkoutRepositoryError.unauthorized }
            if result.response.statusCode == 502 || result.response.statusCode == 503 {
                throw WorkoutRepositoryError.unavailable
            }
            throw WorkoutRepositoryError.server(statusCode: result.response.statusCode)
        }
        do {
            return try decoder.decode(type, from: result.data)
        } catch {
            throw WorkoutRepositoryError.invalidResponse
        }
    }

    private func execute(_ request: URLRequest) async throws -> (data: Data, response: HTTPURLResponse) {
        var authorizedRequest = request
        let accessToken = try await sessionManager.validAccessToken()
        authorizedRequest.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await ResilientURLSession.data(for: authorizedRequest, using: session)
        guard let response = response as? HTTPURLResponse else {
            throw WorkoutRepositoryError.invalidResponse
        }
        return (data, response)
    }
}
