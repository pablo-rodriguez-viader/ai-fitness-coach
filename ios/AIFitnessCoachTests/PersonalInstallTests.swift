import XCTest
@testable import AIFitnessCoach

/// Personal install (free Apple ID): access-code sign-in and no HealthKit background delivery,
/// only in the Personal configuration. The tests run in Debug, where both stay as in the store app.
@MainActor
final class PersonalInstallTests: XCTestCase {
    func testStoreBuildsKeepSignInWithAppleAndBackgroundDelivery() {
        XCTAssertFalse(AuthenticationViewModel.usesAccessCode)
        XCTAssertTrue(HealthSyncService.supportsBackgroundDelivery)
    }

    func testAccessCodeErrorsAreExplainedAndAreNotAnExpiredSession() {
        XCTAssertEqual(AuthenticationError.invalidAccessCode.localizedDescription, "El código de acceso no es correcto.")
        XCTAssertTrue(AuthenticationError.personalSignInDisabled.localizedDescription.contains("PERSONAL_ACCESS_CODE"))
        XCTAssertEqual(AuthenticationError.invalidAccessCode.appErrorKind, .failure)
        XCTAssertEqual(AuthenticationError.personalSignInDisabled.appErrorKind, .failure)
    }

    func testPersonalEnvironmentIsRecognised() {
        XCTAssertEqual(AppEnvironment(rawValue: "personal"), .personal)
        XCTAssertEqual(AppEnvironment.personal.displayName, "Instalación personal")
    }

    func testAnEmptyAccessCodeIsNotSent() async {
        let viewModel = AuthenticationViewModel(sessionManager: SessionManager(service: nil, configurationError: "sin API"))
        viewModel.accessCode = "   "

        await viewModel.signInWithAccessCode()

        XCTAssertNil(viewModel.errorMessage, "Nothing happens until a code is typed.")
        XCTAssertFalse(viewModel.isSigningIn)
    }

    func testAnAccessCodeWithoutBackendExplainsTheConfiguration() async {
        let viewModel = AuthenticationViewModel(sessionManager: SessionManager(service: nil, configurationError: "sin API"))
        viewModel.accessCode = "codigo-de-prueba"

        await viewModel.signInWithAccessCode()

        XCTAssertEqual(viewModel.errorMessage, "sin API")
        XCTAssertEqual(viewModel.accessCode, "codigo-de-prueba", "The code is kept so it can be retried.")
    }
}
