import Foundation

actor WellnessRepository {
    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(ISO8601DateFormatter().string(from: date))
        }
        self.encoder = encoder
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let value = try container.decode(String.self)
            let iso = ISO8601DateFormatter()
            iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            let regular = ISO8601DateFormatter()
            let dateFormatter = DateFormatter()
            dateFormatter.calendar = Calendar(identifier: .iso8601)
            dateFormatter.timeZone = TimeZone(secondsFromGMT: 0)
            dateFormatter.dateFormat = "yyyy-MM-dd"
            guard let date = iso.date(from: value)
                ?? regular.date(from: value)
                ?? dateFormatter.date(from: value)
            else {
                throw DecodingError.dataCorruptedError(
                    in: container,
                    debugDescription: "Invalid ISO-8601 date.")
            }
            return date
        }
        self.decoder = decoder
    }

    func goals() async throws -> [GoalRecord] {
        try await send(path: "api/v1/goals", method: "GET", as: [GoalRecord].self)
    }

    func createGoal(_ request: GoalRequest) async throws -> GoalRecord {
        try await send(path: "api/v1/goals", method: "POST", body: encoder.encode(request), as: GoalRecord.self)
    }

    func setNutritionProfile(_ request: NutritionProfileRequest) async throws -> NutritionTargets {
        try await send(path: "api/v1/nutrition/profile", method: "POST", body: encoder.encode(request), as: NutritionTargets.self)
    }

    func nutritionTargets() async throws -> NutritionTargets {
        try await send(path: "api/v1/nutrition/targets", method: "GET", as: NutritionTargets.self)
    }

    func addNutritionLog(_ request: NutritionLogRequest) async throws -> NutritionSummary {
        try await send(path: "api/v1/nutrition/log", method: "POST", body: encoder.encode(request), as: NutritionSummary.self)
    }

    func nutritionSummary(date: Date = .now) async throws -> NutritionSummary {
        var components = URLComponents(url: baseURL.appending(path: "api/v1/nutrition/summary"), resolvingAgainstBaseURL: false)
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .iso8601)
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        formatter.dateFormat = "yyyy-MM-dd"
        components?.queryItems = [URLQueryItem(name: "date", value: formatter.string(from: date))]
        guard let url = components?.url else { throw WellnessError.invalidResponse }
        return try await send(url: url, method: "GET", as: NutritionSummary.self)
    }

    func progress() async throws -> ProgressSnapshot {
        try await send(path: "api/v1/progress", method: "GET", as: ProgressSnapshot.self)
    }

    private func send<Response: Decodable>(
        path: String,
        method: String,
        body: Data? = nil,
        as type: Response.Type
    ) async throws -> Response {
        try await send(url: baseURL.appending(path: path), method: method, body: body, as: type)
    }

    private func send<Response: Decodable>(
        url: URL,
        method: String,
        body: Data? = nil,
        as type: Response.Type
    ) async throws -> Response {
        var request = URLRequest(url: url)
        request.timeoutInterval = 20
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = body
        }
        var result = try await execute(request)
        if result.1.statusCode == 401 {
            _ = try await sessionManager.validAccessToken(forceRefresh: true)
            result = try await execute(request)
        }
        guard (200..<300).contains(result.1.statusCode) else {
            if result.1.statusCode == 401 { throw WellnessError.unauthorized }
            if result.1.statusCode == 400,
               let message = APIValidationProblem.decode(from: result.0)?.firstMessage {
                throw WellnessError.validation(message)
            }
            throw WellnessError.server(statusCode: result.1.statusCode)
        }
        do {
            return try decoder.decode(type, from: result.0)
        } catch {
            throw WellnessError.invalidResponse
        }
    }

    private func execute(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var authorized = request
        let token = try await sessionManager.validAccessToken()
        authorized.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        let (data, response) = try await ResilientURLSession.data(for: authorized, using: session)
        guard let http = response as? HTTPURLResponse else { throw WellnessError.invalidResponse }
        return (data, http)
    }
}
