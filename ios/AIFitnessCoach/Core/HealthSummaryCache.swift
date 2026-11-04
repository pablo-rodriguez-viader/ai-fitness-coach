import Foundation

struct CachedHealthSummary: Codable, Equatable, Sendable {
    let userId: String
    let summary: HealthSummary
    let fetchedAt: Date
}

@MainActor
protocol HealthSummaryCaching {
    func load(userId: String) -> CachedHealthSummary?
    func save(_ summary: HealthSummary, userId: String, fetchedAt: Date)
    func clear()
}

/// Last dashboard summary on disk so the app has something to show after a restart without
/// connection. Encrypted with the device (complete protection) and excluded from backups.
@MainActor
final class FileHealthSummaryCache: HealthSummaryCaching {
    private let fileURL: URL?

    init(fileURL: URL? = FileHealthSummaryCache.defaultFileURL()) {
        self.fileURL = fileURL
    }

    func load(userId: String) -> CachedHealthSummary? {
        guard let fileURL, let data = try? Data(contentsOf: fileURL) else { return nil }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        guard let cached = try? decoder.decode(CachedHealthSummary.self, from: data),
              cached.userId == userId
        else { return nil }
        return cached
    }

    func save(_ summary: HealthSummary, userId: String, fetchedAt: Date) {
        guard let fileURL else { return }
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(
            CachedHealthSummary(userId: userId, summary: summary, fetchedAt: fetchedAt))
        else { return }
        try? data.write(to: fileURL, options: [.atomic, .completeFileProtection])
    }

    func clear() {
        guard let fileURL else { return }
        try? FileManager.default.removeItem(at: fileURL)
    }

    nonisolated static func defaultFileURL() -> URL? {
        let fileManager = FileManager.default
        guard var directory = try? fileManager.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true)
        else { return nil }
        directory.append(path: "Cache", directoryHint: .isDirectory)
        try? fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        var values = URLResourceValues()
        values.isExcludedFromBackup = true
        try? directory.setResourceValues(values)
        return directory.appending(path: "health-summary.json")
    }
}
