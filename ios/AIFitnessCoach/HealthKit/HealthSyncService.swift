import Foundation
import HealthKit

@MainActor
final class HealthSyncService {
    private let store = HKHealthStore()
    private var observers: [Int: HKObserverQuery] = [:]

    func enableBackgroundUpdates(
        onUpdate: @escaping @MainActor @Sendable () async -> Void
    ) async throws {
        guard HKHealthStore.isHealthDataAvailable() else {
            throw HealthKitManagerError.unavailable
        }

        let types = Self.observedTypes

        for (index, type) in types.enumerated() where observers[index] == nil {
            let typeIdentifier = type.identifier
            let observer = HKObserverQuery(sampleType: type, predicate: nil) { _, completion, error in
                if let error {
                    NSLog("HealthKit observer failed: %@", error.localizedDescription)
                    ObservabilityEvents.healthKitQueryFailed(
                        stage: "observer", typeIdentifier: typeIdentifier, error: error)
                    completion()
                    return
                }

                Task { @MainActor in
                    ObservabilityEvents.healthKitObserverUpdate(typeIdentifier: typeIdentifier)
                    await onUpdate()
                    completion()
                }
            }
            observers[index] = observer
            store.execute(observer)

            // Free Apple ID builds cannot carry the background-delivery entitlement: observers
            // then run while the app is open and sync happens on launch and foreground.
            guard Self.supportsBackgroundDelivery else { continue }
            do {
                try await enableBackgroundDelivery(for: type)
            } catch {
                ObservabilityEvents.healthKitQueryFailed(
                    stage: "background_delivery", typeIdentifier: typeIdentifier, error: error)
                store.stop(observer)
                observers.removeValue(forKey: index)
                throw error
            }
        }
    }

    func disableBackgroundUpdates() async throws {
        let activeObservers = Array(observers.values)
        observers.removeAll()
        for observer in activeObservers {
            store.stop(observer)
        }
        guard Self.supportsBackgroundDelivery else { return }
        for type in Self.observedTypes {
            try await disableBackgroundDelivery(for: type)
        }
    }

    /// False in the Personal build (`NO_HEALTHKIT_BACKGROUND_DELIVERY`), installed with a free Apple ID.
    nonisolated static var supportsBackgroundDelivery: Bool {
        #if NO_HEALTHKIT_BACKGROUND_DELIVERY
        false
        #else
        true
        #endif
    }

    private static var observedTypes: [HKSampleType] {
        var types: [HKSampleType] = []
        if let steps = HKObjectType.quantityType(forIdentifier: .stepCount) {
            types.append(steps)
        }
        if let weight = HKObjectType.quantityType(forIdentifier: .bodyMass) {
            types.append(weight)
        }
        if let heartRate = HKObjectType.quantityType(forIdentifier: .heartRate) {
            types.append(heartRate)
        }
        if let energy = HKObjectType.quantityType(forIdentifier: .activeEnergyBurned) {
            types.append(energy)
        }
        if let sleep = HKObjectType.categoryType(forIdentifier: .sleepAnalysis) {
            types.append(sleep)
        }
        types.append(HKObjectType.workoutType())
        return types
    }

    private func enableBackgroundDelivery(for type: HKSampleType) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            store.enableBackgroundDelivery(for: type, frequency: .hourly) { success, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if success {
                    continuation.resume()
                } else {
                    continuation.resume(throwing: HealthSyncError.backgroundDeliveryRejected)
                }
            }
        }
    }

    private func disableBackgroundDelivery(for type: HKSampleType) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            store.disableBackgroundDelivery(for: type) { success, error in
                if let error {
                    continuation.resume(throwing: error)
                } else if success {
                    continuation.resume()
                } else {
                    continuation.resume(throwing: HealthSyncError.backgroundDeliveryRejected)
                }
            }
        }
    }
}
