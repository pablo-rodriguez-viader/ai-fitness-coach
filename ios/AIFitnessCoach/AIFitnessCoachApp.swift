import BackgroundTasks
import Combine
import SwiftUI
import UIKit
import UserNotifications

private enum SignOutError: Error {
    case failed
}

@MainActor
final class AppDependencies: ObservableObject {
    /// Must match `BGTaskSchedulerPermittedIdentifiers` in Info.plist (`$(PRODUCT_BUNDLE_IDENTIFIER).sync`),
    /// so it follows the bundle id set in Config/Shared.xcconfig.
    nonisolated static let backgroundSyncTaskIdentifier =
        (Bundle.main.bundleIdentifier ?? "com.example.aifitnesscoach") + ".sync"

    @Published private(set) var signOutError: String?
    let sessionManager: SessionManager
    let authenticationViewModel: AuthenticationViewModel
    let userProfileViewModel: UserProfileViewModel?
    let dashboardViewModel: DashboardViewModel?
    let coachViewModel: CoachViewModel?
    let activityAnalysisViewModel: ActivityAnalysisViewModel?
    let workoutPlanViewModel: WorkoutPlanViewModel?
    let coachInsightsViewModel: CoachInsightsViewModel?
    let goalViewModel: GoalViewModel?
    let nutritionViewModel: NutritionViewModel?
    let progressViewModel: ProgressViewModel?
    let engagementViewModel: EngagementViewModel?
    private(set) var settingsViewModel: SettingsViewModel?
    let appConfig: AppConfigStore
    let consents: ConsentStore
    let configurationError: String?
    let networkMonitor = NetworkMonitor()
    let healthSyncStatus = HealthSyncStatusStore()
    let betaFeedback = BetaFeedbackPrompt()
    let deepLinkRouter = DeepLinkRouter()
    private let healthRepository: HealthKitManager?
    private let conversationStore: ConversationStore?
    private let accountRepository: AccountRepository?
    private let revokeAIConsent: (@MainActor () async throws -> Void)?
    private let crashDiagnostics = CrashDiagnostics()
    private var bootstrapTask: Task<Void, Never>?
    private var hasCompletedBootstrap = false
    private var cancellables: Set<AnyCancellable> = []
    private var notificationResponder: NotificationResponder?
    /// A new session starts at the first foreground and after 30 minutes in the background.
    nonisolated static let sessionTimeout: TimeInterval = 30 * 60
    private var hasStartedSession = false
    private var lastBackgroundAt: Date?

