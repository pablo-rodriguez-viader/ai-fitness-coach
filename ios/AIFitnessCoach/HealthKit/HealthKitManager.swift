import Foundation
import HealthKit

enum HealthKitManagerError: Error, LocalizedError {
    case unavailable
    case unsupportedSampleType
    case invalidSyncAnchor

    var errorDescription: String? {
        switch self {
        case .unavailable:
            "Apple Health no está disponible en este dispositivo."
        case .unsupportedSampleType:
            "El tipo de dato solicitado no está disponible en este sistema."
        case .invalidSyncAnchor:
            "No se pudo leer el punto de sincronización de Apple Health."
        }
    }
}

private struct StoredHealthAnchor: Codable {
    let encodedAnchor: Data
    let windowStart: Date
}

private struct AnchoredHealthSamples {
    let samples: [HKSample]
    let deletedSampleIds: [UUID]
    let cursorData: Data
}

@MainActor
final class HealthKitManager: HealthDataRepository {
    private let store = HKHealthStore()
    private let syncService = HealthSyncService()
    private let syncWorker: HealthSyncWorker
    private let keychain: KeychainManager
    private let deviceId: String
    private var synchronizationTask: Task<Void, Error>?
    private var needsAnotherPass = false

    init(
        transport: any HealthSyncTransport,
        sessionManager: SessionManager,
        deviceId: String = "legacy-device",
        keychain: KeychainManager = KeychainManager()
    ) throws {
        self.keychain = keychain
        self.deviceId = deviceId
        syncWorker = HealthSyncWorker(
            coordinator: HealthSyncCoordinator(
                queue: try FileHealthSyncQueue(),
                transport: transport,
                deviceId: deviceId),
            sessionManager: sessionManager,
            deviceId: deviceId)
    }

    var isAvailable: Bool {
        HKHealthStore.isHealthDataAvailable()
    }

    /// Minimum set: every type here is read by `recentSamplesForSync` and used by a screen
    /// (see docs/APP_STORE_SUBMISSION.md, "Permisos"). Do not add a type without a use.
    var readTypes: Set<HKObjectType> {
        let identifiers: [HKQuantityTypeIdentifier] = [
            .stepCount,
            .activeEnergyBurned,
            .basalEnergyBurned,
            .heartRate,
            .bodyMass
        ]

        var types = Set<HKObjectType>()
        identifiers
            .compactMap { HKObjectType.quantityType(forIdentifier: $0) }
            .forEach { types.insert($0) }
        if let sleep = HKObjectType.categoryType(forIdentifier: .sleepAnalysis) {
            types.insert(sleep)
        }
        types.insert(HKObjectType.workoutType())
        return types
    }

    func requestReadAuthorization() async throws {
        guard isAvailable else {
            throw HealthKitManagerError.unavailable
        }
        do {
            try await store.requestAuthorization(toShare: [], read: readTypes)
            // iOS never tells an app which read types were denied; "completed" means the sheet
            // was answered, not that every type was granted.
            ObservabilityEvents.healthKitAuthorization(outcome: "completed")
            trackReadableData()
        } catch {
            ObservabilityEvents.healthKitAuthorization(outcome: "failed", error: error)
            throw error
        }
    }

    private var hasTrackedReadableData = false

    /// iOS never tells an app which read permissions were denied: a denied type simply returns no
    /// samples. Counting the requested types with any sample in the last 30 days is the closest
    /// signal ("0" = permissions likely denied, or no data at all). Runs at most once per launch.
    func trackReadableData() {
        guard isAvailable, !hasTrackedReadableData else { return }
        hasTrackedReadableData = true
        let types = readTypes.compactMap { $0 as? HKSampleType }
        let store = store
        Task {
            var readable = 0
            for type in types {
                if await Self.hasRecentSample(in: store, of: type) { readable += 1 }
            }
            AppTelemetry.shared.track("healthkit_data_check", [
                "readable_types": String(readable),
                "requested_types": String(types.count)
            ])
        }
    }

