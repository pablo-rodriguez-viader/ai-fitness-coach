import Foundation
import UIKit

/// HealthKit and AI events for the beta dashboards. Like the rest of the telemetry they carry
/// only names, outcomes, error codes and coarse durations: never health values or user text.
enum ObservabilityEvents {
    // MARK: HealthKit

    static func healthKitAuthorization(outcome: String, error: Error? = nil) {
        var properties = ["outcome": outcome]
        if let error {
            properties.merge(errorProperties(error)) { $1 }
            // HealthKit's own text (e.g. "Missing com.apple.developer.healthkit entitlement")
            // tells signing problems apart from a user refusal. It never contains health values.
            if (error as NSError).domain == "com.apple.healthkit" {
                properties["reason"] = String(error.localizedDescription.prefix(120))
            }
        }
        AppTelemetry.shared.track("healthkit_authorization", properties)
    }

    static func healthKitQueryFailed(stage: String, typeIdentifier: String? = nil, error: Error) {
        var properties = errorProperties(error)
        properties["stage"] = stage
        if let typeIdentifier { properties["type"] = shortTypeName(typeIdentifier) }
        AppTelemetry.shared.track("healthkit_query_failed", properties)
    }

    /// Observers can fire many times an hour; one event per type and hour is enough to see
    /// whether background delivery works without filling the telemetry buffer.
    @MainActor
    static func healthKitObserverUpdate(typeIdentifier: String, now: Date = .now) {
        let type = shortTypeName(typeIdentifier)
        if let last = lastObserverEvent[type], now.timeIntervalSince(last) < 3600 { return }
        lastObserverEvent[type] = now
        let state = UIApplication.shared.applicationState == .background ? "background" : "foreground"
        AppTelemetry.shared.track("healthkit_observer_update", ["type": type, "app_state": state])
    }

    @MainActor private static var lastObserverEvent: [String: Date] = [:]

    // MARK: AI

    /// One event per AI request made from the app (Coach, Análisis, plan de entrenamiento).
    static func aiRequest(feature: String, outcome: String, startedAt: Date, error: Error? = nil) {
        var properties = [
            "feature": feature,
            "outcome": outcome,
            "duration_bucket": durationBucket(Date().timeIntervalSince(startedAt))
        ]
        if let error { properties.merge(errorProperties(error)) { $1 } }
        AppTelemetry.shared.track("ai_request", properties)
    }

    static func aiOutcome(for error: Error) -> String {
        switch error.appErrorKind {
        case .cancelled: "cancelled"
        case .offline: "offline"
        case .unavailable: "unavailable"
        default: "failed"
        }
    }

    // MARK: Helpers

    static func durationBucket(_ seconds: TimeInterval) -> String {
        switch seconds {
        case ..<2: "lt_2s"
        case ..<5: "2_5s"
        case ..<10: "5_10s"
        case ..<20: "10_20s"
        case ..<40: "20_40s"
        default: "gte_40s"
        }
    }

    static func shortTypeName(_ identifier: String) -> String {
        let name = identifier
            .replacingOccurrences(of: "HKQuantityTypeIdentifier", with: "")
            .replacingOccurrences(of: "HKCategoryTypeIdentifier", with: "")
            .replacingOccurrences(of: "HKWorkoutTypeIdentifier", with: "Workout")
        return String(name.prefix(40))
    }

    private static func errorProperties(_ error: Error) -> [String: String] {
        let nsError = error as NSError
        return ["domain": String(nsError.domain.suffix(60)), "code": String(nsError.code)]
    }
}