    init() {
        crashDiagnostics.start()
        do {
            let configuration = try APIConfiguration()
            let authenticationService = AuthenticationService(baseURL: configuration.baseURL)
            let session = SessionManager(service: authenticationService)
            let conversationStore = ConversationStore()
            let chatRepository = CoachRepository(
                baseURL: configuration.baseURL,
                sessionManager: session)
            let activityAnalysisRepository = ChatRepository(
                baseURL: configuration.baseURL,
                sessionManager: session)
            let workoutRepository = WorkoutRepository(
                baseURL: configuration.baseURL,
                sessionManager: session)
            let wellnessRepository = WellnessRepository(
                baseURL: configuration.baseURL,
                sessionManager: session)
            let profileRepository = UserProfileRepository(baseURL: configuration.baseURL, sessionManager: session)
            let coachViewModel = CoachViewModel(repository: chatRepository)
            let transport = HealthSyncAPIClient(
                baseURL: configuration.baseURL,
                sessionManager: session)
            let healthKit = try HealthKitManager(
                transport: transport,
                sessionManager: session,
                deviceId: Self.deviceIdentifier())
            sessionManager = session
            authenticationViewModel = AuthenticationViewModel(sessionManager: session)
            userProfileViewModel = UserProfileViewModel(repository: profileRepository)
            dashboardViewModel = DashboardViewModel(
                repository: healthKit,
                cache: FileHealthSummaryCache(),
                userIdProvider: { [weak session] in session?.userId })
            self.coachViewModel = coachViewModel
            activityAnalysisViewModel = ActivityAnalysisViewModel(repository: activityAnalysisRepository)
            workoutPlanViewModel = WorkoutPlanViewModel(repository: workoutRepository)
            coachInsightsViewModel = CoachInsightsViewModel(repository: workoutRepository)
            goalViewModel = GoalViewModel(repository: wellnessRepository)
            nutritionViewModel = NutritionViewModel(repository: wellnessRepository)
            progressViewModel = ProgressViewModel(repository: wellnessRepository)
            engagementViewModel = EngagementViewModel(
                repository: EngagementRepository(baseURL: configuration.baseURL, sessionManager: session),
                notifications: NotificationScheduler())
            healthRepository = healthKit
            self.conversationStore = conversationStore
            appConfig = AppConfigStore(fetcher: AppConfigAPIClient(baseURL: configuration.baseURL))
            let accounts = AccountRepository(baseURL: configuration.baseURL, sessionManager: session)
            accountRepository = accounts
            consents = ConsentStore(repository: accounts)
            revokeAIConsent = { try await chatRepository.setCoachConsent(granted: false) }
            configurationError = nil
            let telemetryClient = TelemetryAPIClient(baseURL: configuration.baseURL, sessionManager: session)
            Task { await AppTelemetry.shared.configure(uploader: telemetryClient) }
        } catch {
            let message = error.localizedDescription
            let session = SessionManager(service: nil, configurationError: message)
            sessionManager = session
            authenticationViewModel = AuthenticationViewModel(sessionManager: session)
            userProfileViewModel = nil
            dashboardViewModel = nil
            coachViewModel = nil
            activityAnalysisViewModel = nil
            workoutPlanViewModel = nil
            coachInsightsViewModel = nil
            goalViewModel = nil
            nutritionViewModel = nil
            progressViewModel = nil
            engagementViewModel = nil
            healthRepository = nil
            conversationStore = nil
            appConfig = AppConfigStore(fetcher: nil)
            accountRepository = nil
            consents = ConsentStore(repository: nil)
            revokeAIConsent = nil
            configurationError = message
        }
        if let accountRepository {
            settingsViewModel = SettingsViewModel(
                repository: accountRepository,
                diagnostics: { [weak self] in await self?.diagnostics() ?? [:] },
                onAccountDeleted: { [weak self] in await self?.handleAccountDeleted() },
                revokeAIConsent: revokeAIConsent,
                consents: consents,
                onHealthDataDeleted: { [weak self] in
                    // Stop observers and drop the local queue so nothing is uploaded again.
                    try? await self?.dashboardViewModel?.clearPendingHealthSync()
                })
        }
        appConfig.objectWillChange
            .sink { [weak self] _ in self?.objectWillChange.send() }
            .store(in: &cancellables)
        // Screens other than the dashboard show the "sync pending" state from this store.
        dashboardViewModel?.$syncOverview.assign(to: &healthSyncStatus.$overview)
        appConfig.refresh(force: true)
        // The root view observes this object only. Without forwarding, finishing a restore or a
        // sign-in would not redraw it and the app could stay on the loading or sign-in screen.
        sessionManager.objectWillChange
            .sink { [weak self] _ in self?.objectWillChange.send() }
            .store(in: &cancellables)
        networkMonitor.setReconnectHandler { [weak self] in
            self?.handleReconnect()
        }
        // Set during launch so a tap on a reminder that launched the app is delivered.
        let responder = NotificationResponder(router: deepLinkRouter)
        notificationResponder = responder
        UNUserNotificationCenter.current().delegate = responder
        AppTelemetry.shared.track("app_launch")
        // Start restoring right away, not when a window appears: HealthKit can launch the app in
        // the background, and observers must be registered during that launch to receive updates.
        Task { [weak self] in await self?.bootstrap() }
    }

    /// Restores the session and resumes HealthKit observers. Safe to call repeatedly.
    func bootstrap() async {
        if let bootstrapTask {
            await bootstrapTask.value
            return
        }
        let task = Task { @MainActor [weak self] in
            guard let self else { return }
            await sessionManager.restoreSession { [weak self] in
                self?.dashboardViewModel?.resumeHealthSync()
            }
            hasCompletedBootstrap = true
            await AppTelemetry.shared.flush()
        }
        bootstrapTask = task
        await task.value
    }

