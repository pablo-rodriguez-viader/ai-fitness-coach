import Foundation

struct HealthSummary: Equatable, Sendable {
    var steps: Double?
    var activeEnergyKilocalories: Double?
    var totalEnergyKilocalories: Double? = nil
    var walkingDistanceKilometers: Double?
    var averageHeartRateBeatsPerMinute: Double?
    var latestWeightKilograms: Double?
    var sleepSeconds: Double? = nil

    var hasMetrics: Bool {
        steps != nil
            || activeEnergyKilocalories != nil
            || totalEnergyKilocalories != nil
            || walkingDistanceKilometers != nil
            || averageHeartRateBeatsPerMinute != nil
            || latestWeightKilograms != nil
            || sleepSeconds != nil
    }

    static let empty = HealthSummary()
}

enum HealthConnectionState: Equatable, Sendable {
    case notConnected
    case connected
    case unavailable
}

enum HealthPermissionState: Equatable, Sendable {
    case notRequested
    case requesting
    case requestCompleted
    case unavailable
}

enum HealthSummaryState: Equatable, Sendable {
    case notLoaded
    case loading
    case noData
    case available
    case failed(String)
}

@MainActor
protocol HealthDataRepository {
    var isAvailable: Bool { get }
    func requestReadAuthorization() async throws
    func fetchTodaySummary() async throws -> HealthSummary
    func enableAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws
    func resumeAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws -> Bool
    func clearPendingSync() async throws
    func retryPendingSync() async throws
    func syncOverview() async -> HealthSyncOverview
}

extension HealthDataRepository {
    func syncOverview() async -> HealthSyncOverview { .idle }
}

extension HealthSummary: Codable {}
