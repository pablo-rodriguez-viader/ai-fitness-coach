import XCTest
@testable import AIFitnessCoach

final class ProductSmokeJourneyTests: XCTestCase {
    @MainActor
    func testProfileSetupContinuesIntoHealthDashboard() async {
        let profile = UserProfileViewModel(repository: SmokeProfileRepository())
        profile.displayName = "Beta"
        profile.age = "32"
        profile.heightCentimeters = "170"
        profile.goal = .generalHealth
        profile.load(userId: "smoke-user")
        await waitUntil { profile.state == .needsSetup }

        profile.save()
        await waitUntil { profile.state == .ready }
        XCTAssertEqual(profile.profile?.goal, UserFitnessGoal.generalHealth.rawValue)

        let dashboard = DashboardViewModel(repository: SmokeHealthRepository())
        dashboard.connectHealth()
        await waitUntil { dashboard.summaryState == .available }
        XCTAssertEqual(dashboard.connectionState, .connected)
        XCTAssertEqual(dashboard.summary.steps, 1200)
    }

    @MainActor
    private func waitUntil(
        timeout: TimeInterval = 2,
        condition: @escaping @MainActor () -> Bool
    ) async {
        let deadline = Date.now.addingTimeInterval(timeout)
        while !condition() && Date.now < deadline {
            try? await Task.sleep(nanoseconds: 10_000_000)
        }
    }
}

private actor SmokeProfileRepository: UserProfileRepositoryProtocol {
    private var profile: UserProfile?

    func fetch() async throws -> UserProfile {
        guard let profile else { throw UserProfileError.server(statusCode: 404) }
        return profile
    }

    func save(_ request: UserProfileRequest) async throws -> UserProfile {
        let result = UserProfile(
            userId: "smoke-user",
            displayName: request.displayName,
            age: request.age,
            heightCentimeters: request.heightCentimeters,
            goal: request.goal)
        profile = result
        return result
    }
}

@MainActor
private final class SmokeHealthRepository: HealthDataRepository {
    var isAvailable = true

    func requestReadAuthorization() async throws {}

    func fetchTodaySummary() async throws -> HealthSummary {
        HealthSummary(
            steps: 1200,
            activeEnergyKilocalories: nil,
            totalEnergyKilocalories: nil,
            walkingDistanceKilometers: nil,
            averageHeartRateBeatsPerMinute: nil,
            latestWeightKilograms: nil,
            sleepSeconds: nil)
    }

    func enableAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws {}

    func resumeAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws -> Bool { false }

    func clearPendingSync() async throws {}
    func retryPendingSync() async throws {}
}
