import Combine
import Foundation
import UIKit

@MainActor
final class SessionManager: ObservableObject {
    @Published private(set) var userId: String?
    @Published private(set) var isRestoring = true
    @Published private(set) var errorMessage: String?

    private let service: AuthenticationService?
    private let keychain: KeychainManager
    private var accessToken: String?
    private var refreshToken: String?
    private var accessTokenExpiresAt: Date?
    private var refreshTask: Task<AuthenticationTokenPair, Error>?
    private var restoreTask: Task<Void, Never>?
    /// Set when restoring failed because the device was locked or offline-before-first-read,
    /// so the next foreground can try again instead of showing the sign-in screen forever.
    private(set) var needsRestoreRetry = false
    private(set) var configurationError: String?

    private static let sessionKeys = ["refresh_token", "access_token", "user_id", "access_expiry"]

    init(
        service: AuthenticationService?,
        keychain: KeychainManager = KeychainManager(),
        configurationError: String? = nil
    ) {
        self.service = service
        self.keychain = keychain
        self.configurationError = configurationError
        isRestoring = service != nil
    }

    var isAuthenticated: Bool {
        userId != nil && accessToken != nil
    }

    /// Restores the stored session once; concurrent callers wait for the same restore.
    /// `onStoredSessionLoaded` runs as soon as the Keychain session is in memory, before the
    /// network refresh, so HealthKit observers can start within the background launch budget.
    func restoreSession(onStoredSessionLoaded: (@MainActor () -> Void)? = nil) async {
        if let restoreTask {
            await restoreTask.value
            return
        }
        let task = Task { @MainActor [weak self] in
            guard let self else { return }
            await self.performRestore(onStoredSessionLoaded: onStoredSessionLoaded)
        }
        restoreTask = task
        await task.value
        restoreTask = nil
    }

    /// Retries a restore that failed for a transient reason (locked device). No-op otherwise.
    func restoreSessionIfNeeded(onStoredSessionLoaded: (@MainActor () -> Void)? = nil) async {
        guard needsRestoreRetry, !isAuthenticated, service != nil else { return }
        isRestoring = true
        await restoreSession(onStoredSessionLoaded: onStoredSessionLoaded)
    }

    private func performRestore(onStoredSessionLoaded: (@MainActor () -> Void)?) async {
        defer { isRestoring = false }
        guard let service else {
            errorMessage = configurationError
            return
        }

        do {
            guard let storedRefreshToken = try keychain.read("refresh_token"),
                  let storedAccessToken = try keychain.read("access_token"),
                  let storedUserId = try keychain.read("user_id")
            else {
                needsRestoreRetry = false
                return
            }
            needsRestoreRetry = false
            keychain.upgradeAccessibility(of: Self.sessionKeys)
            refreshToken = storedRefreshToken
            accessToken = storedAccessToken
            userId = storedUserId
            if let expiry = try keychain.read("access_expiry") {
                accessTokenExpiresAt = ISO8601DateFormatter.withFractionalSeconds.date(from: expiry)
                    ?? ISO8601DateFormatter.withoutFractionalSeconds.date(from: expiry)
            }
            onStoredSessionLoaded?()
            let pair = try await refreshedToken(using: service, refreshToken: storedRefreshToken)
            try persist(pair)
            errorMessage = nil
            AppTelemetry.shared.track("session_restored")
        } catch AuthenticationError.unauthorized {
            expireSession()
        } catch let error as KeychainError where error.isDeviceLocked {
            // Launched in the background before the first unlock: try again when active.
            needsRestoreRetry = !isAuthenticated
        } catch {
            // Offline or server error: keep the stored session and refresh later.
            if isAuthenticated {
                errorMessage = nil
            } else {
                errorMessage = error.localizedDescription
            }
        }
    }

    func signIn(
        identityToken: String,
        nonce: String,
        displayName: String?
    ) async throws {
        guard let service else {
            throw AuthenticationError.notConfigured(configurationError ?? "API no configurada.")
        }
        let pair = try await service.signInWithApple(
            identityToken: identityToken,
            nonce: nonce,
            displayName: displayName)
        try persist(pair)
        errorMessage = nil
        needsRestoreRetry = false
        AppTelemetry.shared.track("sign_in")
    }

