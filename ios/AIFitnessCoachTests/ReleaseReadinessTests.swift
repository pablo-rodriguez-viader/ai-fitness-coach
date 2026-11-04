import XCTest
@testable import AIFitnessCoach

/// Feature flags, account export/deletion, Feedback Center and the new observability helpers.
@MainActor
final class ReleaseReadinessTests: XCTestCase {
    // MARK: Feature flags

    func testServerConfigDecodesAndUnknownFlagsDefaultToOn() throws {
        let json = Data(#"""
        {"environment":"beta","minimumSupportedBuild":12,"supportEmail":"beta@example.com",
         "privacyPolicyUrl":"https://example.com/privacy","termsUrl":null,
         "features":{"coach":false,"nutrition":true}}
        """#.utf8)

        let config = try JSONDecoder().decode(AppConfig.self, from: json)

        XCTAssertFalse(config.isEnabled(.coach))
        XCTAssertTrue(config.isEnabled(.nutrition))
        XCTAssertTrue(config.isEnabled(.feedbackCenter), "A flag the server did not send must stay on.")
        XCTAssertEqual(config.privacyPolicyUrl?.absoluteString, "https://example.com/privacy")
        XCTAssertNil(config.termsUrl)
        XCTAssertTrue(AppConfig.defaults.isEnabled(.coach))
    }

    func testConfigIsPersistedAndSurvivesAnOfflineLaunch() async throws {
        let fileURL = FileManager.default.temporaryDirectory.appending(path: "config-\(UUID().uuidString).json")
        defer { try? FileManager.default.removeItem(at: fileURL) }
        var remote = AppConfig.defaults
        remote.features = ["workouts": false]
        remote.minimumSupportedBuild = 20

        let online = AppConfigStore(fetcher: StubConfigFetcher(result: .success(remote)), fileURL: fileURL, buildNumber: 15)
        online.refresh(force: true)
        await online.waitForRefresh()
        XCTAssertFalse(online.isEnabled(.workouts))
        XCTAssertTrue(online.requiresUpdate)

        let offline = AppConfigStore(
            fetcher: StubConfigFetcher(result: .failure(URLError(.notConnectedToInternet))),
            fileURL: fileURL,
            buildNumber: 25)
        offline.refresh(force: true)
        await offline.waitForRefresh()
        XCTAssertFalse(offline.isEnabled(.workouts), "The last config received must be used offline.")
        XCTAssertFalse(offline.requiresUpdate)
    }

    func testRefreshIsThrottled() async {
        let fetcher = StubConfigFetcher(result: .success(.defaults))
        let store = AppConfigStore(fetcher: fetcher, fileURL: nil)
        let now = Date()
        store.refresh(force: true, now: now)
        await store.waitForRefresh()
        store.refresh(now: now.addingTimeInterval(60))
        await store.waitForRefresh()
        store.refresh(now: now.addingTimeInterval(AppConfigStore.minimumRefreshInterval + 1))
        await store.waitForRefresh()

        let calls = await fetcher.calls
        XCTAssertEqual(calls, 2)
    }

    // MARK: Support

    func testSupportDraftValidation() {
        var draft = SupportReportDraft(
            kind: .bug, category: "sync", message: "   ", rating: nil, contactEmail: "", includeDiagnostics: true)
        XCTAssertNotNil(draft.validationError)
        draft.message = "Los pasos no se sincronizan."
        XCTAssertNil(draft.validationError)
        draft.contactEmail = "no-es-un-correo"
        XCTAssertNotNil(draft.validationError)
        draft.contactEmail = "tester@example.com"
        XCTAssertNil(draft.validationError)
        draft.message = String(repeating: "a", count: SupportReportDraft.maximumMessageLength + 1)
        XCTAssertNotNil(draft.validationError)
    }

    func testBugReportAttachesDiagnosticsOnlyWhenAllowed() async {
        let repository = StubAccountRepository()
        let viewModel = SettingsViewModel(
            repository: repository,
            diagnostics: { ["build": "14", "sync_pending": "3"] },
            onAccountDeleted: {},
            revokeAIConsent: nil)
        var draft = SupportReportDraft(
            kind: .bug, category: "sync", message: "Falla la sincronización", rating: nil,
            contactEmail: "", includeDiagnostics: true)

        let sentWithDiagnostics = await viewModel.submit(draft)
        draft.includeDiagnostics = false
        viewModel.resetSubmitState()
        let sentWithout = await viewModel.submit(draft)

        XCTAssertTrue(sentWithDiagnostics)
        XCTAssertTrue(sentWithout)
        let submitted = await repository.submitted
        XCTAssertEqual(submitted.count, 2)
        XCTAssertEqual(submitted[0].diagnostics["sync_pending"], "3")
        XCTAssertTrue(submitted[1].diagnostics.isEmpty)
        XCTAssertEqual(viewModel.submitState, .sent)
    }

    func testInvalidReportIsNotSent() async {
        let repository = StubAccountRepository()
        let viewModel = SettingsViewModel(
            repository: repository, diagnostics: { [:] }, onAccountDeleted: {}, revokeAIConsent: nil)

        let sent = await viewModel.submit(SupportReportDraft(
            kind: .feedback, category: "general", message: "", rating: 5, contactEmail: "", includeDiagnostics: false))

        XCTAssertFalse(sent)
        let submitted = await repository.submitted
        XCTAssertTrue(submitted.isEmpty)
        guard case .failed = viewModel.submitState else { return XCTFail("Expected a validation message.") }
    }

    // MARK: Account deletion

    func testSuccessfulDeletionClearsLocalStateOnce() async {
        let repository = StubAccountRepository()
        var cleanups = 0
        let viewModel = SettingsViewModel(
            repository: repository, diagnostics: { [:] }, onAccountDeleted: { cleanups += 1 }, revokeAIConsent: nil)

        let deleted = await viewModel.deleteAccount(appleAuthorizationCode: "apple-code")

        XCTAssertTrue(deleted)
        XCTAssertEqual(cleanups, 1)
        let codes = await repository.deletionCodes
        XCTAssertEqual(codes, ["apple-code"])
        XCTAssertNil(viewModel.deletionError)
    }

    func testFailedDeletionKeepsTheSessionAndShowsWhy() async {
        let repository = StubAccountRepository(deletionError: AccountError.server(statusCode: 500))
        var cleanups = 0
        let viewModel = SettingsViewModel(
            repository: repository, diagnostics: { [:] }, onAccountDeleted: { cleanups += 1 }, revokeAIConsent: nil)

        let deleted = await viewModel.deleteAccount(appleAuthorizationCode: nil)

        XCTAssertFalse(deleted)
        XCTAssertEqual(cleanups, 0, "Local data must stay while the server still has the account.")
        XCTAssertNotNil(viewModel.deletionError)
        XCTAssertFalse(viewModel.isDeleting)
    }

    func testAccountErrorsAreClassified() {
        XCTAssertEqual(AccountError.unauthorized.appErrorKind, .sessionExpired)
        XCTAssertEqual(AccountError.server(statusCode: 503).appErrorKind, .unavailable)
        XCTAssertEqual(AccountError.validation("x").appErrorKind, .failure)
    }

    func testExportFileLivesInAProtectedTemporaryFolder() throws {
        let url = try AccountRepository.exportFileURL(now: Date(timeIntervalSince1970: 0))
        XCTAssertEqual(url.deletingLastPathComponent().lastPathComponent, "AccountExport")
        XCTAssertEqual(url.pathExtension, "json")
        AccountRepository.removeExports()
        XCTAssertFalse(FileManager.default.fileExists(atPath: AccountRepository.exportDirectory().path()))
    }

    // MARK: Observability

    func testAIDurationsAreBucketed() {
        XCTAssertEqual(ObservabilityEvents.durationBucket(0.5), "lt_2s")
        XCTAssertEqual(ObservabilityEvents.durationBucket(7), "5_10s")
        XCTAssertEqual(ObservabilityEvents.durationBucket(90), "gte_40s")
        XCTAssertEqual(ObservabilityEvents.aiOutcome(for: URLError(.notConnectedToInternet)), "offline")
        XCTAssertEqual(ObservabilityEvents.aiOutcome(for: AIChatError.unavailable), "unavailable")
    }

    func testHealthKitTypeNamesAreShortened() {
        XCTAssertEqual(ObservabilityEvents.shortTypeName("HKQuantityTypeIdentifierStepCount"), "StepCount")
        XCTAssertEqual(ObservabilityEvents.shortTypeName("HKCategoryTypeIdentifierSleepAnalysis"), "SleepAnalysis")
    }

    func testDiagnosticsNeverContainHealthValues() async {
        var config = AppConfig.defaults
        config.features = ["coach": false]
        let values = await DiagnosticsSnapshot.collect(
            isOnline: false,
            healthConnection: "connected",
            syncOverview: HealthSyncOverview(state: .idle, pendingCount: 4, lastSuccessAt: Date().addingTimeInterval(-600)),
            config: config)

        XCTAssertEqual(values["network"], "offline")
        XCTAssertEqual(values["sync_pending"], "4")
        XCTAssertEqual(values["flags_disabled"], "coach")
        XCTAssertEqual(values["sync_last_success_minutes_ago"], "10")
        XCTAssertNil(values["steps"])
    }
}

private actor StubConfigFetcher: AppConfigFetching {
    private let result: Result<AppConfig, Error>
    private(set) var calls = 0

    init(result: Result<AppConfig, Error>) {
        self.result = result
    }

    func fetch() async throws -> AppConfig {
        calls += 1
        return try result.get()
    }
}

actor StubAccountRepository: AccountRepositoryProtocol {
    struct Submitted {
        let draft: SupportReportDraft
        let diagnostics: [String: String]
    }

    private let deletionError: Error?
    private(set) var submitted: [Submitted] = []
    private(set) var deletionCodes: [String?] = []

    init(deletionError: Error? = nil) {
        self.deletionError = deletionError
    }

    func exportData() async throws -> URL {
        FileManager.default.temporaryDirectory.appending(path: "export.json")
    }

    func deleteAccount(appleAuthorizationCode: String?) async throws {
        deletionCodes.append(appleAuthorizationCode)
        if let deletionError { throw deletionError }
    }

    func submit(_ draft: SupportReportDraft, diagnostics: [String: String]) async throws {
        submitted.append(Submitted(draft: draft, diagnostics: diagnostics))
    }

    private(set) var serverConsents: [ConsentRecord] = []
    private(set) var deletedCategories: [String] = []

    func setServerConsents(_ records: [ConsentRecord]) {
        serverConsents = records
    }

    private var consentsError: Error?

    func setConsentsError(_ error: Error?) {
        consentsError = error
    }

    func consents() async throws -> [ConsentRecord] {
        if let consentsError { throw consentsError }
        return serverConsents
    }

    func setConsent(_ kind: ConsentKind, granted: Bool) async throws {
        serverConsents.removeAll { $0.kind == kind.rawValue }
        serverConsents.append(ConsentRecord(kind: kind.rawValue, granted: granted, version: kind.textVersion))
    }

    func deleteAIData() async throws {
        deletedCategories.append("ai")
    }

    func deleteHealthData() async throws {
        if let deletionError { throw deletionError }
        deletedCategories.append("health")
    }
}
