import Combine
import Foundation
import UserNotifications

/// A local notification ready to schedule.
struct PlannedNotification: Equatable, Sendable {
    let identifier: String
    let kind: String
    let title: String
    let body: String
    let deepLink: String
    let fireDate: Date
}

/// Turns the server's reminders into local notifications at fixed local times. Pure logic.
enum NotificationPlanner {
    static let identifierPrefix = "engagement."
    /// Local hour per reminder kind: after work for training, evening for the daily goal,
    /// Sunday evening for the weekly summary.
    static let hours: [String: Int] = [
        "pending_workout": 18,
        "daily_goal": 20,
        "weekly_summary": 19
    ]

    static func plan(reminders: [ReminderInfo], now: Date, calendar: Calendar = .current) -> [PlannedNotification] {
        reminders.compactMap { reminder in
            guard let hour = hours[reminder.kind],
                  var components = EngagementDay.components(reminder.date)
            else { return nil }
            components.hour = hour
            components.minute = 0
            guard let fireDate = calendar.date(from: components),
                  fireDate > now.addingTimeInterval(60)
            else { return nil }
            return PlannedNotification(
                identifier: "\(identifierPrefix)\(reminder.kind).\(reminder.date)",
                kind: reminder.kind,
                title: reminder.title,
                body: reminder.body,
                deepLink: reminder.deepLink,
                fireDate: fireDate)
        }
    }
}

/// The parts of `UNUserNotificationCenter` the scheduler uses, so tests can replace it.
protocol NotificationCenterClient: AnyObject, Sendable {
    func authorizationStatus() async -> UNAuthorizationStatus
    func requestAuthorization() async throws -> Bool
    func pendingIdentifiers() async -> [String]
    func removePending(identifiers: [String])
    func add(_ notification: PlannedNotification, calendar: Calendar) async throws
}

final class SystemNotificationCenter: NotificationCenterClient, @unchecked Sendable {
    private let center = UNUserNotificationCenter.current()

    func authorizationStatus() async -> UNAuthorizationStatus {
        await center.notificationSettings().authorizationStatus
    }

    func requestAuthorization() async throws -> Bool {
        // Only what the reminders use: the app never sets a badge.
        try await center.requestAuthorization(options: [.alert, .sound])
    }

    func pendingIdentifiers() async -> [String] {
        await center.pendingNotificationRequests().map(\.identifier)
    }

    func removePending(identifiers: [String]) {
        center.removePendingNotificationRequests(withIdentifiers: identifiers)
    }

    func add(_ notification: PlannedNotification, calendar: Calendar) async throws {
        let content = UNMutableNotificationContent()
        content.title = notification.title
        content.body = notification.body
        content.sound = .default
        content.userInfo = ["deep_link": notification.deepLink, "kind": notification.kind]
        let components = calendar.dateComponents([.year, .month, .day, .hour, .minute], from: notification.fireDate)
        let trigger = UNCalendarNotificationTrigger(dateMatching: components, repeats: false)
        try await center.add(UNNotificationRequest(identifier: notification.identifier, content: content, trigger: trigger))
    }
}

/// Keeps the pending reminders in sync with the latest engagement summary.
@MainActor
final class NotificationScheduler: ObservableObject {
    @Published private(set) var authorizationStatus: UNAuthorizationStatus = .notDetermined

    private let center: NotificationCenterClient
    private let calendar: Calendar

    init(center: NotificationCenterClient = SystemNotificationCenter(), calendar: Calendar = .current) {
        self.center = center
        self.calendar = calendar
    }

    var canAskForPermission: Bool { authorizationStatus == .notDetermined }
    var isEnabled: Bool { authorizationStatus == .authorized || authorizationStatus == .provisional }

    func refreshStatus() async {
        authorizationStatus = await center.authorizationStatus()
    }

    /// Asks once; returns whether reminders are now allowed.
    @discardableResult
    func requestPermission() async -> Bool {
        let granted = (try? await center.requestAuthorization()) ?? false
        AppTelemetry.shared.track("notifications_permission", ["granted": granted ? "true" : "false"])
        await refreshStatus()
        return granted
    }

    /// Replaces this app's pending reminders with the ones for the current summary.
    func reschedule(reminders: [ReminderInfo], now: Date = .now) async {
        await refreshStatus()
        await cancelAll()
        guard isEnabled else { return }
        for notification in NotificationPlanner.plan(reminders: reminders, now: now, calendar: calendar) {
            try? await center.add(notification, calendar: calendar)
        }
    }

    /// Used on sign-out: a signed-out device must not keep sending reminders.
    func cancelAll() async {
        let ours = await center.pendingIdentifiers().filter { $0.hasPrefix(NotificationPlanner.identifierPrefix) }
        if !ours.isEmpty {
            center.removePending(identifiers: ours)
        }
    }
}

/// Opens the screen a reminder points to and shows reminders while the app is open.
final class NotificationResponder: NSObject, UNUserNotificationCenterDelegate, @unchecked Sendable {
    private let router: DeepLinkRouter

    init(router: DeepLinkRouter) {
        self.router = router
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        let info = response.notification.request.content.userInfo
        let kind = info["kind"] as? String ?? "unknown"
        AppTelemetry.shared.track("notification_opened", ["kind": kind])
        if let link = info["deep_link"] as? String, let url = URL(string: link) {
            let router = self.router
            Task { @MainActor in
                router.handle(url)
            }
        }
        completionHandler()
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .list, .sound])
    }
}
