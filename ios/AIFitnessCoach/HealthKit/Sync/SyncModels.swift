import Foundation

enum HealthMetricType: String, Codable, CaseIterable, Sendable {
    case steps
    case bodyMass = "body_mass"
    case activeEnergy = "active_energy"
    case basalEnergy = "basal_energy"
    case heartRate = "heart_rate"
    case sleepDuration = "sleep_duration"
    case workoutDuration = "workout_duration"

    var unit: String {
        switch self {
        case .steps:
            "count"
        case .bodyMass:
            "kg"
        case .activeEnergy, .basalEnergy:
            "kcal"
        case .heartRate:
            "count/min"
        case .sleepDuration, .workoutDuration:
            "s"
        }
    }
}

struct HealthSyncJob: Codable, Equatable, Identifiable, Sendable {
    let id: UUID
    let metricType: HealthMetricType
    let value: Double
    let unit: String
    let source: String
    let timestamp: Date
    let deviceId: String
    let userId: String?
    var attemptCount: Int
    var nextAttemptAt: Date?
    var lastError: String?
    /// Times the server rejected this change permanently (4xx). Tracked apart from
    /// `attemptCount` so offline retries never count towards dropping a sample.
    var rejectionCount: Int

    var metric: String { metricType.rawValue }
    var observedAt: Date { timestamp }
    var ownerUserId: String? { userId }

    init(
        id: UUID,
        metricType: HealthMetricType,
        value: Double,
        unit: String,
        source: String = "apple_health",
        timestamp: Date,
        deviceId: String,
        userId: String? = nil,
        attemptCount: Int = 0,
        nextAttemptAt: Date? = nil,
        lastError: String? = nil,
        rejectionCount: Int = 0
    ) {
        self.id = id
        self.metricType = metricType
        self.value = value
        self.unit = unit
        self.source = source
        self.timestamp = timestamp
        self.deviceId = deviceId
        self.userId = userId
        self.attemptCount = attemptCount
        self.nextAttemptAt = nextAttemptAt
        self.lastError = lastError
        self.rejectionCount = rejectionCount
    }

    init(
        id: UUID,
        metric: String,
        value: Double,
        unit: String,
        observedAt: Date,
        source: String = "apple_health",
        ownerUserId: String? = nil,
        deviceId: String = "legacy-device"
    ) {
        self.init(
            id: id,
            metricType: HealthMetricType(rawValue: metric) ?? .steps,
            value: value,
            unit: unit,
            source: source,
            timestamp: observedAt,
            deviceId: deviceId,
            userId: ownerUserId)
    }

    func owned(by userId: String, deviceId: String? = nil) -> HealthSyncJob {
        HealthSyncJob(
            id: id,
            metricType: metricType,
            value: value,
            unit: unit,
            source: source,
            timestamp: timestamp,
            deviceId: deviceId ?? self.deviceId,
            userId: userId,
            attemptCount: attemptCount,
            nextAttemptAt: nextAttemptAt,
            lastError: lastError,
            rejectionCount: rejectionCount)
    }

    func retrying(after delay: TimeInterval, error: String, now: Date = .now) -> HealthSyncJob {
        HealthSyncJob(
            id: id,
            metricType: metricType,
            value: value,
            unit: unit,
            source: source,
            timestamp: timestamp,
            deviceId: deviceId,
            userId: userId,
            attemptCount: attemptCount + 1,
            nextAttemptAt: now.addingTimeInterval(delay),
            lastError: error,
            rejectionCount: rejectionCount)
    }

    /// Moves a permanently rejected change out of the way so newer changes can still upload.
    func parked(after delay: TimeInterval, error: String, now: Date = .now) -> HealthSyncJob {
        HealthSyncJob(
            id: id,
            metricType: metricType,
            value: value,
            unit: unit,
            source: source,
            timestamp: timestamp,
            deviceId: deviceId,
            userId: userId,
            attemptCount: attemptCount,
            nextAttemptAt: now.addingTimeInterval(delay),
            lastError: error,
            rejectionCount: rejectionCount + 1)
    }

    private enum CodingKeys: String, CodingKey {
        case id, metricType, metric, value, unit, source, timestamp, observedAt
        case deviceId, userId, ownerUserId, attemptCount, nextAttemptAt, lastError, rejectionCount
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(UUID.self, forKey: .id)
        let metricValue: String
        if let storedMetric = try container.decodeIfPresent(String.self, forKey: .metricType) {
            metricValue = storedMetric
        } else {
            metricValue = try container.decode(String.self, forKey: .metric)
        }
        guard let decodedMetricType = HealthMetricType(rawValue: metricValue) else {
            throw DecodingError.dataCorruptedError(
                forKey: .metricType,
                in: container,
                debugDescription: "Unsupported HealthKit metric \(metricValue).")
        }
        metricType = decodedMetricType
        value = try container.decode(Double.self, forKey: .value)
        unit = try container.decode(String.self, forKey: .unit)
        source = try container.decodeIfPresent(String.self, forKey: .source) ?? "apple_health"
        if let storedTimestamp = try container.decodeIfPresent(Date.self, forKey: .timestamp) {
            timestamp = storedTimestamp
        } else {
            timestamp = try container.decode(Date.self, forKey: .observedAt)
        }
        deviceId = try container.decodeIfPresent(String.self, forKey: .deviceId) ?? "legacy-device"
        let storedUserId = try container.decodeIfPresent(String.self, forKey: .userId)
        userId = try storedUserId ?? container.decodeIfPresent(String.self, forKey: .ownerUserId)
        attemptCount = try container.decodeIfPresent(Int.self, forKey: .attemptCount) ?? 0
        nextAttemptAt = try container.decodeIfPresent(Date.self, forKey: .nextAttemptAt)
        lastError = try container.decodeIfPresent(String.self, forKey: .lastError)
        rejectionCount = try container.decodeIfPresent(Int.self, forKey: .rejectionCount) ?? 0
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(metricType, forKey: .metricType)
        try container.encode(value, forKey: .value)
        try container.encode(unit, forKey: .unit)
        try container.encode(source, forKey: .source)
        try container.encode(timestamp, forKey: .timestamp)
        try container.encode(deviceId, forKey: .deviceId)
        try container.encodeIfPresent(userId, forKey: .userId)
        try container.encode(attemptCount, forKey: .attemptCount)
        try container.encodeIfPresent(nextAttemptAt, forKey: .nextAttemptAt)
        try container.encodeIfPresent(lastError, forKey: .lastError)
        try container.encode(rejectionCount, forKey: .rejectionCount)
    }
}

