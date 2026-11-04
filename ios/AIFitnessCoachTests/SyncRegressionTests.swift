import XCTest
@testable import AIFitnessCoach

/// Regression tests for the sync problems found while preparing the private beta.
final class SyncRegressionTests: XCTestCase {
    // Regression: one rejected batch blocked every newer sample forever.
    func testRejectedBatchDoesNotBlockNewerChanges() async throws {
        let queue = MemorySyncQueue()
        let transport = ScriptedTransport(failures: [.server(statusCode: 400)])
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        let rejected = job(userId: "user-1")
        try await coordinator.enqueue([rejected], ownerUserId: "user-1", cursorUpdates: [:])
        do {
            try await coordinator.synchronizePending(ownerUserId: "user-1")
            XCTFail("Expected the first batch to be rejected.")
        } catch HealthSyncError.server(statusCode: 400) {}

        let newer = job(userId: "user-1")
        try await coordinator.enqueue([newer], ownerUserId: "user-1", cursorUpdates: [:])
        try await coordinator.synchronizePending(ownerUserId: "user-1")

        let uploaded = await transport.uploadedIds
        let remaining = try await queue.count(ownerUserId: "user-1")
        XCTAssertEqual(uploaded, [newer.id])
        XCTAssertEqual(remaining, 1, "The rejected sample stays queued for a later retry.")
    }

    func testChangeIsDroppedAfterRepeatedRejections() async throws {
        let queue = MemorySyncQueue()
        let transport = ScriptedTransport(failures: [.server(statusCode: 422)])
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        var almostExpired = job(userId: "user-1")
        almostExpired.rejectionCount = HealthSyncCoordinator.maximumRejections - 1
        try await coordinator.enqueue([almostExpired], ownerUserId: "user-1", cursorUpdates: [:])

        do {
            try await coordinator.synchronizePending(ownerUserId: "user-1")
            XCTFail("Expected a rejection.")
        } catch HealthSyncError.server(statusCode: 422) {}

        let remaining = try await queue.count(ownerUserId: "user-1")
        let metrics = await coordinator.metrics
        XCTAssertEqual(remaining, 0)
        XCTAssertEqual(metrics.deadLettered, 1)
    }

    func testOfflineRetriesNeverCountTowardsDropping() async throws {
        let queue = MemorySyncQueue()
        let transport = ScriptedTransport(failures: [.offline])
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        var retriedOften = job(userId: "user-1")
        retriedOften.attemptCount = 40
        try await coordinator.enqueue([retriedOften], ownerUserId: "user-1", cursorUpdates: [:])

        do {
            try await coordinator.synchronizePending(ownerUserId: "user-1")
        } catch HealthSyncError.offline {}

        let remaining = try await queue.count(ownerUserId: "user-1")
        let metrics = await coordinator.metrics
        XCTAssertEqual(remaining, 1)
        XCTAssertEqual(metrics.deadLettered, 0)
    }

    func testExpiredSessionStopsSyncAndKeepsChangesDue() async throws {
        let queue = MemorySyncQueue()
        let transport = ScriptedTransport(failures: [.unauthorized])
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        let item = job(userId: "user-1")
        try await coordinator.enqueue([item], ownerUserId: "user-1", cursorUpdates: [:])

        do {
            try await coordinator.synchronizePending(ownerUserId: "user-1")
            XCTFail("Expected unauthorized.")
        } catch HealthSyncError.unauthorized {}

        let due = try await queue.nextBatch(maximumCount: 10, ownerUserId: "user-1", now: .now)
        XCTAssertEqual(due.map(\.id), [item.id], "Nothing is parked or dropped for a session problem.")
    }

