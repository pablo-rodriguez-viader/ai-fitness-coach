import Foundation
import os

protocol TelemetryUploader: Sendable {
    func send(_ events: [AppTelemetry.Event]) async throws
}

/// Crash, error, usage and sync events for the beta. Events never contain health values or
/// free text typed by the user: only names, screens and counters. They are kept in a small file
/// so crash reports delivered at launch survive until they can be uploaded.
actor AppTelemetry {
    struct Event: Codable, Equatable, Sendable {
        let name: String
        let occurredAt: Date
        let properties: [String: String]
    }

    /// The app instance waits for the analytics consent before uploading anything.
    static let shared = AppTelemetry(fileURL: AppTelemetry.defaultFileURL(), requiresConsent: true)

    private static let logger = Logger(
        subsystem: Bundle.main.bundleIdentifier ?? "AIFitnessCoach",
        category: "Telemetry")
    static let maximumBufferedEvents = 300
    static let uploadBatchSize = 50
    /// Beta builds upload sooner so the team sees crashes, errors and sync results within minutes.
    static let flushThreshold = AppEnvironment.current == .beta ? 5 : 20

    private let fileURL: URL?
    private var buffer: [Event] = []
    private var uploader: (any TelemetryUploader)?
    private var isFlushing = false
    private var hasLoaded = false
    /// GDPR: undecided = keep a bounded local buffer but upload nothing; denied = drop
    /// everything; granted = upload as usual.
    private var uploadDecision: ConsentDecision

    init(fileURL: URL?, requiresConsent: Bool = false) {
        self.fileURL = fileURL
        uploadDecision = requiresConsent ? .undecided : .granted
    }

    func setUploadDecision(_ decision: ConsentDecision) {
        uploadDecision = decision
        switch decision {
        case .denied:
            loadIfNeeded()
            buffer.removeAll()
            persist()
        case .granted:
            Task { await self.flush() }
        case .undecided:
            break
        }
    }

    func currentUploadDecision() -> ConsentDecision {
        uploadDecision
    }

    /// Fire-and-forget entry point usable from any thread or actor.
    nonisolated func track(_ name: String, _ properties: [String: String] = [:]) {
        let event = Event(name: name, occurredAt: Date(), properties: properties)
        Task { await self.append(event) }
    }

    func configure(uploader: (any TelemetryUploader)?) {
        self.uploader = uploader
    }

    func append(_ event: Event) {
        guard uploadDecision != .denied else { return }
        loadIfNeeded()
        buffer.append(event)
        if buffer.count > Self.maximumBufferedEvents {
            buffer.removeFirst(buffer.count - Self.maximumBufferedEvents)
        }
        persist()
        if buffer.count >= Self.flushThreshold {
            Task { await self.flush() }
        }
    }

    func flush() async {
        loadIfNeeded()
        guard uploadDecision == .granted, let uploader, !isFlushing, !buffer.isEmpty else { return }
        isFlushing = true
        defer { isFlushing = false }
        while !buffer.isEmpty {
            let batch = Array(buffer.prefix(Self.uploadBatchSize))
            do {
                try await uploader.send(batch)
                buffer.removeFirst(min(batch.count, buffer.count))
                persist()
            } catch TelemetryUploadError.rejected {
                // The server will never accept this batch; drop it rather than retry forever.
                buffer.removeFirst(min(batch.count, buffer.count))
                persist()
            } catch {
                Self.logger.debug("Telemetry upload deferred: \(error.localizedDescription, privacy: .public)")
                return
            }
        }
    }

    /// Removes queued events, used when the user signs out.
    func clear() {
        buffer.removeAll()
        hasLoaded = true
        persist()
    }

    func pendingEvents() -> [Event] {
        loadIfNeeded()
        return buffer
    }

    private func loadIfNeeded() {
        guard !hasLoaded else { return }
        hasLoaded = true
        guard let fileURL, let data = try? Data(contentsOf: fileURL) else { return }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let stored = (try? decoder.decode([Event].self, from: data)) ?? []
        buffer = stored + buffer
    }

    private func persist() {
        guard let fileURL else { return }
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        guard let data = try? encoder.encode(buffer) else { return }
        try? data.write(to: fileURL, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    static func defaultFileURL() -> URL? {
        let fileManager = FileManager.default
        guard var directory = try? fileManager.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true)
        else { return nil }
        directory.append(path: "Telemetry", directoryHint: .isDirectory)
        try? fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        var values = URLResourceValues()
        values.isExcludedFromBackup = true
        try? directory.setResourceValues(values)
        return directory.appending(path: "events.json")
    }

    static var appVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown"
    }

    static var buildNumber: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "unknown"
    }

    static var osVersion: String {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        return "\(version.majorVersion).\(version.minorVersion).\(version.patchVersion)"
    }

    static var deviceModel: String {
        var systemInfo = utsname()
        uname(&systemInfo)
        return Mirror(reflecting: systemInfo.machine).children.reduce(into: "") { result, element in
            guard let value = element.value as? Int8, value != 0 else { return }
            result.append(Character(UnicodeScalar(UInt8(value))))
        }
    }
}

enum TelemetryUploadError: Error {
    case rejected
    case failed(statusCode: Int)
}

/// Sends events to `POST /api/v1/telemetry/events` with the user's session.
actor TelemetryAPIClient: TelemetryUploader {
    private struct Payload: Encodable {
        let events: [EventPayload]
        let appVersion: String
        let buildNumber: String
        let osVersion: String
        let deviceModel: String
        /// development, beta or production: lets logs and reports separate TestFlight data.
        let environment: String
    }

    private struct EventPayload: Encodable {
        let name: String
        let occurredAt: Date
        let properties: [String: String]
    }

    private let baseURL: URL
    private let sessionManager: SessionManager
    private let session: URLSession

    init(baseURL: URL, sessionManager: SessionManager, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.sessionManager = sessionManager
        self.session = session
    }

    func send(_ events: [AppTelemetry.Event]) async throws {
        guard !events.isEmpty else { return }
        let token = try await sessionManager.validAccessToken()
        var request = URLRequest(url: baseURL.appending(path: "api/v1/telemetry/events"))
        request.httpMethod = "POST"
        request.timeoutInterval = 15
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        request.httpBody = try encoder.encode(Payload(
            events: events.map { EventPayload(name: $0.name, occurredAt: $0.occurredAt, properties: $0.properties) },
            appVersion: AppTelemetry.appVersion,
            buildNumber: AppTelemetry.buildNumber,
            osVersion: AppTelemetry.osVersion,
            deviceModel: AppTelemetry.deviceModel,
            environment: AppEnvironment.current.rawValue))
        let (_, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw TelemetryUploadError.failed(statusCode: 0)
        }
        switch http.statusCode {
        case 200..<300:
            return
        case 400, 413, 422:
            throw TelemetryUploadError.rejected
        default:
            throw TelemetryUploadError.failed(statusCode: http.statusCode)
        }
    }
}
