import UIKit

/// Asks iOS for extra time so a request the user started (deleting the account, sending a
/// report) finishes even if they leave the app right after tapping. Without it the request can
/// be cut off when the app is suspended and the result is unknown.
@MainActor
enum BackgroundWork {
    static func run<T>(_ name: String, _ operation: () async throws -> T) async rethrows -> T {
        let token = Token()
        token.identifier = UIApplication.shared.beginBackgroundTask(withName: name) {
            // Out of time: end the task now (synchronously) so iOS does not terminate the app.
            // Same pattern as SessionManager's refresh task.
            token.end()
        }
        defer { token.end() }
        return try await operation()
    }

    /// Ends the UIKit background task exactly once.
    @MainActor
    private final class Token {
        var identifier: UIBackgroundTaskIdentifier = .invalid

        func end() {
            guard identifier != .invalid else { return }
            UIApplication.shared.endBackgroundTask(identifier)
            identifier = .invalid
        }
    }
}