    // Regression: six HealthKit observers fire together; `enqueue` overwrote `.syncing`, so a
    // second loop started and both uploaded the same batch.
    func testConcurrentSyncIsRejectedAndEnqueueKeepsSyncingState() async throws {
        let queue = MemorySyncQueue()
        let transport = GatedTransport()
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        try await coordinator.enqueue([job(userId: "user-1")], ownerUserId: "user-1", cursorUpdates: [:])

        let firstSync = Task { try await coordinator.synchronizePending(ownerUserId: "user-1") }
        await transport.waitUntilUploadStarts()

        let lateItem = job(userId: "user-1")
        try await coordinator.enqueue([lateItem], ownerUserId: "user-1", cursorUpdates: [:])
        let stateDuringUpload = await coordinator.state
        do {
            try await coordinator.synchronizePending(ownerUserId: "user-1")
            XCTFail("A second loop must not start while one is running.")
        } catch HealthSyncCoordinatorError.alreadySyncing {}

        await transport.release()
        try await firstSync.value

        let uploads = await transport.uploadCount
        let remaining = try await queue.count(ownerUserId: "user-1")
        XCTAssertEqual(stateDuringUpload, .syncing(1))
        XCTAssertEqual(uploads, 2, "The running loop also drains the item enqueued meanwhile.")
        XCTAssertEqual(remaining, 0)
    }

    func testBatchesFitTheServerLimitOfFiveHundredChanges() {
        XCTAssertLessThanOrEqual(HealthSyncCoordinator.batchSize * 2, 500)
    }

    // Queue files written by the previous build have no `rejectionCount`.
    func testQueueFromPreviousBuildStillLoads() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appending(path: UUID().uuidString, directoryHint: .isDirectory)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let fileURL = directory.appending(path: "queue.json")
        let itemId = UUID()
        let deletionId = UUID()
        let legacy = """
        {"items":[{"id":"\(itemId.uuidString)","metricType":"steps","value":10,"unit":"count",\
        "source":"apple_health","timestamp":"2026-10-20T10:00:00Z","deviceId":"d","userId":"user-1",\
        "attemptCount":2}],
        "deletions":[{"id":"\(deletionId.uuidString)","userId":"user-1","source":"apple_health",\
        "externalId":"x","deviceId":"d","attemptCount":0}],
        "cursors":{}}
        """
        try Data(legacy.utf8).write(to: fileURL)

        let queue = try FileHealthSyncQueue(fileURL: fileURL)
        let items = try await queue.nextBatch(maximumCount: 10, ownerUserId: "user-1", now: .now)
        let deletions = try await queue.nextDeletionBatch(maximumCount: 10, ownerUserId: "user-1", now: .now)

