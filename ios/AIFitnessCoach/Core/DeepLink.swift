import Combine
import Foundation

/// Screens reachable from a link such as `aifitnesscoach://coach`. Only existing screens.
enum AppDestination: String, CaseIterable, Equatable, Identifiable, Sendable {
    case dashboard
    case coach
    case analysis
    case workouts
    case goals
    case nutrition
    case progress
    case summary
    case achievements

    static let scheme = "aifitnesscoach"

    var id: String { rawValue }

    init?(url: URL) {
        guard url.scheme?.lowercased() == Self.scheme else { return nil }
        // Accept both aifitnesscoach://coach and aifitnesscoach:///coach.
        let key = (url.host(percentEncoded: false) ?? url.pathComponents.dropFirst().first ?? "")
            .lowercased()
        guard let destination = AppDestination(rawValue: key.isEmpty ? "dashboard" : key) else {
            return nil
        }
        self = destination
    }
}

/// Holds a link until the dashboard can show it (for example, after sign-in finishes).
@MainActor
final class DeepLinkRouter: ObservableObject {
    @Published var pending: AppDestination?

    @discardableResult
    func handle(_ url: URL) -> Bool {
        guard let destination = AppDestination(url: url) else { return false }
        pending = destination
        AppTelemetry.shared.track("deep_link_opened", ["destination": destination.rawValue])
        return true
    }

    func consume() -> AppDestination? {
        defer { pending = nil }
        return pending
    }
}
