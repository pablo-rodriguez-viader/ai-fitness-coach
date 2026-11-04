import Foundation

private struct SyncBatchRequest: Encodable {
    let userId: String
    let metrics: [SyncMetricRequest]
    let deletedMetrics: [SyncMetricDeletionRequest]
}

private struct SyncMetricDeletionRequest: Encodable {
    let source: String
    let externalId: String
    let deviceId: String

    init(deletion: HealthSyncDeletion) {
        source = deletion.source
        externalId = deletion.externalId
        deviceId = deletion.deviceId
    }
}

private struct SyncMetricRequest: Encodable {
    let metric: String
    let value: Double
    let unit: String
    let observedAt: Date
    let source: String
    let externalId: String
    let deviceId: String

    init(job: HealthSyncJob) {
        metric = job.metricType.rawValue
        value = job.value
        unit = job.unit
        observedAt = job.timestamp
        source = job.source
        externalId = job.id.uuidString
        deviceId = job.deviceId
    }
}

private struct HealthSummaryResponse: Decodable {
    let steps: Double?
    let weightKilograms: Double?
    let activeEnergyKilocalories: Double?
    let totalEnergyKilocalories: Double?
    let averageHeartRateBeatsPerMinute: Double?
    let sleepSeconds: Double?

    var summary: HealthSummary {
        HealthSummary(
            steps: steps,
            activeEnergyKilocalories: activeEnergyKilocalories,
            totalEnergyKilocalories: totalEnergyKilocalories,
            walkingDistanceKilometers: nil,
            averageHeartRateBeatsPerMinute: averageHeartRateBeatsPerMinute,
            latestWeightKilograms: weightKilograms,
            sleepSeconds: sleepSeconds)
    }
}

actor HealthSyncAPIClient: HealthSyncTransport {
    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
    }

    func upload(
        _ jobs: [HealthSyncJob],
        deletions: [HealthSyncDeletion]
    ) async throws -> HealthSyncResult {
        guard !jobs.isEmpty || !deletions.isEmpty else {
            return HealthSyncResult(
                receivedCount: 0,
                insertedCount: 0,
                updatedCount: 0,
                duplicateCount: 0)
        }
        let userId = jobs.first?.userId ?? deletions.first?.userId
        guard let userId,
              jobs.allSatisfy({ $0.userId == userId && !$0.deviceId.isEmpty }),
              deletions.allSatisfy({ $0.userId == userId && !$0.deviceId.isEmpty })
        else {
            throw HealthSyncError.accountMismatch
        }
        var request = URLRequest(url: baseURL.appending(path: "api/v1/health-metrics/batch"))
        request.timeoutInterval = 20
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        request.httpBody = try encoder.encode(SyncBatchRequest(
            userId: userId,
            metrics: jobs.map(SyncMetricRequest.init),
            deletedMetrics: deletions.map(SyncMetricDeletionRequest.init)))

        let (data, response) = try await performAuthenticated(request)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw HealthSyncError.server(statusCode: 0)
        }
        guard (200..<300).contains(httpResponse.statusCode) else {
            if httpResponse.statusCode == 401 {
                throw HealthSyncError.unauthorized
            }
            throw HealthSyncError.server(statusCode: httpResponse.statusCode)
        }
        return try JSONDecoder().decode(HealthSyncResult.self, from: data)
    }

    func fetchSummary(from: Date, to: Date) async throws -> HealthSummary {
        guard var components = URLComponents(
            url: baseURL.appending(path: "api/v1/health-summary"),
            resolvingAgainstBaseURL: false)
        else {
            throw HealthSyncError.server(statusCode: 0)
        }
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        components.queryItems = [
            URLQueryItem(name: "from", value: formatter.string(from: from)),
            URLQueryItem(name: "to", value: formatter.string(from: to))
        ]
        guard let url = components.url else {
            throw HealthSyncError.server(statusCode: 0)
        }
        var request = URLRequest(url: url)
        request.timeoutInterval = 20
        request.httpMethod = "GET"
        let (data, response) = try await performAuthenticated(request)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw HealthSyncError.server(statusCode: 0)
        }
        guard (200..<300).contains(httpResponse.statusCode) else {
            if httpResponse.statusCode == 401 {
                throw HealthSyncError.unauthorized
            }
            throw HealthSyncError.server(statusCode: httpResponse.statusCode)
        }
        return try JSONDecoder().decode(HealthSummaryResponse.self, from: data).summary
    }

    private func performAuthenticated(_ request: URLRequest) async throws -> (Data, URLResponse) {
        var authorizedRequest = request
        do {
            let token = try await sessionManager.validAccessToken()
            authorizedRequest.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        } catch let error as URLError where Self.isOffline(error) {
            throw HealthSyncError.offline
        }

        var result = try await perform(authorizedRequest)
        if (result.1 as? HTTPURLResponse)?.statusCode == 401 {
            do {
                let token = try await sessionManager.validAccessToken(forceRefresh: true)
                authorizedRequest.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            } catch let error as URLError where Self.isOffline(error) {
                throw HealthSyncError.offline
            }
            result = try await perform(authorizedRequest)
        }
        return result
    }

    private func perform(_ request: URLRequest) async throws -> (Data, URLResponse) {
        do {
            return try await session.data(for: request)
        } catch let error as URLError where Self.isOffline(error) {
            throw HealthSyncError.offline
        }
    }

    private static func isOffline(_ error: URLError) -> Bool {
        [
            .notConnectedToInternet,
            .networkConnectionLost,
            .cannotConnectToHost,
            .cannotFindHost,
            .timedOut
        ].contains(error.code)
    }
}