    private nonisolated static func hasRecentSample(in store: HKHealthStore, of type: HKSampleType) async -> Bool {
        await withCheckedContinuation { continuation in
            let predicate = HKQuery.predicateForSamples(
                withStart: Date().addingTimeInterval(-30 * 24 * 60 * 60),
                end: nil)
            let query = HKSampleQuery(sampleType: type, predicate: predicate, limit: 1, sortDescriptors: nil) { _, samples, _ in
                continuation.resume(returning: !(samples ?? []).isEmpty)
            }
            store.execute(query)
        }
    }

    func enableAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws {
        guard isAvailable else {
            throw HealthKitManagerError.unavailable
        }
        var initialSyncError: Error?
        do {
            try await synchronizeHealthData()
        } catch {
            initialSyncError = error
        }
        try await syncService.enableBackgroundUpdates { [weak self] in
            guard let self else { return }
            do {
                try await self.synchronizeHealthData()
            } catch {
                NSLog("HealthKit upload failed: %@", error.localizedDescription)
            }
            await onUpdate()
        }
        try keychain.save("true", for: "health_sync_enabled")
        if let initialSyncError { throw initialSyncError }
    }

    func resumeAutomaticSync(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws -> Bool {
        guard isAvailable, try keychain.read("health_sync_enabled") == "true" else {
            return false
        }
        // Permissions can be changed in Settings at any time: re-check once per launch.
        trackReadableData()
        try await enableAutomaticSync(onUpdate: onUpdate)
        return true
    }

    /// Every observer fires on launch and several can fire together later. Running one read pass
    /// at a time (plus one follow-up if more changes arrived meanwhile) avoids reading the same
    /// HealthKit anchors concurrently and uploading duplicate work.
    func synchronizeHealthData() async throws {
        guard isAvailable else {
            throw HealthKitManagerError.unavailable
        }
        if let inFlight = synchronizationTask {
            needsAnotherPass = true
            try await inFlight.value
            return
        }
        let task = Task { @MainActor [weak self] in
            guard let self else { return }
            repeat {
                self.needsAnotherPass = false
                try await self.performSynchronization()
            } while self.needsAnotherPass
        }
        synchronizationTask = task
        defer { synchronizationTask = nil }
        try await task.value
    }

    private func performSynchronization() async throws {
        let userId = try syncWorker.currentUserId()
        let result: (items: [HealthSyncItem], deletions: [HealthSyncDeletion], cursorUpdates: [String: Data])
        do {
            result = try await recentSamplesForSync(userId: userId)
        } catch {
            if !error.isCancellation {
                ObservabilityEvents.healthKitQueryFailed(stage: "read", error: error)
            }
            throw error
        }
        try await syncWorker.enqueueAndUpload(
            result.items,
            deletions: result.deletions,
            cursorUpdates: result.cursorUpdates,
            for: userId)
    }

    func syncOverview() async -> HealthSyncOverview {
        await syncWorker.overview()
    }

    func syncMetrics() async -> HealthSyncMetricsSnapshot {
        await syncWorker.metricsSnapshot()
    }

    func clearPendingSync() async throws {
        try await syncService.disableBackgroundUpdates()
        if try keychain.read("health_sync_enabled") == "true" {
            try keychain.delete("health_sync_enabled")
        }
        try await syncWorker.clearPending()
    }

    func retryPendingSync() async throws {
        try await syncWorker.retryPending()
    }
    private func recentSamplesForSync(userId: String) async throws -> (
        items: [HealthSyncItem],
        deletions: [HealthSyncDeletion],
        cursorUpdates: [String: Data]
    ) {
        var items: [HealthSyncItem] = []
        var deletions: [HealthSyncDeletion] = []
        var cursorUpdates: [String: Data] = [:]

        if let type = HKObjectType.quantityType(forIdentifier: .stepCount) {
            let batch = try await querySamples(type, userId: userId)
            cursorUpdates[type.identifier] = batch.cursorData
            deletions += makeDeletions(batch.deletedSampleIds, userId: userId)
            items += batch.samples.compactMap { sample in
                guard let quantity = sample as? HKQuantitySample else { return nil }
                return HealthSyncItem(
                    id: sample.uuid,
                    metric: "steps",
                    value: quantity.quantity.doubleValue(for: .count()),
                    unit: "count",
                    observedAt: sample.endDate)
            }
        }
        if let type = HKObjectType.quantityType(forIdentifier: .bodyMass) {
            let batch = try await querySamples(type, userId: userId)
            cursorUpdates[type.identifier] = batch.cursorData
            deletions += makeDeletions(batch.deletedSampleIds, userId: userId)
            items += batch.samples.compactMap { sample in
                guard let quantity = sample as? HKQuantitySample else { return nil }
                return HealthSyncItem(
                    id: sample.uuid,
                    metric: "body_mass",
                    value: quantity.quantity.doubleValue(for: .gramUnit(with: .kilo)),
                    unit: "kg",
                    observedAt: sample.endDate)
            }
        }
        if let type = HKObjectType.quantityType(forIdentifier: .heartRate) {
            let batch = try await querySamples(type, userId: userId)
            cursorUpdates[type.identifier] = batch.cursorData
            deletions += makeDeletions(batch.deletedSampleIds, userId: userId)
            items += batch.samples.compactMap { sample in
                guard let quantity = sample as? HKQuantitySample else { return nil }
                return HealthSyncItem(
                    id: sample.uuid,
                    metric: "heart_rate",
                    value: quantity.quantity.doubleValue(
                        for: HKUnit.count().unitDivided(by: .minute())),
                    unit: "count/min",
                    observedAt: sample.endDate)
            }
        }
        if let type = HKObjectType.quantityType(forIdentifier: .activeEnergyBurned) {
            let batch = try await querySamples(type, userId: userId)
            cursorUpdates[type.identifier] = batch.cursorData
            deletions += makeDeletions(batch.deletedSampleIds, userId: userId)
            items += batch.samples.compactMap { sample in
                guard let quantity = sample as? HKQuantitySample else { return nil }
                return HealthSyncItem(
                    id: sample.uuid,
                    metric: "active_energy",
                    value: quantity.quantity.doubleValue(for: .kilocalorie()),
                    unit: "kcal",
                    observedAt: sample.endDate)
            }
        }
        if let type = HKObjectType.quantityType(forIdentifier: .basalEnergyBurned) {
            let batch = try await querySamples(type, userId: userId)
            cursorUpdates[type.identifier] = batch.cursorData
            deletions += makeDeletions(batch.deletedSampleIds, userId: userId)
            items += batch.samples.compactMap { sample in
                guard let quantity = sample as? HKQuantitySample else { return nil }
                return HealthSyncItem(
                    id: sample.uuid,
                    metric: "basal_energy",
                    value: quantity.quantity.doubleValue(for: .kilocalorie()),
                    unit: "kcal",
                    observedAt: sample.endDate)
            }
        }
        if let type = HKObjectType.categoryType(forIdentifier: .sleepAnalysis) {
            let batch = try await querySamples(type, userId: userId)
            cursorUpdates[type.identifier] = batch.cursorData
            deletions += makeDeletions(batch.deletedSampleIds, userId: userId)
            let asleepValues: Set<Int> = [
                HKCategoryValueSleepAnalysis.asleepUnspecified.rawValue,
                HKCategoryValueSleepAnalysis.asleepCore.rawValue,
                HKCategoryValueSleepAnalysis.asleepDeep.rawValue,
                HKCategoryValueSleepAnalysis.asleepREM.rawValue
            ]
            items += batch.samples.compactMap { sample in
                guard let category = sample as? HKCategorySample,
                      asleepValues.contains(category.value)
                else { return nil }
                return HealthSyncItem(
                    id: sample.uuid,
                    metric: "sleep_duration",
                    value: sample.endDate.timeIntervalSince(sample.startDate),
                    unit: "s",
                    observedAt: sample.endDate)
            }
        }

        let workoutType = HKObjectType.workoutType()
        let workouts = try await querySamples(workoutType, userId: userId)
        cursorUpdates[workoutType.identifier] = workouts.cursorData
        deletions += makeDeletions(workouts.deletedSampleIds, userId: userId)
        items += workouts.samples.compactMap { sample in
            guard let workout = sample as? HKWorkout else { return nil }
            return HealthSyncItem(
                id: sample.uuid,
                metric: "workout_duration",
                value: workout.duration,
                unit: "s",
                observedAt: sample.endDate)
        }
        return (items, deletions, cursorUpdates)
    }

    private func makeDeletions(_ ids: [UUID], userId: String) -> [HealthSyncDeletion] {
        ids.map {
            HealthSyncDeletion(
                id: $0,
                userId: userId,
                source: "apple_health",
                externalId: $0.uuidString,
                deviceId: deviceId)
        }
    }

    private func querySamples(
        _ sampleType: HKSampleType,
        userId: String
    ) async throws -> AnchoredHealthSamples {
        let previousCursorData = try await syncWorker.anchorData(
            for: sampleType.identifier,
            userId: userId)
        let previousCursor: StoredHealthAnchor?
        let anchor: HKQueryAnchor?
        if let previousCursorData {
            let decodedCursor = try JSONDecoder().decode(StoredHealthAnchor.self, from: previousCursorData)
            previousCursor = decodedCursor
            guard let decodedAnchor = try NSKeyedUnarchiver.unarchivedObject(
                ofClass: HKQueryAnchor.self,
                from: decodedCursor.encodedAnchor)
            else {
                throw HealthKitManagerError.invalidSyncAnchor
            }
            anchor = decodedAnchor
        } else {
            previousCursor = nil
            anchor = nil
        }

        let initialHistoryYears = sampleType.identifier == HKQuantityTypeIdentifier.bodyMass.rawValue ? -20 : 0
        let initialHistoryDays = initialHistoryYears == 0 ? -30 : 0
        let initialWindow = initialHistoryYears == 0
            ? Calendar.current.date(byAdding: .day, value: initialHistoryDays, to: Date())
            : Calendar.current.date(byAdding: .year, value: initialHistoryYears, to: Date())
        let windowStart = previousCursor?.windowStart
            ?? initialWindow
            ?? Date()
        let predicate = HKQuery.predicateForSamples(withStart: windowStart, end: nil)
        let (samples, deletedSampleIds, nextAnchor) = try await withCheckedThrowingContinuation {
            (continuation: CheckedContinuation<([HKSample], [UUID], HKQueryAnchor), Error>) in
            let query = HKAnchoredObjectQuery(
                type: sampleType,
                predicate: predicate,
                anchor: anchor,
                limit: HKObjectQueryNoLimit
            ) { _, samples, deletedObjects, newAnchor, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if let newAnchor {
                    continuation.resume(returning: (
                        samples ?? [],
                        (deletedObjects ?? []).map(\.uuid),
                        newAnchor))
                } else {
                    continuation.resume(throwing: HealthKitManagerError.invalidSyncAnchor)
                }
            }
            store.execute(query)
        }

        let encodedAnchor = try NSKeyedArchiver.archivedData(
            withRootObject: nextAnchor,
            requiringSecureCoding: true)
        let cursor = StoredHealthAnchor(
            encodedAnchor: encodedAnchor,
            windowStart: windowStart)
        return AnchoredHealthSamples(
            samples: samples,
            deletedSampleIds: deletedSampleIds,
            cursorData: try JSONEncoder().encode(cursor))
    }

    func fetchTodaySummary() async throws -> HealthSummary {
        guard isAvailable else {
            throw HealthKitManagerError.unavailable
        }

        let now = Date()
        let startOfDay = Calendar.current.startOfDay(for: now)
        return try await syncWorker.fetchSummary(from: startOfDay, to: now)
    }
}
