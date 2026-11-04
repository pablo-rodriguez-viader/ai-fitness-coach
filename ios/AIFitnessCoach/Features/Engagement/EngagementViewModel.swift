import Combine
import Foundation

@MainActor
final class EngagementViewModel: ObservableObject {
    enum State: Equatable {
        case idle
        case loading
        case loaded
        case failed(String)
    }

    @Published private(set) var summary: EngagementSummary?
    @Published private(set) var state: State = .idle
    /// The last refresh failed and the summary on screen is the previous one.
    @Published private(set) var isShowingStaleData = false

    let notifications: NotificationScheduler?
    private let repository: any EngagementRepositoryProtocol
    private var loadTask: Task<Void, Never>?

    init(repository: any EngagementRepositoryProtocol, notifications: NotificationScheduler? = nil) {
        self.repository = repository
        self.notifications = notifications
    }

    /// True until the user has done anything that counts: the screens show a welcome state.
    var isEmpty: Bool {
        guard let summary else { return false }
        return summary.streak.longest == 0 && summary.unlockedAchievements == 0 && summary.weeklySummary.activeDays == 0
    }

    func load() {
        loadTask?.cancel()
        if summary == nil {
            state = .loading
        }
        loadTask = Task { [weak self] in
            guard let self else { return }
            do {
                let latest = try await repository.summary()
                summary = latest
                state = .loaded
                isShowingStaleData = false
                await notifications?.reschedule(reminders: latest.reminders)
            } catch {
                guard !error.isCancellation else { return }
                if summary != nil {
                    isShowingStaleData = true
                    state = .loaded
                } else {
                    state = .failed(error.displayMessage)
                }
                error.track(area: "engagement")
            }
        }
    }

    func enableReminders() {
        guard let notifications else { return }
        Task { [weak self] in
            guard await notifications.requestPermission(), let reminders = self?.summary?.reminders else { return }
            await notifications.reschedule(reminders: reminders)
        }
    }

    func resetForSignOut() {
        loadTask?.cancel()
        loadTask = nil
        summary = nil
        state = .idle
        isShowingStaleData = false
        Task { [notifications] in await notifications?.cancelAll() }
    }
}
