import Foundation

protocol HealthSyncQueue: Sendable {
    func enqueue(
        _ items: [HealthSyncJob],
        deletions: [HealthSyncDeletion],
        cursorUpdates: [String: Data]
    ) async throws
    func nextBatch(
        maximumCount: Int,
        ownerUserId: String,
        now: Date
    ) async throws -> [HealthSyncJob]
    func nextDeletionBatch(
        maximumCount: Int,
        ownerUserId: String,
        now: Date
    ) async throws -> [HealthSyncDeletion]
    func remove(ids: Set<UUID>, ownerUserId: String) async throws
    func removeDeletions(ids: Set<UUID>, ownerUserId: String) async throws
    func markRetry(_ jobs: [HealthSyncJob]) async throws
    func markDeletionRetry(_ deletions: [HealthSyncDeletion]) async throws
    func count(ownerUserId: String) async throws -> Int
    func nextRetryDate(ownerUserId: String) async throws -> Date?
    func cursorData(for key: String) async throws -> Data?
    func clear(ownerUserId: String) async throws
}

actor FileHealthSyncQueue: HealthSyncQueue {
    private struct StoredQueue: Codable {
        var items: [HealthSyncItem] = []
        var deletions: [HealthSyncDeletion] = []
        var cursors: [String: Data] = [:]

        private enum CodingKeys: String, CodingKey {
            case items, deletions, cursors
        }

        init() {}

        init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: CodingKeys.self)
            items = try container.decodeIfPresent([HealthSyncItem].self, forKey: .items) ?? []
            deletions = try container.decodeIfPresent([HealthSyncDeletion].self, forKey: .deletions) ?? []
            cursors = try container.decodeIfPresent([String: Data].self, forKey: .cursors) ?? [:]
        }

        func encode(to encoder: Encoder) throws {
            var container = encoder.container(keyedBy: CodingKeys.self)
            try container.encode(items, forKey: .items)
            try container.encode(deletions, forKey: .deletions)
            try container.encode(cursors, forKey: .cursors)
        }
    }

    private let fileURL: URL
    private let fileManager: FileManager
    private var storedQueue: StoredQueue?

    init(fileURL requestedFileURL: URL? = nil, fileManager: FileManager = .default) throws {
        self.fileManager = fileManager
        let directory: URL
        if let requestedFileURL {
            directory = requestedFileURL.deletingLastPathComponent()
            fileURL = requestedFileURL
        } else {
            directory = try fileManager.url(
                for: .applicationSupportDirectory,
                in: .userDomainMask,
                appropriateFor: nil,
                create: true)
                .appending(path: "HealthSync")
            fileURL = directory.appending(path: "queue.json")
        }
        try fileManager.createDirectory(
            at: directory,
            withIntermediateDirectories: true,
            attributes: [.protectionKey: FileProtectionType.complete])
        var protectedDirectory = directory
        var resourceValues = URLResourceValues()
        resourceValues.isExcludedFromBackup = true
        try protectedDirectory.setResourceValues(resourceValues)
    }

    func enqueue(
        _ newItems: [HealthSyncJob],
        deletions newDeletions: [HealthSyncDeletion] = [],
        cursorUpdates: [String: Data]
    ) throws {
        var state = try load()
        var knownIds = Set(state.items.map { "\($0.userId ?? "")|\($0.source)|\($0.id)" })
        for item in newItems where knownIds.insert("\(item.userId ?? "")|\(item.source)|\(item.id)").inserted {
            state.items.append(item)
        }
        let deletionKeys = Set(newDeletions.map {
            "\($0.userId)|\($0.source)|\($0.externalId)"
        })
        state.items.removeAll {
            deletionKeys.contains("\($0.userId ?? "")|\($0.source)|\($0.id.uuidString)")
        }
        var knownDeletions = Set(state.deletions.map { "\($0.userId)|\($0.source)|\($0.externalId)" })
        for deletion in newDeletions
        where knownDeletions.insert("\(deletion.userId)|\(deletion.source)|\(deletion.externalId)").inserted {
            state.deletions.append(deletion)
        }
        state.cursors.merge(cursorUpdates) { _, new in new }
        try save(state)
    }

    func nextDeletionBatch(
        maximumCount: Int,
        ownerUserId: String,
        now: Date
    ) throws -> [HealthSyncDeletion] {
        Array(try load().deletions
            .filter { $0.userId == ownerUserId && ($0.nextAttemptAt.map { $0 <= now } ?? true) }
            .prefix(max(0, maximumCount)))
    }

    func nextBatch(
        maximumCount: Int,
        ownerUserId: String,
        now: Date
    ) throws -> [HealthSyncJob] {
        Array(try load().items
            .filter { $0.userId == ownerUserId && ($0.nextAttemptAt.map { $0 <= now } ?? true) }
            .prefix(max(0, maximumCount)))
    }

    func removeDeletions(ids: Set<UUID>, ownerUserId: String) throws {
        var state = try load()
        state.deletions.removeAll {
            $0.userId == ownerUserId && ids.contains($0.id)
        }
        try save(state)
    }

    func remove(ids: Set<UUID>, ownerUserId: String) throws {
        var state = try load()
        state.items.removeAll {
            $0.ownerUserId == ownerUserId && ids.contains($0.id)
        }
        try save(state)
    }

    func count(ownerUserId: String) throws -> Int {
        let state = try load()
        return state.items.filter { $0.userId == ownerUserId }.count
            + state.deletions.filter { $0.userId == ownerUserId }.count
    }

    func markRetry(_ jobs: [HealthSyncJob]) throws {
        var state = try load()
        let retryById = Dictionary(uniqueKeysWithValues: jobs.map { ($0.id, $0) })
        for index in state.items.indices {
            guard let retry = retryById[state.items[index].id],
                  state.items[index].userId == retry.userId
            else { continue }
            state.items[index] = retry
        }
        try save(state)
    }

    func markDeletionRetry(_ deletions: [HealthSyncDeletion]) throws {
        var state = try load()
        let retryById = Dictionary(uniqueKeysWithValues: deletions.map { ($0.id, $0) })
        for index in state.deletions.indices {
            guard let retry = retryById[state.deletions[index].id],
                  state.deletions[index].userId == retry.userId
            else { continue }
            state.deletions[index] = retry
        }
        try save(state)
    }

    func nextRetryDate(ownerUserId: String) throws -> Date? {
        let state = try load()
        let jobDates = state.items.filter { $0.userId == ownerUserId }.compactMap(\.nextAttemptAt)
        let deletionDates = state.deletions.filter { $0.userId == ownerUserId }.compactMap(\.nextAttemptAt)
        return (jobDates + deletionDates).min()
    }

    func cursorData(for key: String) throws -> Data? {
        try load().cursors[key]
    }

    func clear(ownerUserId: String) throws {
        var state = try load()
        state.items.removeAll { $0.userId == ownerUserId }
        state.deletions.removeAll { $0.userId == ownerUserId }
        let prefix = "\(ownerUserId)|"
        state.cursors = state.cursors.filter { !$0.key.hasPrefix(prefix) }
        try save(state)
    }

    private func load() throws -> StoredQueue {
        if let storedQueue {
            return storedQueue
        }
        guard fileManager.fileExists(atPath: fileURL.path) else {
            let empty = StoredQueue()
            storedQueue = empty
            return empty
        }
        let data = try Data(contentsOf: fileURL)
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let decoded = try decoder.decode(StoredQueue.self, from: data)
        storedQueue = decoded
        return decoded
    }

    private func save(_ state: StoredQueue) throws {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let data = try encoder.encode(state)
        try data.write(to: fileURL, options: .atomic)
        try fileManager.setAttributes(
            [.protectionKey: FileProtectionType.complete],
            ofItemAtPath: fileURL.path)
        storedQueue = state
    }
}
