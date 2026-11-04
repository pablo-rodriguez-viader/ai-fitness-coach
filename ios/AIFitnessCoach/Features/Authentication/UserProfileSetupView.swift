import SwiftUI

struct UserProfileSetupView: View {
    @ObservedObject var viewModel: UserProfileViewModel
    let userId: String?

    var body: some View {
        NavigationStack {
            Group {
                switch viewModel.state {
                case .loading:
                    ProgressView("Cargando perfil…")
                case .failed:
                    VStack {
                        OfflineBanner(message: "Sin conexión. Reintenta cuando vuelva la conexión.")
                            .padding(.horizontal)
                        failedView
                    }
                case .needsSetup:
                    profileForm
                case .ready:
                    ProgressView("Abriendo tu panel…")
                }
            }
            .navigationTitle("Tu perfil")
            .task { viewModel.load(userId: userId) }
        }
    }

    private var failedView: some View {
        ContentUnavailableView {
            Label("No se pudo cargar el perfil", systemImage: "person.crop.circle.badge.exclamationmark")
        } description: {
            Text(viewModel.errorMessage ?? "Comprueba la conexión e inténtalo de nuevo.")
        } actions: {
            Button("Reintentar") { viewModel.retry(userId: userId) }
        }
    }

    private var profileForm: some View {
        Form {
            OfflineBanner(message: "Sin conexión. Podrás guardar tu perfil cuando vuelva la conexión.")
                .listRowBackground(Color.clear)
            Section("Para personalizar el Coach") {
                TextField("Nombre", text: $viewModel.displayName)
                    .textContentType(.name)
                TextField("Edad", text: $viewModel.age).keyboardType(.numberPad)
                TextField("Altura (cm)", text: $viewModel.heightCentimeters).keyboardType(.decimalPad)
                Picker("Objetivo", selection: $viewModel.goal) {
                    ForEach(UserFitnessGoal.allCases) { goal in Text(goal.title).tag(goal) }
                }
            }
            if let error = viewModel.errorMessage {
                Section { Text(error).foregroundStyle(Color.errorText) }
            }
            Section {
                Button {
                    viewModel.save()
                } label: {
                    if viewModel.isSaving { ProgressView("Guardando…") }
                    else { Text("Guardar y continuar") }
                }
                .disabled(viewModel.isSaving)
            }
        }
    }
}
