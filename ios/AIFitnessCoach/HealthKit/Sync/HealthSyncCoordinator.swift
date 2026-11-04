import Foundation
import os

protocol HealthSyncTransport: Sendable {
    func upload(
        _ items: [HealthSyncJob],
        deletions: [HealthSyncDeletion]
    ) async throws -> HealthSyncResult
    func fetchSummary(from: Date, to: Date) async throws -> HealthSummary
}

actor HealthSyncCoordinator {
    private static let logger = Logger(
        subsystem: Bundle.main.bundleIdentifier ?? "AIFitnessCoach",
        category: "HealthSync")
    /// Samples and deletions per request. The server accepts at most 500 changes per batch,
    /// so 250 + 250 always fits.
    static let batchSize = 250
    /// A permanently rejected batch waits this long before being tried again.
    static let rejectedRetryDelay: TimeInterval = 6 * 60 * 60
    /// After this many permanent rejections a change is dropped so it cannot block the queue.
    static let maximumRejections = 4

    private let queue: any HealthSyncQueue
    private let transport: any HealthSyncTransport
    private let deviceId: String
    private(set) var state: HealthSyncState = .idle
    private(set) var metrics = HealthSyncMetricsSnapshot()
    private var isSynchronizing = false
    private var retryTask: Task<Void, Never>?
    private var scheduledRetryUserId: String?

    init(queue: any HealthSyncQueue, transport: any HealthSyncTransport, deviceId: String = "legacy-device") {
        self.queue = queue
        self.transport = transport
        self.deviceId = deviceId
    }

    func enqueue(
        _ items: [HealthSyncItem],
        ownerUserId: String,
        cursorUpdates: [String: Data],
        deletions: [HealthSyncDeletion] = []
    ) async throws {
        guard items.allSatisfy({ $0.userId == ownerUserId }) else {
            throw HealthSyncTransportError.accountMismatch
        }
        guard items.allSatisfy({
            $0.value.isFinite
                && $0.value >= 0
                && $0.unit == $0.metricType.unit
                && !$0.source.isEmpty
                && !$0.deviceId.isEmpty
        }) else {
            throw HealthSyncError.invalidMetric
        }
        guard deletions.allSatisfy({ $0.userId == ownerUserId }) else {
            throw HealthSyncTransportError.accountMismatch
        }
        let namespacedCursors = Dictionary(
            uniqueKeysWithValues: cursorUpdates.map { ("\(ownerUserId)|\($0.key)", $0.value) })
        try await queue.enqueue(items, deletions: deletions, cursorUpdates: namespacedCursors)
        // While a sync loop runs it owns `state`; the loop picks up the new items itself.
        guard !isSynchronizing else { return }
        let queuedCount = try await queue.count(ownerUserId: ownerUserId)
        state = queuedCount == 0 ? .idle : .queued(queuedCount)
    }

    func synchronizePending(ownerUserId: String) async throws {
        // Actor methods can interleave at every `await`, so `state` alone cannot guard this.
        guard !isSynchronizing else {
            throw HealthSyncCoordinatorError.alreadySyncing
        }
        isSynchronizing = true
        defer { isSynchronizing = false }
        state = .syncing(0)
        var permanentFailure: Error?

        do {
            while true {
                let batch = try await queue.nextBatch(
                    maximumCount: Self.batchSize,
                    ownerUserId: ownerUserId,
                    now: .now)
                let deletionBatch = try await queue.nextDeletionBatch(
                    maximumCount: Self.batchSize,
                    ownerUserId: ownerUserId,
                    now: .now)
                guard !batch.isEmpty || !deletionBatch.isEmpty else {
                    let remaining = try await queue.count(ownerUserId: ownerUserId)
                    if let permanentFailure {
                        state = .failed(permanentFailure.localizedDescription)
                        throw permanentFailure
                    }
                    if remaining == 0 {
                        state = .idle
                        metrics.lastSuccessAt = Date.now
                    } else {
                        state = .retrying(remaining)
                        scheduleRetry(ownerUserId: ownerUserId)
                    }
                    return
                }

                guard batch.allSatisfy({ $0.userId == ownerUserId }) else {
                    throw HealthSyncTransportError.accountMismatch
                }
                guard deletionBatch.allSatisfy({ $0.userId == ownerUserId }) else {
                    throw HealthSyncTransportError.accountMismatch
                }
                state = .syncing(batch.count + deletionBatch.count)
                do {
                    let result = try await transport.upload(batch, deletions: deletionBatch)
                    metrics.batches += 1
                    metrics.metricsReceived += result.receivedCount
                    metrics.metricsInserted += result.insertedCount
                    metrics.metricsUpdated += result.updatedCount
                    metrics.metricsDeduplicated += result.duplicateCount
                    metrics.metricsDeleted += result.deletedCount
                    metrics.metricsRejected += result.rejectedCount
                } catch {
                    metrics.errors += 1
                    metrics.lastErrorAt = Date.now
                    let syncError = error as? HealthSyncError
                    guard syncError?.isRetryable ?? true else {
                        if Self.stopsSynchronization(syncError) {
                            // Session or account problems: keep everything queued for the next sign-in.
                            cancelScheduledRetry(for: ownerUserId)
                            state = .failed(error.localizedDescription)
                            Self.logger.error(
                                "Health sync stopped; changes remain queued: \(error.localizedDescription, privacy: .private)")
                            throw error
                        }
                        try await park(batch, deletionBatch, ownerUserId: ownerUserId, error: error)
                        permanentFailure = error
                        continue
                    }
                    let now = Date.now
                    let retryJobs = batch.map { job in
                        let delay = min(21_600, pow(2, Double(min(job.attemptCount, 14))))
                        return job.retrying(after: delay, error: error.localizedDescription, now: now)
                    }
                    let retryDeletions = deletionBatch.map { deletion in
                        let delay = min(21_600, pow(2, Double(min(deletion.attemptCount, 14))))
                        return deletion.retrying(after: delay, error: error.localizedDescription, now: now)
                    }
                    try await queue.markRetry(retryJobs)
                    try await queue.markDeletionRetry(retryDeletions)
                    metrics.retries += retryJobs.count + retryDeletions.count
                    let count = try await queue.count(ownerUserId: ownerUserId)
                    if case .some(.offline) = syncError {
                        state = .offline
                    } else {
                        state = .retrying(count)
                    }
                    Self.logger.error(
                        "Health sync batch failed; persisted \(retryJobs.count) retries: \(error.localizedDescription, privacy: .private)")
                    scheduleRetry(ownerUserId: ownerUserId)
                    throw error
                }
                try await queue.remove(
                    ids: Set(batch.map(\.id)),
                    ownerUserId: ownerUserId)
                try await queue.removeDeletions(
                    ids: Set(deletionBatch.map(\.id)),
                    ownerUserId: ownerUserId)
                let remaining = try await queue.count(ownerUserId: ownerUserId)
                state = remaining == 0 ? .idle : .queued(remaining)
                if remaining == 0 {
                    cancelScheduledRetry(for: ownerUserId)
                }
            }
        } catch is CancellationError {
            let remaining = try await queue.count(ownerUserId: ownerUserId)
            state = remaining == 0 ? .idle : .queued(remaining)
            throw CancellationError()
        } catch {
            if case .offline = state {
                throw error
            }
            if case .retrying = state {
                throw error
            }
            state = .failed(error.localizedDescription)
            throw error
        }
    }

    func clearPending(ownerUserId: String) async throws {
        try await queue.clear(ownerUserId: ownerUserId)
        cancelScheduledRetry(for: ownerUserId)
        state = .idle
    }

    func cursorData(ownerUserId: String, typeIdentifier: String) async throws -> Data? {
        try await queue.cursorData(for: "\(ownerUserId)|\(typeIdentifier)")
    }

    func fetchSummary(from: Date, to: Date) async throws -> HealthSummary {
        try await transport.fetchSummary(from: from, to: to)
    }

    func pendingCount(ownerUserId: String) async -> Int {
        (try? await queue.count(ownerUserId: ownerUserId)) ?? 0
    }

    private static func stopsSynchronization(_ error: HealthSyncError?) -> Bool {
        guard let error else { return false }
        switch error {
        case .unauthorized, .accountMismatch:
            return true
        case let .server(statusCode):
            // 403: token and batch owner disagree, or no health-data consent. 404: wrong base URL.
            // Neither is the data's fault.
            return statusCode == 403 || statusCode == 404
        default:
            return false
        }
    }

    private func park(
        _ jobs: [HealthSyncJob],
        _ deletions: [HealthSyncDeletion],
        ownerUserId: String,
        error: Error
    ) async throws {
        let now = Date.now
        let description = error.localizedDescription
        let droppedJobs = jobs.filter { $0.rejectionCount + 1 >= Self.maximumRejections }
        let droppedDeletions = deletions.filter { $0.rejections + 1 >= Self.maximumRejections }
        let parkedJobs = jobs
            .filter { $0.rejectionCount + 1 < Self.maximumRejections }
            .map { $0.parked(after: Self.rejectedRetryDelay, error: description, now: now) }
        let parkedDeletions = deletions
            .filter { $0.rejections + 1 < Self.maximumRejections }
            .map { $0.parked(after: Self.rejectedRetryDelay, error: description, now: now) }
        try await queue.markRetry(parkedJobs)
        try await queue.markDeletionRetry(parkedDeletions)
        if !droppedJobs.isEmpty {
            try await queue.remove(ids: Set(droppedJobs.map(\.id)), ownerUserId: ownerUserId)
        }
        if !droppedDeletions.isEmpty {
            try await queue.removeDeletions(ids: Set(droppedDeletions.map(\.id)), ownerUserId: ownerUserId)
        }
        metrics.parked += parkedJobs.count + parkedDeletions.count
        metrics.deadLettered += droppedJobs.count + droppedDeletions.count
        Self.logger.error(
            "Health sync batch rejected: parked \(parkedJobs.count + parkedDeletions.count), dropped \(droppedJobs.count + droppedDeletions.count): \(description, privacy: .private)")
    }

    private func cancelScheduledRetry(for ownerUserId: String) {
        guard scheduledRetryUserId == ownerUserId else { return }
        retryTask?.cancel()
        retryTask = nil
        scheduledRetryUserId = nil
    }

    private func scheduleRetry(ownerUserId: String) {
        guard scheduledRetryUserId != ownerUserId || retryTask == nil else { return }
        retryTask?.cancel()
        scheduledRetryUserId = ownerUserId
        retryTask = Task { [weak self] in
            do {
                guard let self else { return }
                let retryDate = try await self.queue.nextRetryDate(ownerUserId: ownerUserId)
                let delay = max(0, retryDate?.timeIntervalSinceNow ?? 30)
                try await Task.sleep(for: .seconds(delay))
                await self.clearRetrySchedule(for: ownerUserId)
                try await self.synchronizePending(ownerUserId: ownerUserId)
            } catch is CancellationError {
                return
            } catch HealthSyncCoordinatorError.alreadySyncing {
                return
            } catch {
                Self.logger.error("Scheduled HealthKit retry failed: \(error.localizedDescription, privacy: .public)")
            }
        }
    }

    private func clearRetrySchedule(for ownerUserId: String) {
        guard scheduledRetryUserId == ownerUserId else { return }
        retryTask = nil
        scheduledRetryUserId = nil
    }
}

