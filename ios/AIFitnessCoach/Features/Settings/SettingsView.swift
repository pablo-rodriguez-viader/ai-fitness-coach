import AuthenticationServices
import SwiftUI
import UIKit

/// Ajustes: privacy and data rights (App Store 5.1.1), Feedback Center and support, app info.
struct SettingsView: View {
    @ObservedObject var viewModel: SettingsViewModel
    @EnvironmentObject private var appConfig: AppConfigStore
    @Environment(\.dismiss) private var dismiss
    @Environment(\.openURL) private var openURL
    @State private var supportKind: SupportReportKind?
    @State private var isConfirmingDeletion = false
    @State private var dataToDelete: SettingsViewModel.DataDeletion?
    @State private var isShowingDeletion = false

    var body: some View {
        NavigationStack {
            Form {
                privacySection
                consentSection
                dataSection
                if appConfig.isEnabled(.feedbackCenter) {
                    supportSection
                }
                aboutSection
                deletionSection
            }
            .navigationTitle("Ajustes")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Cerrar") { dismiss() }
                }
            }
            .sheet(item: $supportKind) { kind in
                SupportFormView(viewModel: viewModel, kind: kind)
            }
            .sheet(isPresented: $isShowingDeletion) {
                DeleteAccountView(viewModel: viewModel)
            }
            .confirmationDialog(
                "¿Eliminar tu cuenta?",
                isPresented: $isConfirmingDeletion,
                titleVisibility: .visible
            ) {
                Button("Continuar", role: .destructive) { isShowingDeletion = true }
                Button("Cancelar", role: .cancel) {}
            } message: {
                Text("Se borrarán de forma permanente tu perfil, los datos de salud sincronizados, las conversaciones con el Coach, tus planes, objetivos y comidas. No se puede deshacer.")
            }
            .onAppear {
                AppTelemetry.shared.track("screen_view", ["screen": "settings"])
                appConfig.refresh()
            }
        }
    }

    private var privacySection: some View {
        Section {
            if let url = appConfig.config.privacyPolicyUrl {
                Link(destination: url) {
                    Label("Política de privacidad", systemImage: "hand.raised")
                }
            }
            if let url = appConfig.config.termsUrl {
                Link(destination: url) {
                    Label("Términos de uso", systemImage: "doc.text")
                }
            }
            Button {
                if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
            } label: {
                Label("Permisos de Salud y notificaciones", systemImage: "heart.text.square")
            }
            if viewModel.canRevokeAIConsent {
                Button {
                    viewModel.withdrawAIConsent()
                } label: {
                    Label("Retirar consentimiento de IA", systemImage: "sparkles")
                }
                .disabled(viewModel.isRevokingAIConsent)
                if let message = viewModel.aiConsentMessage {
                    Text(message).font(.footnote).foregroundStyle(.secondary)
                }
            }
        } header: {
            Text("Privacidad")
        } footer: {
            Text("Los permisos de lectura de Salud se cambian en Ajustes › Salud › Acceso a datos y dispositivos › AI Coach. Tus datos de salud nunca se usan para publicidad.")
        }
    }

    @ViewBuilder
    private var consentSection: some View {
        if let consents = viewModel.consents {
            Section {
                ConsentToggle(consents: consents)
                Button(role: .destructive) {
                    dataToDelete = .health
                } label: {
                    Label("Desconectar Salud y borrar sus datos", systemImage: "heart.slash")
                }
                .disabled(viewModel.deletingData != nil)
                Button(role: .destructive) {
                    dataToDelete = .ai
                } label: {
                    Label("Borrar datos del Coach (IA)", systemImage: "sparkles")
                }
                .disabled(viewModel.deletingData != nil)
                if viewModel.deletingData != nil {
                    ProgressView("Borrando…")
                }
                if let message = viewModel.dataDeletionMessage {
                    Text(message).font(.footnote).foregroundStyle(.secondary)
                }
            } header: {
                Text("Consentimientos y datos")
            } footer: {
                Text("Borrar los datos de Salud retira su consentimiento y detiene la sincronización. Borrar los datos del Coach elimina tus conversaciones y lo enviado a la IA, y retira su consentimiento. Tu cuenta se mantiene.")
            }
            .confirmationDialog(
                dataToDelete == .health ? "¿Desconectar Salud y borrar sus datos?" : "¿Borrar los datos del Coach?",
                isPresented: Binding(get: { dataToDelete != nil }, set: { if !$0 { dataToDelete = nil } }),
                titleVisibility: .visible
            ) {
                Button("Borrar", role: .destructive) {
                    let category = dataToDelete
                    dataToDelete = nil
                    Task {
                        if category == .health { await viewModel.deleteHealthData() }
                        if category == .ai { await viewModel.deleteAIData() }
                    }
                }
                Button("Cancelar", role: .cancel) { dataToDelete = nil }
            } message: {
                Text(dataToDelete == .health
                    ? "Se borrarán de nuestros servidores todos tus datos de Salud sincronizados. Los de tu iPhone no se tocan."
                    : "Se borrarán tus conversaciones con el Coach y el Análisis y todo lo enviado a la IA.")
            }
        }
    }

    @ViewBuilder
    private var dataSection: some View {
        if appConfig.isEnabled(.dataExport) {
            Section {
                switch viewModel.exportState {
                case .idle:
                    Button("Exportar mis datos", systemImage: "square.and.arrow.up") { viewModel.exportData() }
                case .exporting:
                    HStack {
                        ProgressView()
                        Text("Preparando la exportación…")
                    }
                case let .ready(url):
                    ShareLink(item: url) {
                        Label("Compartir exportación (JSON)", systemImage: "square.and.arrow.up.on.square")
                    }
                    Button("Generar de nuevo", systemImage: "arrow.clockwise") { viewModel.exportData() }
                case let .failed(message):
                    Text(message).font(.footnote).foregroundStyle(Color.errorText)
                    Button("Reintentar exportación", systemImage: "arrow.clockwise") { viewModel.exportData() }
                }
            } header: {
                Text("Tus datos")
            } footer: {
                Text("Descarga una copia en JSON de todo lo que guardamos sobre ti: perfil, datos de salud sincronizados, conversaciones, entrenamientos, nutrición, objetivos y eventos de uso.")
            }
        }
    }

    private var supportSection: some View {
        Section {
            Button {
                supportKind = .feedback
            } label: {
                Label("Enviar comentarios", systemImage: "star.bubble")
            }
            Button {
                supportKind = .bug
            } label: {
                Label("Informar de un error", systemImage: "ladybug")
            }
            Button {
                supportKind = .support
            } label: {
                Label("Contactar con soporte", systemImage: "questionmark.bubble")
            }
            if let email = appConfig.config.supportEmail,
               let url = SupportMail.url(to: email) {
                Link(destination: url) {
                    Label("Escribir a \(email)", systemImage: "envelope")
                }
            }
        } header: {
            Text("Ayuda y comentarios")
        } footer: {
            if AppEnvironment.current == .beta {
                Text("En TestFlight también puedes hacer una captura de pantalla y tocar «Compartir beta» para enviarla con tus comentarios.")
            }
        }
    }

    private var aboutSection: some View {
        Section("Acerca de") {
            LabeledContent("Versión", value: "\(AppTelemetry.appVersion) (\(AppTelemetry.buildNumber))")
            LabeledContent("Entorno", value: AppEnvironment.current.displayName)
            if appConfig.requiresUpdate {
                Label("Hay una versión más reciente. Actualiza desde TestFlight o el App Store.", systemImage: "arrow.down.app")
                    .foregroundStyle(Color.warningText)
            }
        }
    }

    private var deletionSection: some View {
        Section {
            Button(role: .destructive) {
                isConfirmingDeletion = true
            } label: {
                Label("Eliminar cuenta", systemImage: "trash")
            }
            .disabled(viewModel.isDeleting)
        } footer: {
            Text("Elimina tu cuenta y todos sus datos de nuestros servidores. Los datos de Apple Health de tu iPhone no se tocan.")
        }
    }
}