typealias HealthSyncItem = HealthSyncJob

struct HealthSyncDeletion: Codable, Equatable, Identifiable, Sendable {
    let id: UUID
    let userId: String
    let source: String
    let externalId: String
    let deviceId: String
    var attemptCount: Int = 0
    var nextAttemptAt: Date?
    var lastError: String?
    /// Optional so queue files written by earlier builds still decode.
    var rejectionCount: Int?

    var rejections: Int { rejectionCount ?? 0 }

    func retrying(after delay: TimeInterval, error: String, now: Date = .now) -> HealthSyncDeletion {
        HealthSyncDeletion(
            id: id,
            userId: userId,
            source: source,
            externalId: externalId,
            deviceId: deviceId,
            attemptCount: attemptCount + 1,
            nextAttemptAt: now.addingTimeInterval(delay),
            lastError: error,
            rejectionCount: rejectionCount)
    }

    func parked(after delay: TimeInterval, error: String, now: Date = .now) -> HealthSyncDeletion {
        HealthSyncDeletion(
            id: id,
            userId: userId,
            source: source,
            externalId: externalId,
            deviceId: deviceId,
            attemptCount: attemptCount,
            nextAttemptAt: now.addingTimeInterval(delay),
            lastError: error,
            rejectionCount: rejections + 1)
    }
}

struct HealthSyncResult: Decodable, Equatable, Sendable {
    let receivedCount: Int
    let insertedCount: Int
    let updatedCount: Int
    let duplicateCount: Int
    let deletedCount: Int
    /// Samples the server skipped as invalid (for example, timestamps in the future).
    let rejectedCount: Int

    init(
        receivedCount: Int,
        insertedCount: Int,
        updatedCount: Int,
        duplicateCount: Int,
        deletedCount: Int = 0,
        rejectedCount: Int = 0
    ) {
        self.receivedCount = receivedCount
        self.insertedCount = insertedCount
        self.updatedCount = updatedCount
        self.duplicateCount = duplicateCount
        self.deletedCount = deletedCount
        self.rejectedCount = rejectedCount
    }

    private enum CodingKeys: String, CodingKey {
        case receivedCount, insertedCount, updatedCount, duplicateCount, deletedCount, rejectedCount
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        receivedCount = try container.decode(Int.self, forKey: .receivedCount)
        insertedCount = try container.decode(Int.self, forKey: .insertedCount)
        updatedCount = try container.decode(Int.self, forKey: .updatedCount)
        duplicateCount = try container.decode(Int.self, forKey: .duplicateCount)
        deletedCount = try container.decodeIfPresent(Int.self, forKey: .deletedCount) ?? 0
        rejectedCount = try container.decodeIfPresent(Int.self, forKey: .rejectedCount) ?? 0
    }
}

enum HealthSyncStatus: Equatable, Sendable {
    case idle
    case queued(Int)
    case syncing(Int)
    case retrying(Int)
    case offline
    case failed(String)
}

typealias HealthSyncState = HealthSyncStatus

enum HealthSyncCoordinatorError: Error, Sendable {
    case alreadySyncing
}

enum HealthSyncError: Error, Sendable {
    case offline
    case unauthorized
    case server(statusCode: Int)
    case accountMismatch
    case invalidMetric
    case backgroundDeliveryRejected

    var isRetryable: Bool {
        switch self {
        case .offline:
            true
        case let .server(statusCode):
            statusCode == 0 || statusCode == 408 || statusCode == 429 || (500...599).contains(statusCode)
        case .unauthorized, .accountMismatch, .invalidMetric, .backgroundDeliveryRejected:
            false
        }
    }
}

typealias HealthSyncTransportError = HealthSyncError

extension HealthSyncError: LocalizedError {
    var errorDescription: String? {
        switch self {
        case .offline:
            "La sincronización queda pendiente hasta recuperar la conexión."
        case .unauthorized:
            "La sesión ya no permite sincronizar datos de salud."
        case let .server(statusCode):
            "El servidor devolvió HTTP \(statusCode) al sincronizar."
        case .accountMismatch:
            "La cola contiene datos asociados a otra cuenta."
        case .invalidMetric:
            "HealthKit devolvió una métrica no compatible."
        case .backgroundDeliveryRejected:
            "HealthKit no aceptó las actualizaciones en segundo plano."
        }
    }
}
