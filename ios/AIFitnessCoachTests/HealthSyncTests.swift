import XCTest
@testable import AIFitnessCoach

final class HealthSyncTests: XCTestCase {
    func testQueuePersistsAndDeduplicatesSamplesAndCursor() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appending(path: UUID().uuidString, directoryHint: .isDirectory)
        let fileURL = directory.appending(path: "queue.json")
        defer { try? FileManager.default.removeItem(at: directory) }
        let sample = HealthSyncItem(
            id: UUID(),
            metric: "steps",
            value: 120,
            unit: "count",
            observedAt: Date(timeIntervalSince1970: 1_800_000_000),
            ownerUserId: "user-1",
            deviceId: "device-1")

        let firstQueue = try FileHealthSyncQueue(fileURL: fileURL)
        let cursor = Data([1, 2, 3])
        try await firstQueue.enqueue(
            [sample, sample],
            cursorUpdates: ["user-1|steps": cursor])
        let reopenedQueue = try FileHealthSyncQueue(fileURL: fileURL)
        let count = try await reopenedQueue.count(ownerUserId: "user-1")
        let queued = try await reopenedQueue.nextBatch(
            maximumCount: 10,
            ownerUserId: "user-1",
            now: .now)
        let restoredCursor = try await reopenedQueue.cursorData(for: "user-1|steps")

