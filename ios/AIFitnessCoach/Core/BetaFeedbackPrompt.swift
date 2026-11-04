import Combine
import Foundation

/// Asks TestFlight testers for feedback after they have used the app on a few days, so the team
/// hears from every tester without chasing them. Beta builds only, behind the `beta_feedback`
/// flag; shown at most 3 times, 14 days apart. State lives in a small file (not UserDefaults,
/// which would need a privacy-manifest reason).
@MainActor
final class BetaFeedbackPrompt: ObservableObject {
    struct State: Codable, Equatable {
        var activeDays: [String] = []
        var timesShown = 0
        var lastShownAt: Date?
    }

    static let minimumActiveDays = 3
    static let maximumTimesShown = 3
    static let minimumInterval: TimeInterval = 14 * 24 * 60 * 60

    @Published private(set) var isVisible = false
    private(set) var state: State
    private let fileURL: URL?
    private let environment: AppEnvironment

    init(fileURL: URL? = BetaFeedbackPrompt.defaultFileURL(), environment: AppEnvironment = .current) {
        self.fileURL = fileURL
        self.environment = environment
        state = fileURL.flatMap { try? Data(contentsOf: $0) }
            .flatMap { try? JSONDecoder().decode(State.self, from: $0) } ?? State()
    }

    /// Called when the app comes to the foreground.
    func recordActiveDay(now: Date = .now, calendar: Calendar = .current, isEnabled: Bool) {
        let day = Self.dayKey(now, calendar: calendar)
        if state.activeDays.last != day {
            state.activeDays = Array((state.activeDays + [day]).suffix(30))
            persist()
        }
        if !isVisible, Self.shouldShow(state, environment: environment, isEnabled: isEnabled, now: now) {
            isVisible = true
            state.timesShown += 1
            state.lastShownAt = now
            persist()
            AppTelemetry.shared.track("beta_feedback_prompt_shown", ["times": String(state.timesShown)])
        }
    }

    /// The tester sent feedback or tapped "Ahora no".
    func dismiss(sentFeedback: Bool) {
        guard isVisible else { return }
        isVisible = false
        if sentFeedback {
            // Feedback received: do not ask again in this beta.
            state.timesShown = Self.maximumTimesShown
            persist()
        }
        AppTelemetry.shared.track("beta_feedback_prompt_dismissed", ["sent": sentFeedback ? "true" : "false"])
    }

    func reset() {
        isVisible = false
        state = State()
        persist()
    }

    nonisolated static func shouldShow(_ state: State, environment: AppEnvironment, isEnabled: Bool, now: Date) -> Bool {
        guard environment == .beta, isEnabled,
              Set(state.activeDays).count >= minimumActiveDays,
              state.timesShown < maximumTimesShown
        else { return false }
        guard let lastShownAt = state.lastShownAt else { return true }
        return now.timeIntervalSince(lastShownAt) >= minimumInterval
    }

    nonisolated static func dayKey(_ date: Date, calendar: Calendar) -> String {
        let components = calendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d-%02d-%02d", components.year ?? 0, components.month ?? 0, components.day ?? 0)
    }

    private func persist() {
        guard let fileURL, let data = try? JSONEncoder().encode(state) else { return }
        try? data.write(to: fileURL, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    nonisolated static func defaultFileURL() -> URL? {
        guard var directory = try? FileManager.default.url(
            for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        else { return nil }
        directory.append(path: "BetaFeedback", directoryHint: .isDirectory)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory.appending(path: "prompt.json")
    }
}
