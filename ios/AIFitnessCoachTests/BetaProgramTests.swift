import XCTest
@testable import AIFitnessCoach

/// Private beta: feedback prompt for testers and the "beta is full" sign-in answer.
@MainActor
final class BetaProgramTests: XCTestCase {
    private var fileURL: URL!
    private let calendar = Calendar(identifier: .gregorian)

    override func setUp() {
        super.setUp()
        fileURL = FileManager.default.temporaryDirectory.appending(path: "beta-prompt-\(UUID().uuidString).json")
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: fileURL)
        super.tearDown()
    }

    func testPromptAppearsOnTheThirdActiveDayOfABetaBuild() {
        let prompt = BetaFeedbackPrompt(fileURL: fileURL, environment: .beta)
        let start = Date(timeIntervalSince1970: 1_800_000_000)

        prompt.recordActiveDay(now: start, calendar: calendar, isEnabled: true)
        prompt.recordActiveDay(now: start.addingTimeInterval(3600), calendar: calendar, isEnabled: true)
        prompt.recordActiveDay(now: start.addingTimeInterval(86_400), calendar: calendar, isEnabled: true)
        XCTAssertFalse(prompt.isVisible, "Two distinct days are not enough.")

        prompt.recordActiveDay(now: start.addingTimeInterval(2 * 86_400), calendar: calendar, isEnabled: true)
        XCTAssertTrue(prompt.isVisible)
        XCTAssertEqual(prompt.state.timesShown, 1)
    }

    func testPromptNeverShowsOutsideBetaOrWhenTheFlagIsOff() {
        var state = BetaFeedbackPrompt.State()
        state.activeDays = ["2026-10-01", "2026-10-02", "2026-10-03"]

        XCTAssertFalse(BetaFeedbackPrompt.shouldShow(state, environment: .production, isEnabled: true, now: .now))
        XCTAssertFalse(BetaFeedbackPrompt.shouldShow(state, environment: .development, isEnabled: true, now: .now))
        XCTAssertFalse(BetaFeedbackPrompt.shouldShow(state, environment: .beta, isEnabled: false, now: .now))
        XCTAssertTrue(BetaFeedbackPrompt.shouldShow(state, environment: .beta, isEnabled: true, now: .now))
    }

    func testDismissedPromptWaitsTwoWeeksAndSendingStopsIt() {
        let now = Date()
        var state = BetaFeedbackPrompt.State()
        state.activeDays = ["2026-10-01", "2026-10-02", "2026-10-03"]
        state.timesShown = 1
        state.lastShownAt = now.addingTimeInterval(-3 * 86_400)
        XCTAssertFalse(BetaFeedbackPrompt.shouldShow(state, environment: .beta, isEnabled: true, now: now))

        state.lastShownAt = now.addingTimeInterval(-15 * 86_400)
        XCTAssertTrue(BetaFeedbackPrompt.shouldShow(state, environment: .beta, isEnabled: true, now: now))

        state.timesShown = BetaFeedbackPrompt.maximumTimesShown
        XCTAssertFalse(BetaFeedbackPrompt.shouldShow(state, environment: .beta, isEnabled: true, now: now))
    }

    func testPromptStateSurvivesARelaunchAndResetClearsIt() {
        let start = Date(timeIntervalSince1970: 1_800_000_000)
        let first = BetaFeedbackPrompt(fileURL: fileURL, environment: .beta)
        for day in 0..<3 {
            first.recordActiveDay(now: start.addingTimeInterval(Double(day) * 86_400), calendar: calendar, isEnabled: true)
        }
        first.dismiss(sentFeedback: true)

        let relaunched = BetaFeedbackPrompt(fileURL: fileURL, environment: .beta)
        XCTAssertEqual(relaunched.state.timesShown, BetaFeedbackPrompt.maximumTimesShown)
        relaunched.reset()
        XCTAssertEqual(BetaFeedbackPrompt(fileURL: fileURL, environment: .beta).state, BetaFeedbackPrompt.State())
    }

    func testBetaFullAnswerIsRecognisedAndExplained() {
        let body = Data(#"{"status":403,"title":"The private beta is full.","code":"beta_full"}"#.utf8)
        XCTAssertTrue(AuthenticationService.isBetaFull(body))
        XCTAssertFalse(AuthenticationService.isBetaFull(Data(#"{"status":403}"#.utf8)))
        XCTAssertFalse(AuthenticationService.isBetaFull(Data("not json".utf8)))
        XCTAssertTrue(AuthenticationError.betaFull.localizedDescription.contains("beta privada está completa"))
        XCTAssertEqual(AuthenticationError.betaFull.appErrorKind, .failure, "A full beta is not an expired session.")
    }

    func testBetaFeedbackIsARemoteFlag() {
        XCTAssertEqual(FeatureFlag.betaFeedback.rawValue, "beta_feedback")
        XCTAssertTrue(AppConfig.defaults.isEnabled(.betaFeedback))
    }
}
