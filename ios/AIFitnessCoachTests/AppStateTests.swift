import XCTest
@testable import AIFitnessCoach

/// Error classification, deep links and telemetry: the shared pieces every screen relies on.
final class AppStateTests: XCTestCase {
    func testErrorsAreClassifiedConsistently() {
        XCTAssertEqual(URLError(.cancelled).appErrorKind, .cancelled)
        XCTAssertEqual(CancellationError().appErrorKind, .cancelled)
        XCTAssertEqual(URLError(.notConnectedToInternet).appErrorKind, .offline)
        XCTAssertEqual(URLError(.timedOut).appErrorKind, .offline)
        XCTAssertEqual(HealthSyncError.offline.appErrorKind, .offline)
        XCTAssertEqual(HealthSyncError.unauthorized.appErrorKind, .sessionExpired)
        XCTAssertEqual(AuthenticationError.unauthorized.appErrorKind, .sessionExpired)
        XCTAssertEqual(AIChatError.unavailable.appErrorKind, .unavailable)
        XCTAssertEqual(WorkoutRepositoryError.unavailable.appErrorKind, .unavailable)
        XCTAssertEqual(WellnessError.unauthorized.appErrorKind, .sessionExpired)
        XCTAssertEqual(AIChatError.quotaExceeded.appErrorKind, .failure)
    }

    func testCancellationHasNoMessageAndOfflineHasOneWording() {
        XCTAssertTrue(URLError(.cancelled).isCancellation)
        XCTAssertEqual(URLError(.cancelled).userMessage, "")
        XCTAssertEqual(URLError(.notConnectedToInternet).displayMessage,
                       HealthSyncError.offline.displayMessage)
        XCTAssertTrue(AIChatError.quotaExceeded.displayMessage.contains("límite diario"))
    }

    // Regression: a new user opened Nutrition and got "HTTP 400" instead of guidance.
    func testNutritionSetupProblemsAreGuidanceNotErrors() throws {
        let body = Data(#"{"errors":{"profile":["Set a nutrition profile before requesting calorie targets."]},"status":400}"#.utf8)
        let message = try XCTUnwrap(APIValidationProblem.decode(from: body)?.firstMessage)
        let error = WellnessError.validation(message)

        XCTAssertTrue(error.isMissingSetup)
        XCTAssertFalse(error.localizedDescription.contains("HTTP"))
        XCTAssertTrue(WellnessError.validation("A current HealthKit weight measurement is required.").isMissingSetup)
        XCTAssertFalse(WellnessError.validation("Meal type must be Breakfast, Lunch, Dinner, or Snack.").isMissingSetup)
        XCTAssertFalse(WellnessError.server(statusCode: 500).isMissingSetup)
    }

    func testDeepLinksOpenOnlyExistingScreens() {
        XCTAssertEqual(AppDestination(url: URL(string: "aifitnesscoach://coach")!), .coach)
        XCTAssertEqual(AppDestination(url: URL(string: "aifitnesscoach:///nutrition")!), .nutrition)
        XCTAssertEqual(AppDestination(url: URL(string: "AIFITNESSCOACH://Workouts")!), .workouts)
        XCTAssertEqual(AppDestination(url: URL(string: "aifitnesscoach://")!), .dashboard)
        XCTAssertNil(AppDestination(url: URL(string: "aifitnesscoach://settings")!))
        XCTAssertNil(AppDestination(url: URL(string: "https://example.com/coach")!))
    }

    @MainActor
    func testRouterHoldsALinkUntilConsumed() {
        let router = DeepLinkRouter()
        XCTAssertTrue(router.handle(URL(string: "aifitnesscoach://progress")!))
        XCTAssertFalse(router.handle(URL(string: "other://progress")!))

        XCTAssertEqual(router.consume(), .progress)
        XCTAssertNil(router.consume())
    }

    func testTelemetrySurvivesRestartAndUploadsInBatches() async throws {
        let fileURL = FileManager.default.temporaryDirectory.appending(path: "\(UUID().uuidString).json")
        defer { try? FileManager.default.removeItem(at: fileURL) }

        let firstRun = AppTelemetry(fileURL: fileURL)
        for index in 0..<(AppTelemetry.uploadBatchSize + 5) {
            await firstRun.append(.init(name: "screen_view", occurredAt: .now, properties: ["screen": "s\(index)"]))
        }

        let secondRun = AppTelemetry(fileURL: fileURL)
        let restored = await secondRun.pendingEvents()
        XCTAssertEqual(restored.count, AppTelemetry.uploadBatchSize + 5)

        let uploader = RecordingUploader()
        await secondRun.configure(uploader: uploader)
        await secondRun.flush()

        let batches = await uploader.batchSizes
        let pending = await secondRun.pendingEvents()
        XCTAssertEqual(batches, [AppTelemetry.uploadBatchSize, 5])
        XCTAssertTrue(pending.isEmpty)
    }

    func testTelemetryKeepsEventsWhenUploadFailsAndDropsRejectedBatches() async {
        let failing = AppTelemetry(fileURL: nil)
        await failing.append(.init(name: "app_launch", occurredAt: .now, properties: [:]))
        await failing.configure(uploader: RecordingUploader(error: URLError(.notConnectedToInternet)))
        await failing.flush()
        let kept = await failing.pendingEvents()
        XCTAssertEqual(kept.count, 1)

        await failing.configure(uploader: RecordingUploader(error: TelemetryUploadError.rejected))
        await failing.flush()
        let dropped = await failing.pendingEvents()
        XCTAssertTrue(dropped.isEmpty)
    }

    func testTelemetryBufferIsBounded() async {
        let telemetry = AppTelemetry(fileURL: nil)
        for _ in 0..<(AppTelemetry.maximumBufferedEvents + 20) {
            await telemetry.append(.init(name: "app_foreground", occurredAt: .now, properties: [:]))
        }
        let pending = await telemetry.pendingEvents()
        XCTAssertEqual(pending.count, AppTelemetry.maximumBufferedEvents)
    }
}

private actor RecordingUploader: TelemetryUploader {
    private(set) var batchSizes: [Int] = []
    private let error: Error?

    init(error: Error? = nil) {
        self.error = error
    }

    func send(_ events: [AppTelemetry.Event]) async throws {
        if let error { throw error }
        batchSizes.append(events.count)
    }
}