        XCTAssertEqual(items.map(\.id), [itemId])
        XCTAssertEqual(items.first?.rejectionCount, 0)
        XCTAssertEqual(deletions.map(\.id), [deletionId])
        XCTAssertEqual(deletions.first?.rejections, 0)
    }

    func testSyncResultDecodesWithAndWithoutRejectedCount() throws {
        let withCount = Data(#"{"receivedCount":5,"insertedCount":4,"updatedCount":0,"duplicateCount":0,"deletedCount":0,"rejectedCount":1}"#.utf8)
        let olderServer = Data(#"{"receivedCount":5,"insertedCount":5,"updatedCount":0,"duplicateCount":0}"#.utf8)

        let current = try JSONDecoder().decode(HealthSyncResult.self, from: withCount)
        let legacy = try JSONDecoder().decode(HealthSyncResult.self, from: olderServer)

        XCTAssertEqual(current.rejectedCount, 1)
        XCTAssertEqual(legacy.rejectedCount, 0)
        XCTAssertEqual(legacy.deletedCount, 0)
    }

    private func job(userId: String) -> HealthSyncJob {
        HealthSyncJob(
            id: UUID(),
            metric: "steps",
            value: 100,
            unit: "count",
            observedAt: Date(),
            ownerUserId: userId,
            deviceId: "device-1")
    }
}

/// In-memory queue with the same filtering rules as the file queue.
actor MemorySyncQueue: HealthSyncQueue {
    private var items: [HealthSyncJob] = []
    private var deletions: [HealthSyncDeletion] = []
    private var cursors: [String: Data] = [:]

    func enqueue(_ incoming: [HealthSyncJob], deletions incomingDeletions: [HealthSyncDeletion], cursorUpdates: [String: Data]) {
        let known = Set(items.map(\.id))
        items += incoming.filter { !known.contains($0.id) }
        deletions += incomingDeletions
        cursors.merge(cursorUpdates) { _, new in new }
    }

    func nextBatch(maximumCount: Int, ownerUserId: String, now: Date) -> [HealthSyncJob] {
        Array(items.filter { $0.userId == ownerUserId && ($0.nextAttemptAt.map { $0 <= now } ?? true) }
            .prefix(maximumCount))
    }

    func nextDeletionBatch(maximumCount: Int, ownerUserId: String, now: Date) -> [HealthSyncDeletion] {
        Array(deletions.filter { $0.userId == ownerUserId && ($0.nextAttemptAt.map { $0 <= now } ?? true) }
            .prefix(maximumCount))
    }

    func remove(ids: Set<UUID>, ownerUserId: String) {
        items.removeAll { $0.userId == ownerUserId && ids.contains($0.id) }
    }

    func removeDeletions(ids: Set<UUID>, ownerUserId: String) {
        deletions.removeAll { $0.userId == ownerUserId && ids.contains($0.id) }
    }

    func markRetry(_ jobs: [HealthSyncJob]) {
        let byId = Dictionary(uniqueKeysWithValues: jobs.map { ($0.id, $0) })
        items = items.map { byId[$0.id] ?? $0 }
    }

    func markDeletionRetry(_ failed: [HealthSyncDeletion]) {
        let byId = Dictionary(uniqueKeysWithValues: failed.map { ($0.id, $0) })
        deletions = deletions.map { byId[$0.id] ?? $0 }
    }

    func count(ownerUserId: String) -> Int {
        items.filter { $0.userId == ownerUserId }.count + deletions.filter { $0.userId == ownerUserId }.count
    }

    func nextRetryDate(ownerUserId: String) -> Date? {
        (items.compactMap(\.nextAttemptAt) + deletions.compactMap(\.nextAttemptAt)).min()
    }

    func cursorData(for key: String) -> Data? { cursors[key] }

    func clear(ownerUserId: String) {
        items.removeAll { $0.userId == ownerUserId }
        deletions.removeAll { $0.userId == ownerUserId }
    }
}

/// Fails with the scripted errors in order, then succeeds.
actor ScriptedTransport: HealthSyncTransport {
    private var failures: [HealthSyncError]
    private(set) var uploadedIds: [UUID] = []

    init(failures: [HealthSyncError]) {
        self.failures = failures
    }

    func upload(_ items: [HealthSyncJob], deletions: [HealthSyncDeletion]) async throws -> HealthSyncResult {
        if !failures.isEmpty {
            throw failures.removeFirst()
        }
        uploadedIds += items.map(\.id)
        return HealthSyncResult(
            receivedCount: items.count + deletions.count,
            insertedCount: items.count,
            updatedCount: 0,
            duplicateCount: 0,
            deletedCount: deletions.count)
    }

    func fetchSummary(from: Date, to: Date) async throws -> HealthSummary { .empty }
}

/// Holds the first upload until `release()` so tests can act while a sync is in flight.
actor GatedTransport: HealthSyncTransport {
    private(set) var uploadCount = 0
    private var gate: CheckedContinuation<Void, Never>?
    private var startWaiters: [CheckedContinuation<Void, Never>] = []
    private var isReleased = false

    func upload(_ items: [HealthSyncJob], deletions: [HealthSyncDeletion]) async throws -> HealthSyncResult {
        uploadCount += 1
        startWaiters.forEach { $0.resume() }
        startWaiters.removeAll()
        if !isReleased {
            await withCheckedContinuation { gate = $0 }
        }
        return HealthSyncResult(
            receivedCount: items.count,
            insertedCount: items.count,
            updatedCount: 0,
            duplicateCount: 0)
    }

    func waitUntilUploadStarts() async {
        if uploadCount > 0 { return }
        await withCheckedContinuation { startWaiters.append($0) }
    }

    func release() {
        isReleased = true
        gate?.resume()
        gate = nil
    }

    func fetchSummary(from: Date, to: Date) async throws -> HealthSummary { .empty }
}