    /// Personal build: signs in with the owner's access code instead of Sign in with Apple.
    func signIn(personalAccessCode code: String) async throws {
        guard let service else {
            throw AuthenticationError.notConfigured(configurationError ?? "API no configurada.")
        }
        let pair = try await service.signInWithPersonalCode(code)
        try persist(pair)
        errorMessage = nil
        needsRestoreRetry = false
        AppTelemetry.shared.track("sign_in", ["method": "personal_code"])
    }

    func validAccessToken(forceRefresh: Bool = false) async throws -> String {
        guard let service else {
            throw AuthenticationError.notConfigured(configurationError ?? "API no configurada.")
        }
        guard let refreshToken else {
            throw AuthenticationError.missingTokens
        }

        if !forceRefresh,
           let accessToken,
           let expiry = accessTokenExpiresAt,
           expiry.timeIntervalSinceNow > 60 {
            return accessToken
        }

        do {
            let pair = try await refreshedToken(using: service, refreshToken: refreshToken)
            try persist(pair)
            return pair.accessToken
        } catch let error as URLError where !forceRefresh && accessToken != nil {
            if let existingAccessToken = accessToken {
                return existingAccessToken
            }
            throw error
        } catch {
            if let authenticationError = error as? AuthenticationError,
               case .unauthorized = authenticationError {
                expireSession()
            }
            throw error
        }
    }

    func signOut() async throws {
        let token = refreshToken
        defer {
            clearMemory()
            do {
                try keychain.deleteTokens()
            } catch {
                errorMessage = error.localizedDescription
            }
        }
        AppTelemetry.shared.track("sign_out")
        if let token, let service {
            try await service.revoke(refreshToken: token)
        }
    }

    /// The account was deleted on the server, so there is no session left to revoke.
    func endDeletedAccount() {
        clearMemory()
        do {
            try keychain.deleteTokens()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// The server no longer accepts this session: drop it and tell the user why.
    private func expireSession() {
        clearMemory()
        do {
            try keychain.deleteTokens()
        } catch {
            NSLog("Keychain cleanup after session expiry failed: %@", error.localizedDescription)
        }
        errorMessage = AuthenticationError.unauthorized.localizedDescription
        AppTelemetry.shared.track("session_expired")
    }

    private func persist(_ pair: AuthenticationTokenPair) throws {
        do {
            try keychain.save(pair.refreshToken, for: "refresh_token")
            try keychain.save(pair.userId, for: "user_id")
            if let expiry = pair.accessTokenExpiry {
                try keychain.save(
                    ISO8601DateFormatter.withFractionalSeconds.string(from: expiry),
                    for: "access_expiry")
            }
            try keychain.save(pair.accessToken, for: "access_token")
        } catch {
            clearMemory()
            throw error
        }
        accessToken = pair.accessToken
        refreshToken = pair.refreshToken
        accessTokenExpiresAt = pair.accessTokenExpiry
        userId = pair.userId
    }

    private func clearMemory() {
        refreshTask?.cancel()
        refreshTask = nil
        accessToken = nil
        refreshToken = nil
        accessTokenExpiresAt = nil
        userId = nil
    }

    private func refreshedToken(
        using service: AuthenticationService,
        refreshToken: String
    ) async throws -> AuthenticationTokenPair {
        if let refreshTask { return try await refreshTask.value }
        // Keep running briefly if the app is backgrounded mid-refresh: losing the response
        // after the server rotated the token would force the user to sign in again.
        let backgroundTask = BackgroundTaskToken()
        backgroundTask.identifier = UIApplication.shared.beginBackgroundTask(withName: "session-refresh") {
            // iOS terminates apps that overrun background time without ending the task.
            backgroundTask.end()
        }
        let task = Task { try await service.refresh(refreshToken: refreshToken) }
        refreshTask = task
        defer {
            refreshTask = nil
            backgroundTask.end()
        }
        return try await task.value
    }
}

/// Ends a UIKit background task exactly once, from the expiration handler or normal completion.
@MainActor
private final class BackgroundTaskToken {
    var identifier: UIBackgroundTaskIdentifier = .invalid

    func end() {
        guard identifier != .invalid else { return }
        UIApplication.shared.endBackgroundTask(identifier)
        identifier = .invalid
    }
}

private extension ISO8601DateFormatter {
    static let withFractionalSeconds: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    static let withoutFractionalSeconds: ISO8601DateFormatter = {
        ISO8601DateFormatter()
    }()
}
