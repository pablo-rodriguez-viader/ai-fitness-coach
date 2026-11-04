import Foundation
import MetricKit
import os

/// Receives Apple's on-device diagnostics (crashes and hangs from the previous run) and forwards
/// a summary to the beta telemetry: exception type, signal and termination reason only. Call
/// stacks and any health data stay on the device.
final class CrashDiagnostics: NSObject, MXMetricManagerSubscriber {
    private let logger = Logger(
        subsystem: Bundle.main.bundleIdentifier ?? "AIFitnessCoach",
        category: "Diagnostics")

    func start() {
        // MetricKit delivers each payload once, on a later launch; the telemetry file keeps the
        // summary until it can be uploaded.
        MXMetricManager.shared.add(self)
    }

    func didReceive(_ payloads: [MXDiagnosticPayload]) {
        let crashes = payloads.flatMap { $0.crashDiagnostics ?? [] }
        let hangs = payloads.flatMap { $0.hangDiagnostics ?? [] }
        logger.error("MetricKit diagnostics received: crashes=\(crashes.count), hangs=\(hangs.count)")
        for crash in crashes.prefix(10) {
            AppTelemetry.shared.track("crash_report", Self.properties(for: crash))
        }
        if !hangs.isEmpty {
            let longest = hangs.map { $0.hangDuration.converted(to: .seconds).value }.max() ?? 0
            AppTelemetry.shared.track("hang_report", [
                "count": String(hangs.count),
                "longest_seconds": String(format: "%.1f", longest)
            ])
        }
    }

    func didReceive(_ payloads: [MXMetricPayload]) {
        logger.info("MetricKit daily metric reports received: count=\(payloads.count)")
    }

    static func properties(for crash: MXCrashDiagnostic) -> [String: String] {
        var properties = ["count": "1"]
        if let exceptionType = crash.exceptionType {
            properties["exception_type"] = exceptionType.stringValue
        }
        if let exceptionCode = crash.exceptionCode {
            properties["exception_code"] = exceptionCode.stringValue
        }
        if let signal = crash.signal {
            properties["signal"] = signal.stringValue
        }
        if let reason = crash.terminationReason {
            properties["termination_reason"] = String(reason.prefix(100))
        }
        properties["app_build"] = crash.metaData.applicationBuildVersion
        properties["os"] = crash.metaData.osVersion
        return properties
    }
}