/// Re-authentication with Apple right before deleting: confirms it is the account owner and gives
/// the server a fresh code to revoke the Sign in with Apple grant (Guideline 5.1.1(v)).
private struct DeleteAccountView: View {
    @ObservedObject var viewModel: SettingsViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 20) {
                Label("Eliminar cuenta", systemImage: "exclamationmark.triangle.fill")
                    .font(.title2.bold())
                    .foregroundStyle(Color.errorText)
                Text(AuthenticationViewModel.usesAccessCode
                    ? "Se borrarán todos tus datos del servidor y se cerrará la sesión en este iPhone. Esta acción es definitiva."
                    : "Confirma con Apple para eliminar tu cuenta. Se borrarán todos tus datos del servidor y se cerrará la sesión en este iPhone. Esta acción es definitiva.")
                if viewModel.isDeleting {
                    ProgressView("Eliminando tu cuenta…")
                        .frame(maxWidth: .infinity)
                } else if AuthenticationViewModel.usesAccessCode {
                    // Personal build: no Sign in with Apple, so nothing to re-authenticate or revoke.
                    Button(role: .destructive) {
                        Task { _ = await viewModel.deleteAccount(appleAuthorizationCode: nil) }
                    } label: {
                        Text("Eliminar definitivamente").frame(maxWidth: .infinity, minHeight: 44)
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Color.errorText)
                } else {
                    SignInWithAppleButton(.continue) { request in
                        request.requestedScopes = []
                    } onCompletion: { result in
                        handle(result)
                    }
                    .signInWithAppleButtonStyle(.black)
                    .frame(height: 50)
                }
                if let error = viewModel.deletionError {
                    Text(error).font(.footnote).foregroundStyle(Color.errorText)
                }
                Spacer()
            }
            .padding()
            .navigationTitle("Confirmar")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Cancelar") { dismiss() }.disabled(viewModel.isDeleting)
                }
            }
            .interactiveDismissDisabled(viewModel.isDeleting)
        }
    }

    private func handle(_ result: Result<ASAuthorization, Error>) {
        switch result {
        case let .success(authorization):
            let credential = authorization.credential as? ASAuthorizationAppleIDCredential
            let code = credential?.authorizationCode.flatMap { String(data: $0, encoding: .utf8) }
            Task {
                // On success the session ends and the app returns to the sign-in screen.
                _ = await viewModel.deleteAccount(appleAuthorizationCode: code)
            }
        case let .failure(error):
            if let authorizationError = error as? ASAuthorizationError, authorizationError.code == .canceled {
                return
            }
            viewModel.reportDeletionProblem("No se pudo confirmar con Apple. Inténtalo de nuevo.")
        }
    }
}

enum SupportMail {
    static func url(to address: String) -> URL? {
        var components = URLComponents()
        components.scheme = "mailto"
        components.path = address
        components.queryItems = [
            URLQueryItem(name: "subject", value: "AI Coach \(AppTelemetry.appVersion) (\(AppTelemetry.buildNumber))")
        ]
        return components.url
    }
}
