import AuthenticationServices
import SwiftUI

struct AuthenticationView: View {
    @ObservedObject var viewModel: AuthenticationViewModel
    var sessionError: String?

    var body: some View {
        VStack(spacing: 24) {
            OfflineBanner(message: "Sin conexión. Necesitas internet para iniciar sesión.")
            Spacer(minLength: 0)
            BrandLogo(size: 120)
                .shadow(color: Brand.teal.opacity(0.35), radius: 24)
            Text("AI Fitness Coach")
                .font(.largeTitle.bold())
                .foregroundStyle(Brand.gradient)
            Text("Inicia sesión para asociar tus datos de salud a tu cuenta.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)

            if AuthenticationViewModel.usesAccessCode {
                accessCodeForm
            } else {
                SignInWithAppleButton(.signIn) { request in
                    do {
                        try viewModel.appleSignInManager.prepare(request)
                    } catch {
                        Task { await viewModel.handleAppleResult(.failure(error)) }
                    }
                } onCompletion: { result in
                    Task { await viewModel.handleAppleResult(result) }
                }
                .signInWithAppleButtonStyle(.black)
                .frame(height: 52)
                .disabled(viewModel.isSigningIn)
            }

            if viewModel.isSigningIn {
                ProgressView("Conectando...")
            }
            if let error = viewModel.errorMessage {
                Text(error)
                    .font(.footnote)
                    .foregroundStyle(Color.errorText)
                    .multilineTextAlignment(.center)
            }
            if let sessionError {
                Text(sessionError)
                    .font(.footnote)
                    .foregroundStyle(Color.errorText)
                    .multilineTextAlignment(.center)
            }
            Spacer(minLength: 0)
        }
        .padding(32)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Brand.canvas.ignoresSafeArea())
    }

    /// Personal build (free Apple ID): the code set as PERSONAL_ACCESS_CODE on your backend.
    private var accessCodeForm: some View {
        VStack(spacing: 12) {
            SecureField("Código de acceso", text: $viewModel.accessCode)
                .textContentType(.password)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.go)
                .onSubmit { Task { await viewModel.signInWithAccessCode() } }
                .padding(12)
                .brandCard(cornerRadius: 12)
                .accessibilityHint("El código configurado en tu servidor")
            Button {
                Task { await viewModel.signInWithAccessCode() }
            } label: {
                Text("Entrar")
            }
            .buttonStyle(.brand)
            .disabled(viewModel.isSigningIn
                || viewModel.accessCode.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            Text("Instalación personal: usa el código de acceso de tu servidor.")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
    }
}
