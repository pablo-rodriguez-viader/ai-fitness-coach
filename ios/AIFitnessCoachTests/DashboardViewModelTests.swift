import XCTest
@testable import AIFitnessCoach

final class DashboardViewModelTests: XCTestCase {
    @MainActor
    func testUnavailableHealthKitIsDifferentFromNoData() {
        let viewModel = DashboardViewModel(
            repository: FakeHealthDataRepository(isAvailable: false)
        )

        XCTAssertEqual(viewModel.connectionState, .unavailable)
        XCTAssertEqual(viewModel.permissionState, .unavailable)
        XCTAssertEqual(viewModel.summaryState, .notLoaded)
    }

    @MainActor
    func testSuccessfulAuthorizationFlowCanStillHaveNoVisibleSamples() async {
        let viewModel = DashboardViewModel(
            repository: FakeHealthDataRepository(isAvailable: true)
        )
        viewModel.connectHealth()

        for _ in 0..<100 {
            if viewModel.summaryState == .noData {
                break
            }
            try? await Task.sleep(nanoseconds: 10_000_000)
        }

        XCTAssertEqual(viewModel.connectionState, .connected)
        XCTAssertEqual(viewModel.permissionState, .requestCompleted)
        XCTAssertEqual(viewModel.summaryState, .noData)
    }
}

@MainActor
private final class FakeHealthDataRepository: HealthDataRepository {
    let isAvailable: Bool

    init(isAvailable: Bool) {
        self.isAvailable = isAvailable
    }

    func requestReadAuthorization() async throws {}

    func fetchTodaySummary() async throws -> HealthSummary {
        .empty
    }

    func enableAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws {}

    func clearPendingSync() async throws {}

    func retryPendingSync() async throws {}

    func resumeAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws -> Bool { false }
}