struct HealthSyncMetricsSnapshot: Equatable, Sendable {
    var batches = 0
    var metricsReceived = 0
    var metricsInserted = 0
    var metricsUpdated = 0
    var metricsDeduplicated = 0
    var metricsDeleted = 0
    var metricsRejected = 0
    var errors = 0
    var retries = 0
    var parked = 0
    var deadLettered = 0
    var lastSuccessAt: Date?
    var lastErrorAt: Date?
}

/// Pending-change count plus the coordinator's state, for the dashboard.
struct HealthSyncOverview: Equatable, Sendable {
    var state: HealthSyncState
    var pendingCount: Int
    var lastSuccessAt: Date?

    static let idle = HealthSyncOverview(state: .idle, pendingCount: 0, lastSuccessAt: nil)
}

@MainActor
final class HealthSyncWorker {
    private let coordinator: HealthSyncCoordinator
    private let sessionManager: SessionManager
    private let deviceId: String

    init(coordinator: HealthSyncCoordinator, sessionManager: SessionManager, deviceId: String) {
        self.coordinator = coordinator
        self.sessionManager = sessionManager
        self.deviceId = deviceId
    }

    func enqueueAndUpload(
        _ items: [HealthSyncJob],
        deletions: [HealthSyncDeletion] = [],
        cursorUpdates: [String: Data],
        for expectedUserId: String
    ) async throws {
        guard sessionManager.userId == expectedUserId else {
            throw HealthSyncTransportError.accountMismatch
        }
        try await coordinator.enqueue(
            items.map { $0.owned(by: expectedUserId, deviceId: deviceId) },
            ownerUserId: expectedUserId,
            cursorUpdates: cursorUpdates,
            deletions: deletions)
        try await synchronize(ownerUserId: expectedUserId)
    }