        XCTAssertEqual(count, 1)
        XCTAssertEqual(queued, [sample])
        XCTAssertEqual(restoredCursor, cursor)
    }

    func testQueuePersistsDeletionUntilAcknowledged() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appending(path: UUID().uuidString, directoryHint: .isDirectory)
        let fileURL = directory.appending(path: "queue.json")
        defer { try? FileManager.default.removeItem(at: directory) }
        let queue = try FileHealthSyncQueue(fileURL: fileURL)
        let deletion = HealthSyncDeletion(
            id: UUID(),
            userId: "user-1",
            source: "apple_health",
            externalId: "sample-id",
            deviceId: "device-1")
        try await queue.enqueue([], deletions: [deletion], cursorUpdates: [:])

        let reopened = try FileHealthSyncQueue(fileURL: fileURL)
        let pending = try await reopened.nextDeletionBatch(
            maximumCount: 10,
            ownerUserId: "user-1",
            now: .now)
        XCTAssertEqual(pending, [deletion])

        try await reopened.removeDeletions(ids: [deletion.id], ownerUserId: "user-1")
        let afterAcknowledgement = try await reopened.nextDeletionBatch(
            maximumCount: 10,
            ownerUserId: "user-1",
            now: .now)
        XCTAssertTrue(afterAcknowledgement.isEmpty)
    }

    func testDeletionSupersedesMatchingPendingSample() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appending(path: UUID().uuidString, directoryHint: .isDirectory)
        let fileURL = directory.appending(path: "queue.json")
        defer { try? FileManager.default.removeItem(at: directory) }
        let queue = try FileHealthSyncQueue(fileURL: fileURL)
        let item = makeJob(userId: "user-1")
        let deletion = HealthSyncDeletion(
            id: UUID(),
            userId: "user-1",
            source: item.source,
            externalId: item.id.uuidString,
            deviceId: item.deviceId)

        try await queue.enqueue([item], deletions: [deletion], cursorUpdates: [:])

        let pendingItems = try await queue.nextBatch(
            maximumCount: 10,
            ownerUserId: "user-1",
            now: .now)
        let pendingDeletions = try await queue.nextDeletionBatch(
            maximumCount: 10,
            ownerUserId: "user-1",
            now: .now)
        XCTAssertTrue(pendingItems.isEmpty)
        XCTAssertEqual(pendingDeletions, [deletion])
    }

    func testClearingOneUserPreservesAnotherUsersPendingChanges() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appending(path: UUID().uuidString, directoryHint: .isDirectory)
        let fileURL = directory.appending(path: "queue.json")
        defer { try? FileManager.default.removeItem(at: directory) }
        let queue = try FileHealthSyncQueue(fileURL: fileURL)
        let userOneItem = makeJob(userId: "user-1")
        let userTwoItem = makeJob(userId: "user-2")
        let userTwoDeletion = HealthSyncDeletion(
            id: UUID(),
            userId: "user-2",
            source: "apple_health",
            externalId: "sample-id",
            deviceId: "device-1")
        try await queue.enqueue(
            [userOneItem, userTwoItem],
            deletions: [userTwoDeletion],
            cursorUpdates: [:])

        try await queue.clear(ownerUserId: "user-1")
        let remaining = try await queue.nextBatch(
            maximumCount: 10,
            ownerUserId: "user-2",
            now: .now)
        let remainingDeletions = try await queue.nextDeletionBatch(
            maximumCount: 10,
            ownerUserId: "user-2",
            now: .now)

        XCTAssertEqual(remaining, [userTwoItem])
        XCTAssertEqual(remainingDeletions, [userTwoDeletion])
    }

    func testCoordinatorRemovesChangesOnlyAfterServerAcknowledgement() async throws {
        let queue = TestHealthSyncQueue()
        let transport = TestHealthSyncTransport()
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        let item = makeJob(userId: "user-1")
        let deletion = HealthSyncDeletion(
            id: UUID(),
            userId: "user-1",
            source: "apple_health",
            externalId: "deleted-sample",
            deviceId: "device-1")

        try await coordinator.enqueue(
            [item],
            ownerUserId: "user-1",
            cursorUpdates: [:],
            deletions: [deletion])
        try await coordinator.synchronizePending(ownerUserId: "user-1")
        let uploaded = await transport.uploadedItems
        let uploadedDeletions = await transport.uploadedDeletions
        let remaining = try await queue.count(ownerUserId: "user-1")
        let state = await coordinator.state

        XCTAssertEqual(uploaded, [item])
        XCTAssertEqual(uploadedDeletions, [deletion])
        XCTAssertEqual(remaining, 0)
        XCTAssertEqual(state, .idle)
    }

    func testOfflineUploadPersistsExponentialRetryState() async throws {
        let queue = TestHealthSyncQueue()
        let transport = TestHealthSyncTransport(failure: HealthSyncError.offline)
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        let item = makeJob(userId: "user-1")
        try await coordinator.enqueue([item], ownerUserId: "user-1", cursorUpdates: [:])

        do {
            try await coordinator.synchronizePending(ownerUserId: "user-1")
            XCTFail("Expected offline transport error.")
        } catch HealthSyncError.offline {
            let remaining = try await queue.count(ownerUserId: "user-1")
            let due = try await queue.nextBatch(
                maximumCount: 10,
                ownerUserId: "user-1",
                now: .distantFuture)
            let state = await coordinator.state
            let metrics = await coordinator.metrics
            XCTAssertEqual(remaining, 1)
            XCTAssertEqual(state, .offline)
            XCTAssertEqual(try XCTUnwrap(due.first).attemptCount, 1)
            XCTAssertEqual(metrics.errors, 1)
            XCTAssertEqual(metrics.retries, 1)
        }
    }

    // Behaviour changed for the beta: a rejected batch used to stay first in the queue and was
    // resent on every sync, so nothing newer could upload. It is now parked, still queued.
    func testPermanentServerErrorKeepsChangesQueuedButParked() async throws {
        let queue = TestHealthSyncQueue()
        let transport = TestHealthSyncTransport(failure: .server(statusCode: 422))
        let coordinator = HealthSyncCoordinator(queue: queue, transport: transport)
        let item = makeJob(userId: "user-1")
        try await coordinator.enqueue([item], ownerUserId: "user-1", cursorUpdates: [:])

        do {
            try await coordinator.synchronizePending(ownerUserId: "user-1")
            XCTFail("Expected a permanent server error.")
        } catch HealthSyncError.server(statusCode: 422) {
            let dueNow = try await queue.nextBatch(maximumCount: 10, ownerUserId: "user-1", now: .now)
            let dueLater = try await queue.nextBatch(
                maximumCount: 10,
                ownerUserId: "user-1",
                now: Date.now.addingTimeInterval(HealthSyncCoordinator.rejectedRetryDelay + 1))
            let state = await coordinator.state
            let metrics = await coordinator.metrics
            XCTAssertTrue(dueNow.isEmpty)
            XCTAssertEqual(dueLater.map(\.id), [item.id])
            XCTAssertEqual(dueLater.first?.rejectionCount, 1)
            XCTAssertEqual(dueLater.first?.attemptCount, 0)
            XCTAssertEqual(state, .failed(HealthSyncError.server(statusCode: 422).localizedDescription))
            XCTAssertEqual(metrics.errors, 1)
            XCTAssertEqual(metrics.retries, 0)
            XCTAssertEqual(metrics.parked, 1)
        }
    }

    private func makeJob(userId: String) -> HealthSyncJob {
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

private actor TestHealthSyncQueue: HealthSyncQueue {
    private var items: [HealthSyncJob] = []
    private var deletions: [HealthSyncDeletion] = []
    private var cursors: [String: Data] = [:]

    func enqueue(
        _ incoming: [HealthSyncJob],
        deletions incomingDeletions: [HealthSyncDeletion],
        cursorUpdates: [String: Data]
    ) {
        var knownIds = Set(items.map { "\($0.userId ?? "")|\($0.source)|\($0.id)" })
        items.append(contentsOf: incoming.filter {
            knownIds.insert("\($0.userId ?? "")|\($0.source)|\($0.id)").inserted
        })
        var knownDeletions = Set(deletions.map { "\($0.userId)|\($0.source)|\($0.externalId)" })
        deletions.append(contentsOf: incomingDeletions.filter {
            knownDeletions.insert("\($0.userId)|\($0.source)|\($0.externalId)").inserted
        })
        cursors.merge(cursorUpdates) { _, new in new }
    }

    func nextBatch(maximumCount: Int, ownerUserId: String, now: Date) -> [HealthSyncJob] {
        Array(items
            .filter { $0.userId == ownerUserId && ($0.nextAttemptAt.map { $0 <= now } ?? true) }
            .prefix(maximumCount))
    }

    func nextDeletionBatch(
        maximumCount: Int,
        ownerUserId: String,
        now: Date
    ) -> [HealthSyncDeletion] {
        Array(deletions
            .filter { $0.userId == ownerUserId && ($0.nextAttemptAt.map { $0 <= now } ?? true) }
            .prefix(maximumCount))
    }

    func remove(ids: Set<UUID>, ownerUserId: String) {
        items.removeAll { $0.userId == ownerUserId && ids.contains($0.id) }
    }

    func removeDeletions(ids: Set<UUID>, ownerUserId: String) {
        deletions.removeAll { $0.userId == ownerUserId && ids.contains($0.id) }
    }

    func count(ownerUserId: String) -> Int {
        items.filter { $0.userId == ownerUserId }.count
            + deletions.filter { $0.userId == ownerUserId }.count
    }

    func markRetry(_ jobs: [HealthSyncJob]) {
        let byId = Dictionary(uniqueKeysWithValues: jobs.map { ($0.id, $0) })
        for index in items.indices {
            if let job = byId[items[index].id] {
                items[index] = job
            }
        }
    }

    func markDeletionRetry(_ failed: [HealthSyncDeletion]) {
        let byId = Dictionary(uniqueKeysWithValues: failed.map { ($0.id, $0) })
        for index in deletions.indices {
            if let deletion = byId[deletions[index].id] {
                deletions[index] = deletion
            }
        }
    }

    func nextRetryDate(ownerUserId: String) -> Date? {
        let jobDates = items.filter { $0.userId == ownerUserId }.compactMap(\.nextAttemptAt)
        let deletionDates = deletions.filter { $0.userId == ownerUserId }.compactMap(\.nextAttemptAt)
        return (jobDates + deletionDates).min()
    }

    func cursorData(for key: String) -> Data? {
        cursors[key]
    }

    func clear(ownerUserId: String) {
        items.removeAll { $0.userId == ownerUserId }
        deletions.removeAll { $0.userId == ownerUserId }
        cursors = cursors.filter { !$0.key.hasPrefix("\(ownerUserId)|") }
    }
}

private actor TestHealthSyncTransport: HealthSyncTransport {
    private(set) var uploadedItems: [HealthSyncJob] = []
    private(set) var uploadedDeletions: [HealthSyncDeletion] = []
    private let failure: HealthSyncError?

    init(failure: HealthSyncError? = nil) {
        self.failure = failure
    }

    func upload(
        _ items: [HealthSyncJob],
        deletions: [HealthSyncDeletion]
    ) async throws -> HealthSyncResult {
        if let failure {
            throw failure
        }
        uploadedItems.append(contentsOf: items)
        uploadedDeletions.append(contentsOf: deletions)
        return HealthSyncResult(
            receivedCount: items.count + deletions.count,
            insertedCount: items.count,
            updatedCount: 0,
            duplicateCount: 0,
            deletedCount: deletions.count)
    }

    func fetchSummary(from: Date, to: Date) async throws -> HealthSummary {
        .empty
    }
}