    func handleForeground(now: Date = .now) {
        AppTelemetry.shared.track("app_foreground")
        trackSessionStartIfNeeded(now: now)
        appConfig.refresh(now: now)
        // The first activation happens during launch, while bootstrap already resumes sync.
        let isReturningToForeground = hasCompletedBootstrap
        Task { [weak self] in
            guard let self else { return }
            await bootstrap()
            if sessionManager.needsRestoreRetry {
                await sessionManager.restoreSessionIfNeeded { [weak self] in
                    self?.dashboardViewModel?.resumeHealthSync()
                }
            } else if isReturningToForeground, sessionManager.isAuthenticated {
                dashboardViewModel?.resumeHealthSync()
                // Refreshes today's reminders (a new day, or actions taken on another device).
                engagementViewModel?.load()
            }
            if sessionManager.isAuthenticated {
                betaFeedback.recordActiveDay(isEnabled: appConfig.isEnabled(.betaFeedback))
            }
            await AppTelemetry.shared.flush()
        }
    }

    /// Sessions for product analytics: background launches (HealthKit) never count.
    private func trackSessionStartIfNeeded(now: Date) {
        let isNewSession = !hasStartedSession
            || lastBackgroundAt.map { now.timeIntervalSince($0) >= Self.sessionTimeout } == true
        guard isNewSession else { return }
        AppTelemetry.shared.track("session_start", ["trigger": hasStartedSession ? "foreground" : "launch"])
        hasStartedSession = true
    }

    func handleBackground() {
        lastBackgroundAt = .now
        AppTelemetry.shared.track("app_background")
        scheduleBackgroundSync()
        Task { await AppTelemetry.shared.flush() }
    }

    /// Clears per-user state when the session ends without an explicit sign-out
    /// (refresh token rejected), so the next account never sees the previous one's data.
    func handleSessionEnded() {
        resetFeatureState()
        consents.reset()
        Task { [conversationStore] in await conversationStore?.clear() }
    }

    func signOut() async throws {
        resetFeatureState()
        var failures: [String] = []
        do {
            try await dashboardViewModel?.clearPendingHealthSync()
        } catch {
            failures.append(error.localizedDescription)
        }
        await AppTelemetry.shared.flush()
        do {
            try await sessionManager.signOut()
        } catch {
            failures.append(error.localizedDescription)
        }
        await AppTelemetry.shared.clear()
        await conversationStore?.clear()
        AccountRepository.removeExports()
        betaFeedback.reset()
        consents.reset()
        guard failures.isEmpty else {
            signOutError = failures.joined(separator: "\n")
            throw SignOutError.failed
        }
        signOutError = nil
    }

    /// The server already removed the account: clear everything stored on this device for it
    /// (session, pending HealthKit uploads and observers, caches, telemetry, conversations).
    func handleAccountDeleted() async {
        resetFeatureState()
        do {
            try await dashboardViewModel?.clearPendingHealthSync()
        } catch {
            NSLog("Local health sync cleanup after account deletion failed: %@", error.localizedDescription)
        }
        await AppTelemetry.shared.clear()
        await conversationStore?.clear()
        AccountRepository.removeExports()
        betaFeedback.reset()
        consents.reset()
        sessionManager.endDeletedAccount()
        signOutError = nil
    }

    /// Technical context for bug reports (see `DiagnosticsSnapshot`).
    func diagnostics() async -> [String: String] {
        let connection: String
        switch dashboardViewModel?.connectionState {
        case .some(.connected): connection = "connected"
        case .some(.notConnected): connection = "not_connected"
        case .some(.unavailable): connection = "unavailable"
        case .none: connection = "unknown"
        }
        return await DiagnosticsSnapshot.collect(
            isOnline: networkMonitor.isOnline,
            healthConnection: connection,
            syncOverview: dashboardViewModel?.syncOverview ?? .idle,
            config: appConfig.config)
    }

    /// Uploads queued HealthKit changes from a background refresh task.
    func performBackgroundSync() async {
        scheduleBackgroundSync()
        await bootstrap()
        guard sessionManager.isAuthenticated, let healthRepository else { return }
        var outcome = "ok"
        do {
            try await healthRepository.retryPendingSync()
        } catch {
            outcome = error.isOffline ? "offline" : "failed"
        }
        AppTelemetry.shared.track("sync_background_refresh", ["outcome": outcome])
        await AppTelemetry.shared.flush()
    }

    func scheduleBackgroundSync() {
        let request = BGAppRefreshTaskRequest(identifier: Self.backgroundSyncTaskIdentifier)
        request.earliestBeginDate = Date(timeIntervalSinceNow: 60 * 60)
        do {
            try BGTaskScheduler.shared.submit(request)
        } catch {
            // Simulator and devices with Background App Refresh disabled reject requests.
            NSLog("Background sync scheduling skipped: %@", error.localizedDescription)
        }
    }

