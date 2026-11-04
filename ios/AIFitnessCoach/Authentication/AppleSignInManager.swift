import AuthenticationServices
import Combine
import CryptoKit
import Foundation
import Security

@MainActor
final class AppleSignInManager: ObservableObject {
    private var currentNonce: String?

    func prepare(_ request: ASAuthorizationAppleIDRequest) throws {
        let nonce = try Self.randomNonce()
        currentNonce = nonce
        request.nonce = Self.sha256(nonce)
        request.requestedScopes = [.fullName, .email]
    }

    func credential(from authorization: ASAuthorization) throws -> (
        identityToken: String,
        nonce: String,
        displayName: String?
    ) {
        guard let credential = authorization.credential as? ASAuthorizationAppleIDCredential,
              let identityToken = credential.identityToken,
              let token = String(data: identityToken, encoding: .utf8),
              let nonce = currentNonce
        else {
            throw AuthenticationError.invalidAppleCredential
        }

        currentNonce = nil
        let components = PersonNameComponentsFormatter().string(from: credential.fullName ?? PersonNameComponents())
        return (token, nonce, components.isEmpty ? nil : components)
    }

    private static func sha256(_ value: String) -> String {
        SHA256.hash(data: Data(value.utf8)).map { String(format: "%02x", $0) }.joined()
    }

    private static func randomNonce(length: Int = 32) throws -> String {
        let alphabet = Array("0123456789ABCDEFGHIJKLMNOPQRSTUVXYZabcdefghijklmnopqrstuvwxyz-._")
        var nonce = ""
        var remaining = length

        while remaining > 0 {
            var bytes = [UInt8](repeating: 0, count: 16)
            let status = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
            guard status == errSecSuccess else {
                throw AuthenticationError.invalidAppleCredential
            }
            for byte in bytes where remaining > 0 {
                let index = Int(byte)
                if index < alphabet.count {
                    nonce.append(alphabet[index])
                    remaining -= 1
                }
            }
        }
        return nonce
    }
}
