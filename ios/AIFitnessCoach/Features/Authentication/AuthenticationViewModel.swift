import AuthenticationServices
import Combine
import Foundation

@MainActor
final class AuthenticationViewModel: ObservableObject {
    @Published private(set) var isSigningIn = false
    @Published private(set) var errorMessage: String?

    private let sessionManager: SessionManager
    let appleSignInManager = AppleSignInManager()

    @Published var accessCode = ""

    init(sessionManager: SessionManager) {
        self.sessionManager = sessionManager
    }

    /// Sign in with Apple needs a paid Apple Developer membership. The Personal build (free
    /// Apple ID, installed with Sideloadly/AltStore) signs in with a secret code instead.
    nonisolated static var usesAccessCode: Bool {
        #if PERSONAL_SIGN_IN
        true
        #else
        false
        #endif
    }

    func signInWithAccessCode() async {
        let code = accessCode.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !code.isEmpty, !isSigningIn else { return }
        isSigningIn = true
        defer { isSigningIn = false }
        do {
            try await sessionManager.signIn(personalAccessCode: code)
            accessCode = ""
            errorMessage = nil
        } catch {
            guard !error.isCancellation else { return }
            errorMessage = error.displayMessage
            error.track(area: "sign_in_personal")
        }
    }

    func handleAppleResult(_ result: Result<ASAuthorization, Error>) async {
        guard case let .success(authorization) = result else {
            if case let .failure(error) = result {
                // Closing Apple's sheet is a choice, not an error.
                if let authorizationError = error as? ASAuthorizationError,
                   authorizationError.code == .canceled {
                    errorMessage = nil
                } else {
                    errorMessage = error.displayMessage
                    error.track(area: "sign_in_apple")
                }
            }
            return
        }

        isSigningIn = true
        defer { isSigningIn = false }
        do {
            let credential = try appleSignInManager.credential(from: authorization)
            try await sessionManager.signIn(
                identityToken: credential.identityToken,
                nonce: credential.nonce,
                displayName: credential.displayName)
            errorMessage = nil
        } catch {
            guard !error.isCancellation else { return }
            errorMessage = error.displayMessage
            error.track(area: "sign_in")
        }
    }
}
