import UserNotifications
import XCTest
@testable import AIFitnessCoach

final class NotificationTests: XCTestCase {
    private var calendar: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "Europe/Madrid")!
        return calendar
    }()

    private func date(_ year: Int, _ month: Int, _ day: Int, _ hour: Int, _ minute: Int = 0) -> Date {
        calendar.date(from: DateComponents(year: year, month: month, day: day, hour: hour, minute: minute))!
    }

    private func reminder(_ kind: String, _ day: String) -> ReminderInfo {
        ReminderInfo(kind: kind, title: "Título", body: "Cuerpo", deepLink: "aifitnesscoach://workouts", date: day)
    }

    func testRemindersAreScheduledAtFixedLocalTimes() {
        let now = date(2026, 10, 22, 9)
        let planned = NotificationPlanner.plan(
            reminders: [
                reminder("pending_workout", "2026-10-22"),
                reminder("daily_goal", "2026-10-22"),
                reminder("weekly_summary", "2026-10-25")
            ],
            now: now,
            calendar: calendar)

        XCTAssertEqual(planned.map(\.kind), ["pending_workout", "daily_goal", "weekly_summary"])
        XCTAssertEqual(planned[0].fireDate, date(2026, 10, 22, 18))
        XCTAssertEqual(planned[1].fireDate, date(2026, 10, 22, 20))
        XCTAssertEqual(planned[2].fireDate, date(2026, 10, 25, 19))
        XCTAssertEqual(planned[0].identifier, "engagement.pending_workout.2026-10-22")
    }

    func testRemindersWhoseTimeHasPassedAreSkipped() {
        let planned = NotificationPlanner.plan(
            reminders: [reminder("pending_workout", "2026-10-22"), reminder("daily_goal", "2026-10-22")],
            now: date(2026, 10, 22, 19),
            calendar: calendar)

        XCTAssertEqual(planned.map(\.kind), ["daily_goal"])
    }

    func testUnknownKindsAndBadDatesAreIgnored() {
        let planned = NotificationPlanner.plan(
            reminders: [reminder("marketing", "2026-10-22"), reminder("daily_goal", "not-a-date")],
            now: date(2026, 10, 22, 9),
            calendar: calendar)

        XCTAssertTrue(planned.isEmpty)
    }

    @MainActor
    func testReschedulingReplacesOnlyThisAppsReminders() async {
        let center = FakeNotificationCenter(status: .authorized)
        center.pending = ["engagement.daily_goal.2026-10-21", "other.app.reminder"]
        let scheduler = NotificationScheduler(center: center, calendar: calendar)

        await scheduler.reschedule(
            reminders: [reminder("daily_goal", "2026-10-22")],
            now: date(2026, 10, 22, 9))

        XCTAssertEqual(center.removed, ["engagement.daily_goal.2026-10-21"])
        XCTAssertEqual(center.added.map(\.identifier), ["engagement.daily_goal.2026-10-22"])
    }

    @MainActor
    func testNothingIsScheduledWithoutPermission() async {
        let center = FakeNotificationCenter(status: .denied)
        let scheduler = NotificationScheduler(center: center, calendar: calendar)

        await scheduler.reschedule(reminders: [reminder("daily_goal", "2026-10-22")], now: date(2026, 10, 22, 9))

        XCTAssertTrue(center.added.isEmpty)
        XCTAssertFalse(scheduler.isEnabled)
        XCTAssertFalse(scheduler.canAskForPermission)
    }

    @MainActor
    func testPermissionRequestUpdatesStatus() async {
        let center = FakeNotificationCenter(status: .notDetermined, grants: true)
        let scheduler = NotificationScheduler(center: center, calendar: calendar)
        await scheduler.refreshStatus()
        XCTAssertTrue(scheduler.canAskForPermission)

        let granted = await scheduler.requestPermission()

        XCTAssertTrue(granted)
        XCTAssertTrue(scheduler.isEnabled)
    }

    // A signed-out device must not keep reminding the previous user.
    @MainActor
    func testCancelAllRemovesPendingReminders() async {
        let center = FakeNotificationCenter(status: .authorized)
        center.pending = ["engagement.weekly_summary.2026-10-25", "engagement.daily_goal.2026-10-22", "other"]
        let scheduler = NotificationScheduler(center: center, calendar: calendar)

        await scheduler.cancelAll()

        XCTAssertEqual(Set(center.removed), ["engagement.weekly_summary.2026-10-25", "engagement.daily_goal.2026-10-22"])
    }
}

final class FakeNotificationCenter: NotificationCenterClient, @unchecked Sendable {
    var status: UNAuthorizationStatus
    let grants: Bool
    var pending: [String] = []
    private(set) var removed: [String] = []
    private(set) var added: [PlannedNotification] = []

    init(status: UNAuthorizationStatus, grants: Bool = false) {
        self.status = status
        self.grants = grants
    }

    func authorizationStatus() async -> UNAuthorizationStatus { status }

    func requestAuthorization() async throws -> Bool {
        status = grants ? .authorized : .denied
        return grants
    }

    func pendingIdentifiers() async -> [String] { pending }

    func removePending(identifiers: [String]) {
        removed += identifiers
        pending.removeAll { identifiers.contains($0) }
    }

    func add(_ notification: PlannedNotification, calendar: Calendar) async throws {
        added.append(notification)
        pending.append(notification.identifier)
    }
}
