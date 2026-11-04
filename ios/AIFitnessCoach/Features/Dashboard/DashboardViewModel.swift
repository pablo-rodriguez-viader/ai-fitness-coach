import Combine
import Foundation

@MainActor
final class DashboardViewModel: ObservableObject {
    @Published private(set) var summary = HealthSummary.empty
    @Published private(set) var connectionState: HealthConnectionState
    @Published private(set) var permissionState: HealthPermissionState = .notRequested
    @Published private(set) var summaryState: HealthSummaryState = .notLoaded
    /// When the shown summary was fetched; set for both live and cached data.
    @Published private(set) var lastUpdatedAt: Date?
    /// True when the server could not be reached and the summary comes from the local cache.
    @Published private(set) var isShowingCachedData = false
    @Published private(set) var syncOverview = HealthSyncOverview.idle

    private let repository: HealthDataRepository
    private let cache: HealthSummaryCaching?
    private let userIdProvider: (@MainActor () -> String?)?
    private var connectionTask: Task<Void, Never>?
    private var refreshTask: Task<Void, Never>?

    init(
        repository: HealthDataRepository,
        cache: HealthSummaryCaching? = nil,
        userIdProvider: (@MainActor () -> String?)? = nil
    ) {
        self.repository = repository
        self.cache = cache
        self.userIdProvider = userIdProvider
        connectionState = repository.isAvailable ? .notConnected : .unavailable
        if !repository.isAvailable {
            permissionState = .unavailable
        }
    }

    func connectHealth() {
        guard repository.isAvailable else {
            connectionState = .unavailable
            permissionState = .unavailable
            return
        }

        connectionTask?.cancel()
        refreshTask?.cancel()
        permissionState = .requesting
        summaryState = .loading

        let repository = self.repository
        connectionTask = Task { [weak self] in
            do {
                try await repository.requestReadAuthorization()
                try Task.checkCancellation()
                guard let self else { return }

                permissionState = .requestCompleted
                connectionState = .connected
                AppTelemetry.shared.track("health_connected")
                do {
                    try await repository.enableAutomaticSync { [weak self] in
                        await self?.refreshFromObserver()
                    }
                } catch let error where DashboardViewModel.isQueuedForRetry(error) {
                    // Samples are saved locally and upload once the connection returns.
                }
                await loadSummary()
            } catch {
                guard let self else { return }
                if error.isCancellation {
                    if permissionState == .requesting {
                        permissionState = .notRequested
                    }
                    if summaryState == .loading {
                        summaryState = .notLoaded
                    }
                    return
                }
                guard !Task.isCancelled else { return }
                error.track(area: "dashboard_connect")
                summaryState = .failed(error.userMessage)
            }
        }
    }

    func refresh() {
        guard connectionState == .connected else { return }
        refreshTask?.cancel()
        summaryState = .loading
        refreshTask = Task { [weak self] () -> Void in
            await self?.loadSummary()
        }
    }

    func retryHealthConnection() {
        if connectionState == .connected {
            refreshTask?.cancel()
            summaryState = .loading
            refreshTask = Task { [weak self] in
                guard let self else { return }
                do {
                    try await repository.retryPendingSync()
                } catch let error where DashboardViewModel.isQueuedForRetry(error) {
                    // Still offline or the server is busy: the queue keeps the samples.
                } catch {
                    if !error.isCancellation {
                        error.track(area: "dashboard_retry")
                    }
                }
                await loadSummary()
            }
        } else {
            connectHealth()
        }
    }

    /// Resumes uploads after the connection returns, without blocking the screen.
    func resumePendingUploads() {
        guard connectionState == .connected else { return }
        Task { [weak self] in
            guard let self else { return }
            try? await repository.retryPendingSync()
            await refreshSyncOverview()
            if summaryState != .loading {
                await loadSummary()
            }
        }
    }

    func resumeHealthSync() {
        guard repository.isAvailable else { return }
        connectionTask?.cancel()
        showCachedSummaryIfEmpty()
        let repository = self.repository
        connectionTask = Task { [weak self] in
            var resumed = false
            do {
                resumed = try await repository.resumeAutomaticSync { [weak self] in
                    await self?.refreshFromObserver()
                }
            } catch let error where DashboardViewModel.isQueuedForRetry(error) {
                // Observers are registered; only the upload is waiting for the network.
                resumed = true
            } catch {
                guard let self, !Task.isCancelled, !error.isCancellation else { return }
                error.track(area: "dashboard_resume")
                summaryState = .failed(error.userMessage)
                return
            }
            guard let self, !Task.isCancelled else { return }
            guard resumed else { return }
            connectionState = .connected
            permissionState = .requestCompleted
            await loadSummary()
        }
    }

    func cancelPendingWork() {
        connectionTask?.cancel()
        refreshTask?.cancel()
        connectionTask = nil
        refreshTask = nil
        if permissionState == .requesting {
            permissionState = .notRequested
        }
        if summaryState == .loading {
            summaryState = .notLoaded
        }
    }

    func clearPendingHealthSync() async throws {
        try await repository.clearPendingSync()
        resetForSignOut()
    }

    /// Clears everything shown for the previous user, including the cached summary.
    func resetForSignOut() {
        connectionTask?.cancel()
        refreshTask?.cancel()
        connectionTask = nil
        refreshTask = nil
        cache?.clear()
        connectionState = repository.isAvailable ? .notConnected : .unavailable
        permissionState = repository.isAvailable ? .notRequested : .unavailable
        summary = .empty
        summaryState = .notLoaded
        lastUpdatedAt = nil
        isShowingCachedData = false
        syncOverview = .idle
    }

    func refreshSyncOverview() async {
        syncOverview = await repository.syncOverview()
    }

    private func refreshFromObserver() async {
        refreshTask?.cancel()
        // `self?.` makes the closure return `()?`; the stored task is `Task<Void, Never>`.
        let task = Task { [weak self] () -> Void in
            guard let self else { return }
            await self.loadSummary()
        }
        refreshTask = task
        await task.value
    }

    private func loadSummary() async {
        // Keep showing data already on screen (live or cached) while the refresh runs.
        if !summary.hasMetrics {
            summaryState = .loading
        }
        do {
            let latestSummary = try await repository.fetchTodaySummary()
            try Task.checkCancellation()
            let now = Date.now
            summary = latestSummary
            summaryState = latestSummary.hasMetrics ? .available : .noData
            lastUpdatedAt = now
            isShowingCachedData = false
            if let userId = userIdProvider?() {
                cache?.save(latestSummary, userId: userId, fetchedAt: now)
            }
        } catch {
            guard !error.isCancellation, !Task.isCancelled else { return }
            if error.isOffline, showCachedSummary() {
                // Cached data is on screen with its timestamp; nothing else to do.
            } else {
                error.track(area: "dashboard_summary")
                summaryState = .failed(error.userMessage)
            }
        }
        await refreshSyncOverview()
    }

    private func showCachedSummaryIfEmpty() {
        guard !summary.hasMetrics else { return }
        _ = showCachedSummary()
    }

    @discardableResult
    private func showCachedSummary() -> Bool {
        guard let userId = userIdProvider?(),
              let cached = cache?.load(userId: userId),
              cached.summary.hasMetrics
        else { return false }
        summary = cached.summary
        summaryState = .available
        lastUpdatedAt = cached.fetchedAt
        isShowingCachedData = true
        return true
    }

    /// Errors after which the samples are already queued locally and will upload later.
    private static func isQueuedForRetry(_ error: Error) -> Bool {
        if let syncError = error as? HealthSyncError {
            return syncError.isRetryable
        }
        return error.isOffline
    }
}
