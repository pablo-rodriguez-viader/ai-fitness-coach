import UserNotifications
import XCTest
@testable import AIFitnessCoach

final class EngagementTests: XCTestCase {
    /// Shape produced by the API (System.Text.Json, camelCase, 7 fractional digits).
    static let sampleJSON = """
    {
      "generatedAt": "2026-10-22T09:15:30.1234567+00:00",
      "streak": { "current": 3, "longest": 5, "activeToday": true, "lastActiveDay": "2026-10-22" },
      "weeklySummary": {
        "from": "2026-10-16", "to": "2026-10-22", "activeDays": 5, "workoutsCompleted": 3,
        "workoutsPlanned": 4, "previousWeekWorkoutsCompleted": 2, "mealsLogged": 6, "coachQueries": 2,
        "feedbackSubmitted": 3, "averageDailySteps": 7421, "headline": "¡Semana muy constante!",
        "highlights": ["Completaste 3 entrenamientos de 4 planificados."]
      },
      "achievements": [
        { "id": "first_workout", "title": "Primer entrenamiento", "description": "d", "unlocked": true, "unlockedOn": "2026-10-10", "progress": 1, "target": 1 },
        { "id": "workouts_10", "title": "10 entrenamientos", "description": "d", "unlocked": false, "unlockedOn": null, "progress": 6, "target": 10 }
      ],
      "adherence": {
        "from": "2026-09-25", "to": "2026-10-22",
        "training": { "achieved": 9, "expected": 12, "rate": 0.75 },
        "nutrition": { "achieved": 0, "expected": 0, "rate": null },
        "goals": { "achieved": 10, "expected": 20, "rate": 0.5 }
      },
      "reminders": [
        { "kind": "weekly_summary", "title": "Tu resumen semanal", "body": "b", "deepLink": "aifitnesscoach://summary", "date": "2026-10-25" }
      ]
    }
    """

    func testDecodesTheServerSummary() throws {
        let summary = try EngagementRepository.decode(Data(Self.sampleJSON.utf8))

        XCTAssertEqual(summary.streak.current, 3)
        XCTAssertEqual(summary.unlockedAchievements, 1)
        XCTAssertEqual(summary.achievements[1].fractionComplete, 0.6, accuracy: 0.001)
        XCTAssertNil(summary.adherence.nutrition.rate)
        XCTAssertEqual(summary.reminders.first?.deepLink, "aifitnesscoach://summary")
        XCTAssertEqual(summary.generatedAt.timeIntervalSince1970,
                       ISO8601DateFormatter().date(from: "2026-10-22T09:15:30Z")!.timeIntervalSince1970 + 0.123,
                       accuracy: 0.001)
    }

    func testDatesWithAnyNumberOfFractionalDigitsParse() {
        XCTAssertNotNil(EngagementRepository.parseDate("2026-10-22T09:15:30Z"))
        XCTAssertNotNil(EngagementRepository.parseDate("2026-10-22T09:15:30.5Z"))
        XCTAssertNotNil(EngagementRepository.parseDate("2026-10-22T09:15:30.1234567-05:00"))
        XCTAssertNil(EngagementRepository.parseDate("yesterday"))
    }

    func testCalendarDaysDoNotShiftWithTimeZones() {
        XCTAssertEqual(EngagementDay.components("2026-10-25"), DateComponents(year: 2026, month: 10, day: 25))
        XCTAssertNil(EngagementDay.components("25/10/2026"))
    }

    @MainActor
    func testLoadingSchedulesTheRemindersFromTheSummary() async throws {
        let center = FakeNotificationCenter(status: .authorized)
        let viewModel = EngagementViewModel(
            repository: StubEngagementRepository(results: [.success(try summary(reminderDay: farFutureSunday()))]),
            notifications: NotificationScheduler(center: center))

        viewModel.load()
        try await waitUntil { viewModel.state == .loaded && !center.added.isEmpty }

        XCTAssertEqual(viewModel.summary?.streak.current, 3)
        XCTAssertEqual(center.added.first?.kind, "weekly_summary")
    }

    @MainActor
    func testFailedFirstLoadIsAnErrorButLaterFailuresKeepTheLastSummary() async throws {
        let repository = StubEngagementRepository(results: [
            .failure(URLError(.notConnectedToInternet)),
            .success(try summary(reminderDay: farFutureSunday())),
            .failure(URLError(.notConnectedToInternet))
        ])
        let viewModel = EngagementViewModel(repository: repository)

        viewModel.load()
        try await waitUntil { if case .failed = viewModel.state { return true }; return false }
        XCTAssertEqual(viewModel.state, .failed(URLError(.notConnectedToInternet).displayMessage))

        viewModel.load()
        try await waitUntil { viewModel.state == .loaded }
        viewModel.load()
        try await waitUntil { viewModel.isShowingStaleData }

        XCTAssertEqual(viewModel.state, .loaded)
        XCTAssertNotNil(viewModel.summary)
    }

    @MainActor
    func testNewUserSeesTheWelcomeState() async throws {
        let empty = Self.sampleJSON
            .replacingOccurrences(of: #""current": 3, "longest": 5"#, with: #""current": 0, "longest": 0"#)
            .replacingOccurrences(of: #""activeDays": 5"#, with: #""activeDays": 0"#)
            .replacingOccurrences(of: #""unlocked": true"#, with: #""unlocked": false"#)
        let viewModel = EngagementViewModel(
            repository: StubEngagementRepository(results: [.success(try EngagementRepository.decode(Data(empty.utf8)))]))

        viewModel.load()
        try await waitUntil { viewModel.state == .loaded }

        XCTAssertTrue(viewModel.isEmpty)
    }

    @MainActor
    func testSignOutClearsTheSummaryAndReminders() async throws {
        let center = FakeNotificationCenter(status: .authorized)
        let viewModel = EngagementViewModel(
            repository: StubEngagementRepository(results: [.success(try summary(reminderDay: farFutureSunday()))]),
            notifications: NotificationScheduler(center: center))
        viewModel.load()
        try await waitUntil { !center.added.isEmpty }

        viewModel.resetForSignOut()
        try await waitUntil { !center.removed.isEmpty }

        XCTAssertNil(viewModel.summary)
        XCTAssertEqual(viewModel.state, .idle)
        XCTAssertTrue(center.pending.isEmpty)
    }

    func testSummaryAndAchievementsAreReachableByDeepLink() {
        XCTAssertEqual(AppDestination(url: URL(string: "aifitnesscoach://summary")!), .summary)
        XCTAssertEqual(AppDestination(url: URL(string: "aifitnesscoach://achievements")!), .achievements)
    }

    private func summary(reminderDay: String) throws -> EngagementSummary {
        try EngagementRepository.decode(Data(Self.sampleJSON.replacingOccurrences(of: "2026-10-25", with: reminderDay).utf8))
    }

    private func farFutureSunday() -> String { "2099-01-04" }

    @MainActor
    private func waitUntil(timeout: Duration = .seconds(2), condition: @MainActor () -> Bool) async throws {
        let start = ContinuousClock.now
        while !condition() {
            if ContinuousClock.now - start > timeout {
                XCTFail("Timed out waiting for the expected state.")
                return
            }
            try await Task.sleep(for: .milliseconds(10))
        }
    }
}

private actor StubEngagementRepository: EngagementRepositoryProtocol {
    private var results: [Result<EngagementSummary, Error>]

    init(results: [Result<EngagementSummary, Error>]) {
        self.results = results
    }

    func summary() async throws -> EngagementSummary {
        let next = results.count > 1 ? results.removeFirst() : results[0]
        return try next.get()
    }
}