    func retryPending() async throws {
        guard let userId = sessionManager.userId else {
            throw AuthenticationError.missingTokens
        }
        try await synchronize(ownerUserId: userId)
    }

    func clearPending() async throws {
        guard let userId = sessionManager.userId else {
            throw AuthenticationError.missingTokens
        }
        try await coordinator.clearPending(ownerUserId: userId)
    }

    func anchorData(for typeIdentifier: String, userId: String) async throws -> Data? {
        guard sessionManager.userId == userId else {
            throw HealthSyncTransportError.accountMismatch
        }
        return try await coordinator.cursorData(
            ownerUserId: userId,
            typeIdentifier: typeIdentifier)
    }

    func currentUserId() throws -> String {
        guard let userId = sessionManager.userId else {
            throw AuthenticationError.missingTokens
        }
        return userId
    }

    func currentState() async -> HealthSyncState {
        await coordinator.state
    }

    func overview() async -> HealthSyncOverview {
        guard let userId = sessionManager.userId else { return .idle }
        let state = await coordinator.state
        let pending = await coordinator.pendingCount(ownerUserId: userId)
        let metrics = await coordinator.metrics
        return HealthSyncOverview(state: state, pendingCount: pending, lastSuccessAt: metrics.lastSuccessAt)
    }

    func metricsSnapshot() async -> HealthSyncMetricsSnapshot {
        await coordinator.metrics
    }