    private func handleReconnect() {
        guard sessionManager.isAuthenticated else { return }
        dashboardViewModel?.resumePendingUploads()
        Task { await AppTelemetry.shared.flush() }
    }

    private func resetFeatureState() {
        userProfileViewModel?.resetForSignOut()
        coachViewModel?.resetForSignOut()
        activityAnalysisViewModel?.resetForSignOut()
        workoutPlanViewModel?.resetForSignOut()
        coachInsightsViewModel?.resetForSignOut()
        goalViewModel?.resetForSignOut()
        nutritionViewModel?.resetForSignOut()
        progressViewModel?.resetForSignOut()
        engagementViewModel?.resetForSignOut()
        dashboardViewModel?.resetForSignOut()
    }

    /// The Keychain can be locked when HealthKit wakes the app before the first unlock after a
    /// reboot. The vendor identifier is stable for this install and safe as a fallback: the
    /// server keys samples by source and HealthKit UUID, not by device.
    private static func deviceIdentifier() -> String {
        if let stored = try? KeychainManager().deviceIdentifier() {
            return stored
        }
        return UIDevice.current.identifierForVendor?.uuidString ?? "unknown-device"
    }
}

@main
struct AIFitnessCoachApp: App {
    @StateObject private var dependencies = AppDependencies()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            Group {
                if let error = dependencies.configurationError {
                    ContentUnavailableView(
                        "Backend no configurado",
                        systemImage: "network.slash",
                        description: Text(error))
                } else if dependencies.sessionManager.isRestoring {
                    ProgressView("Restaurando sesión...")
                } else if dependencies.sessionManager.isAuthenticated,
                          let dashboardViewModel = dependencies.dashboardViewModel,
                          let userProfileViewModel = dependencies.userProfileViewModel {
                    AuthenticatedRootView(
                        profileViewModel: userProfileViewModel,
                        userId: dependencies.sessionManager.userId,
                        dashboard: DashboardView(
                        viewModel: dashboardViewModel,
                        coachViewModel: dependencies.coachViewModel,
                        activityAnalysisViewModel: dependencies.activityAnalysisViewModel,
                        workoutPlanViewModel: dependencies.workoutPlanViewModel,
                        coachInsightsViewModel: dependencies.coachInsightsViewModel,
                        goalViewModel: dependencies.goalViewModel,
                        nutritionViewModel: dependencies.nutritionViewModel,
                        progressViewModel: dependencies.progressViewModel,
                        engagementViewModel: dependencies.engagementViewModel,
                        sessionManager: dependencies.sessionManager,
                        onSignOut: { try await dependencies.signOut() },
                        settingsViewModel: dependencies.settingsViewModel))
                } else {
                    AuthenticationView(
                        viewModel: dependencies.authenticationViewModel,
                        sessionError: dependencies.signOutError
                            ?? dependencies.sessionManager.errorMessage)
                }
            }
            // Brand look (Core/Theme.swift): always dark, lime accents like the logo.
            .preferredColorScheme(.dark)
            .tint(Brand.lime)
            .environmentObject(dependencies.networkMonitor)
            .environmentObject(dependencies.deepLinkRouter)
            .environmentObject(dependencies.appConfig)
            .environmentObject(dependencies.healthSyncStatus)
            .environmentObject(dependencies.betaFeedback)
            .environmentObject(dependencies.consents)
            .task {
                await dependencies.bootstrap()
            }
            .onOpenURL { url in
                dependencies.deepLinkRouter.handle(url)
            }
            .onChange(of: dependencies.sessionManager.userId) { previous, userId in
                if let userId {
                    dependencies.userProfileViewModel?.load(userId: userId)
                    Task { await dependencies.consents.load() }
                } else if previous != nil {
                    dependencies.handleSessionEnded()
                }
            }
            .onChange(of: scenePhase) { _, phase in
                switch phase {
                case .active:
                    dependencies.handleForeground()
                case .background:
                    dependencies.handleBackground()
                default:
                    break
                }
            }
        }
        .backgroundTask(.appRefresh(AppDependencies.backgroundSyncTaskIdentifier)) { @MainActor in
            await dependencies.performBackgroundSync()
        }
    }
}
