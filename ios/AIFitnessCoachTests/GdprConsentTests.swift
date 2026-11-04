import XCTest
@testable import AIFitnessCoach

/// GDPR on the device: explicit consents (health data, analytics), telemetry that waits for the
/// analytics decision, and erasure of health or AI data without closing the account.
@MainActor
final class GdprConsentTests: XCTestCase {
    private var fileURL: URL!

    override func setUp() {
        super.setUp()
        fileURL = FileManager.default.temporaryDirectory.appending(path: "consents-\(UUID().uuidString).json")
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: fileURL)
        super.tearDown()
    }

    // MARK: ConsentStore

    func testNothingCountsAsConsentUntilTheUserDecides() {
        let store = ConsentStore(repository: StubAccountRepository(), fileURL: fileURL, onAnalyticsChange: { _ in })

        XCTAssertEqual(store.decision(.healthData), .undecided)
        XCTAssertEqual(store.decision(.analytics), .undecided)
        XCTAssertFalse(store.isGranted(.healthData))
        XCTAssertFalse(store.hasLoaded)
    }

    func testServerRecordsAreLoadedAndAnOlderTextVersionIsAskedAgain() async {
        let repository = StubAccountRepository()
        await repository.setServerConsents([
            ConsentRecord(kind: "health_data", granted: true, version: ConsentKind.healthData.textVersion),
            ConsentRecord(kind: "analytics", granted: true, version: 0),
            ConsentRecord(kind: "marketing", granted: true, version: 1)
        ])
        let store = ConsentStore(repository: repository, fileURL: fileURL, onAnalyticsChange: { _ in })

        await store.load()

        XCTAssertTrue(store.hasLoaded)
        XCTAssertTrue(store.isGranted(.healthData))
        XCTAssertEqual(store.decision(.analytics), .undecided, "A consent to an older text must be given again.")
    }

    func testADecisionIsStoredOnTheServerAndSurvivesARelaunch() async {
        let repository = StubAccountRepository()
        var analyticsDecisions: [ConsentDecision] = []
        let store = ConsentStore(repository: repository, fileURL: fileURL, onAnalyticsChange: { analyticsDecisions.append($0) })

        let saved = await store.set(.analytics, granted: false)
        let healthSaved = await store.set(.healthData, granted: true)

        XCTAssertTrue(saved)
        XCTAssertTrue(healthSaved)
        let server = await repository.serverConsents
        XCTAssertEqual(Set(server.map(\.kind)), ["analytics", "health_data"])
        XCTAssertEqual(analyticsDecisions, [.undecided, .denied])

        let relaunched = ConsentStore(repository: repository, fileURL: fileURL, onAnalyticsChange: { _ in })
        XCTAssertEqual(relaunched.decision(.analytics), .denied)
        XCTAssertTrue(relaunched.isGranted(.healthData))
    }

    func testOfflineLoadKeepsTheLastKnownDecisions() async {
        let repository = StubAccountRepository()
        let first = ConsentStore(repository: repository, fileURL: fileURL, onAnalyticsChange: { _ in })
        await first.set(.healthData, granted: true)
        await repository.setConsentsError(URLError(.notConnectedToInternet))

        let offline = ConsentStore(repository: repository, fileURL: fileURL, onAnalyticsChange: { _ in })
        await offline.load()

        XCTAssertTrue(offline.isGranted(.healthData))
        XCTAssertTrue(offline.hasLoaded, "Cached decisions are usable offline.")
    }

    func testSignOutForgetsTheDecisions() async {
        let store = ConsentStore(repository: StubAccountRepository(), fileURL: fileURL, onAnalyticsChange: { _ in })
        await store.set(.healthData, granted: true)

        store.reset()

        XCTAssertEqual(store.decision(.healthData), .undecided)
        let relaunched = ConsentStore(repository: StubAccountRepository(), fileURL: fileURL, onAnalyticsChange: { _ in })
        XCTAssertEqual(relaunched.decision(.healthData), .undecided)
    }

    func testConsentTextVersionsAreSentWithTheDecision() {
        XCTAssertEqual(ConsentKind.healthData.rawValue, "health_data")
        XCTAssertEqual(ConsentKind.analytics.rawValue, "analytics")
        XCTAssertGreaterThanOrEqual(ConsentKind.healthData.textVersion, 1)
    }

    // MARK: Telemetry and the analytics consent

    func testTelemetryWaitsForTheDecisionAndDropsEverythingWhenDenied() async {
        let uploader = CountingUploader()
        let telemetry = AppTelemetry(fileURL: nil, requiresConsent: true)
        await telemetry.configure(uploader: uploader)

        await telemetry.append(.init(name: "crash_report", occurredAt: .now, properties: [:]))
        await telemetry.flush()
        var uploaded = await uploader.events
        XCTAssertEqual(uploaded, 0, "Nothing is sent before the user decides.")
        var pending = await telemetry.pendingEvents()
        XCTAssertEqual(pending.count, 1, "Undecided keeps a local buffer.")

        await telemetry.setUploadDecision(.denied)
        await telemetry.append(.init(name: "screen_view", occurredAt: .now, properties: [:]))
        pending = await telemetry.pendingEvents()
        XCTAssertTrue(pending.isEmpty, "Denied drops the buffer and new events.")

        await telemetry.setUploadDecision(.granted)
        await telemetry.append(.init(name: "screen_view", occurredAt: .now, properties: [:]))
        await telemetry.flush()
        // Granting also starts a flush of its own; wait for whichever one uploads.
        var attempts = 0
        while await uploader.events == 0, attempts < 100 {
            attempts += 1
            try? await Task.sleep(nanoseconds: 10_000_000)
        }
        uploaded = await uploader.events
        XCTAssertEqual(uploaded, 1)
    }

    // MARK: Erasure by category

    func testDeletingHealthDataWithdrawsConsentAndStopsLocalSync() async {
        let repository = StubAccountRepository()
        let consents = ConsentStore(repository: repository, fileURL: fileURL, onAnalyticsChange: { _ in })
        await consents.set(.healthData, granted: true)
        var localCleanups = 0
        let viewModel = SettingsViewModel(
            repository: repository,
            diagnostics: { [:] },
            onAccountDeleted: {},
            revokeAIConsent: nil,
            consents: consents,
            onHealthDataDeleted: { localCleanups += 1 })

        await viewModel.deleteHealthData()

        let deleted = await repository.deletedCategories
        XCTAssertEqual(deleted, ["health"])
        XCTAssertEqual(consents.decision(.healthData), .denied)
        XCTAssertEqual(localCleanups, 1)
        XCTAssertNil(viewModel.deletingData)
        XCTAssertNotNil(viewModel.dataDeletionMessage)
    }

    func testFailedHealthDeletionKeepsConsentAndLocalSync() async {
        let repository = StubAccountRepository(deletionError: AccountError.server(statusCode: 500))
        let consents = ConsentStore(repository: repository, fileURL: fileURL, onAnalyticsChange: { _ in })
        await consents.set(.healthData, granted: true)
        var localCleanups = 0
        let viewModel = SettingsViewModel(
            repository: repository,
            diagnostics: { [:] },
            onAccountDeleted: {},
            revokeAIConsent: nil,
            consents: consents,
            onHealthDataDeleted: { localCleanups += 1 })

        await viewModel.deleteHealthData()

        XCTAssertTrue(consents.isGranted(.healthData), "The server still has the data: nothing changes locally.")
        XCTAssertEqual(localCleanups, 0)
        XCTAssertNotNil(viewModel.dataDeletionMessage)
    }

    func testDeletingAIDataCallsTheServer() async {
        let repository = StubAccountRepository()
        let viewModel = SettingsViewModel(
            repository: repository, diagnostics: { [:] }, onAccountDeleted: {}, revokeAIConsent: nil)

        await viewModel.deleteAIData()

        let deleted = await repository.deletedCategories
        XCTAssertEqual(deleted, ["ai"])
        XCTAssertNotNil(viewModel.dataDeletionMessage)
    }
}

private actor CountingUploader: TelemetryUploader {
    private(set) var events = 0

    func send(_ batch: [AppTelemetry.Event]) async throws {
        events += batch.count
    }
}