    func fetchSummary(from: Date, to: Date) async throws -> HealthSummary {
        try await coordinator.fetchSummary(from: from, to: to)
    }

    private func synchronize(ownerUserId: String) async throws {
        do {
            try await coordinator.synchronizePending(ownerUserId: ownerUserId)
            await reportSyncProgress()
        } catch HealthSyncCoordinatorError.alreadySyncing {
            // The running loop drains the queue, including anything enqueued just now.
        } catch {
            await reportSyncProgress()
            if !error.isCancellation {
                let pending = await coordinator.pendingCount(ownerUserId: ownerUserId)
                AppTelemetry.shared.track("sync_failed", [
                    "reason": Self.reason(for: error),
                    "pending": String(pending)
                ])
            }
            throw error
        }
    }

    private var reportedBatches = 0
    private var reportedDeadLettered = 0

    /// Sends counters only (never sample values) when something was uploaded or dropped.
    private func reportSyncProgress() async {
        let metrics = await coordinator.metrics
        if metrics.batches > reportedBatches {
            AppTelemetry.shared.track("sync_completed", [
                "batches": String(metrics.batches - reportedBatches),
                "received_total": String(metrics.metricsReceived),
                "rejected_total": String(metrics.metricsRejected),
                "retries_total": String(metrics.retries)
            ])
            reportedBatches = metrics.batches
        }
        if metrics.deadLettered > reportedDeadLettered {
            AppTelemetry.shared.track("sync_dead_lettered", [
                "count": String(metrics.deadLettered - reportedDeadLettered)
            ])
            reportedDeadLettered = metrics.deadLettered
        }
    }

    private static func reason(for error: Error) -> String {
        switch error as? HealthSyncError {
        case .some(.offline): "offline"
        case .some(.unauthorized): "unauthorized"
        case let .some(.server(statusCode)): "http_\(statusCode)"
        case .some(.accountMismatch): "account_mismatch"
        case .some(.invalidMetric): "invalid_metric"
        case .some(.backgroundDeliveryRejected): "background_delivery"
        case .none: "other"
        }
    }
}
