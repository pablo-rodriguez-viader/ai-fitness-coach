import SwiftUI

struct GoalsView: View {
    @ObservedObject var viewModel: GoalViewModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                OfflineBanner(message: "Sin conexión. Podrás guardar objetivos cuando vuelva la conexión.")
                    .listRowBackground(Color.clear)
                Section("Nuevo objetivo") {
                    Picker("Objetivo", selection: $viewModel.selectedGoal) {
                        ForEach(FitnessGoal.allCases) { goal in
                            Text(goal.title).tag(goal)
                        }
                    }
                    TextField("Peso objetivo (kg, opcional)", text: $viewModel.targetWeight)
                        .keyboardType(.decimalPad)
                    DatePicker("Fecha objetivo", selection: $viewModel.targetDate, in: Date.now..., displayedComponents: .date)
                    Button {
                        viewModel.save()
                    } label: {
                        if viewModel.isSaving { ProgressView("Guardando…") } else { Text("Guardar objetivo") }
                    }
                    .disabled(viewModel.isSaving)
                }
                if let error = viewModel.errorMessage {
                    Section {
                        InlineErrorView(
                            message: error,
                            retryTitle: "Volver a cargar",
                            retry: viewModel.loadFailed ? viewModel.load : nil)
                    }
                }
                Section("Tus objetivos") {
                    if viewModel.isLoading { ProgressView("Cargando objetivos…") }
                    else if viewModel.goals.isEmpty && viewModel.errorMessage == nil {
                        ContentUnavailableView("Sin objetivos", systemImage: "target",
                            description: Text("Guarda un objetivo para seguir tu evolución."))
                    }
                    ForEach(viewModel.goals) { goal in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(goal.typeTitle)
                                .font(.headline)
                            Text(goal.statusTitle)
                                .font(.caption)
                                .foregroundStyle(goal.status == "Active" ? Color.successText : Color.secondary)
                            if let target = goal.targetWeightKilograms {
                                Text("Objetivo: \(target, specifier: "%.1f") kg")
                                    .font(.caption)
                            }
                        }
                    }
                }
            }
            .navigationTitle("Objetivos")
            .toolbar { SheetCloseButton(dismiss: dismiss) }
            .task {
                AppTelemetry.shared.track("screen_view", ["screen": "goals"])
                viewModel.load()
            }
            .onDisappear { viewModel.cancel() }
        }
    }
}

extension GoalRecord {
    /// The API sends enum names (FatLoss, Active...); never show them untranslated.
    var typeTitle: String {
        FitnessGoal(rawValue: type.camelToGoalKey)?.title ?? type
    }

    var statusTitle: String {
        switch status {
        case "Active": "Activo"
        case "Completed": "Completado"
        case "Paused": "En pausa"
        case "Abandoned": "Abandonado"
        default: status
        }
    }
}

private extension String {
    var camelToGoalKey: String {
        switch self {
        case "FatLoss": "fat_loss"
        case "MuscleGain": "muscle_gain"
        case "WeightMaintenance": "weight_maintenance"
        case "Endurance": "endurance"
        case "GeneralHealth": "general_health"
        default: self
        }
    }
}
