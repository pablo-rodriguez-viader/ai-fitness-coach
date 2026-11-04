import SwiftUI

struct NutritionView: View {
    @ObservedObject var viewModel: NutritionViewModel
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var network: NetworkMonitor

    var body: some View {
        NavigationStack {
            Form {
                statusRows
                profileSection
                targetsSections
                todaySection
                mealSection
                Text("Las necesidades calóricas y los macronutrientes son estimaciones generales, no consejo médico.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
            .navigationTitle("Nutrición")
            .toolbar { SheetCloseButton(dismiss: dismiss) }
            .task {
                AppTelemetry.shared.track("screen_view", ["screen": "nutrition"])
                viewModel.load()
            }
            .onDisappear { viewModel.cancel() }
        }
    }

    // Split into small builders: one large Form closure is too slow for the type checker.
    @ViewBuilder
    private var statusRows: some View {
        if !network.isOnline {
            OfflineBanner(message: "Sin conexión. Las comidas no se pueden registrar hasta recuperarla.")
                .listRowBackground(Color.clear)
        }
        SyncPendingNotice(impact: "El peso actual y los objetivos se calculan con los datos ya enviados.")
        if viewModel.isLoading {
            ProgressView("Cargando nutrición…")
        }
        if let error = viewModel.errorMessage {
            Section {
                InlineErrorView(
                    message: error,
                    retryTitle: "Volver a cargar",
                    retry: viewModel.loadFailed ? { viewModel.load() } : nil)
            }
        }
    }

    private var profileSection: some View {
        Section("Perfil nutricional") {
            Picker("Sexo para Mifflin–St Jeor", selection: $viewModel.selectedSex) {
                Text("Hombre").tag(NutritionSex.male)
                Text("Mujer").tag(NutritionSex.female)
            }
            Picker("Actividad", selection: $viewModel.activityLevel) {
                ForEach(NutritionActivityLevel.allCases) { level in
                    Text(level.title).tag(level)
                }
            }
            Button("Calcular mis objetivos") { viewModel.saveProfile() }
                .disabled(viewModel.isSaving)
        }
    }

    @ViewBuilder
    private var targetsSections: some View {
        if let targets = viewModel.targets {
            Section("Necesidades estimadas") {
                valueRow("Peso actual", value: targets.weightKilograms, unit: "kg")
                valueRow("Metabolismo basal (BMR)", value: targets.bmr, unit: "kcal")
                valueRow("Gasto diario (TDEE)", value: targets.tdee, unit: "kcal")
                valueRow("Objetivo diario", value: targets.dailyCalorieTarget, unit: "kcal")
                valueRow("Ajuste", value: targets.deficitOrSurplusCalories, unit: "kcal")
            }
            Section("Macros diarios estimados") {
                valueRow("Proteínas", value: targets.proteinGrams, unit: "g")
                valueRow("Grasas", value: targets.fatGrams, unit: "g")
                valueRow("Carbohidratos", value: targets.carbohydrateGrams, unit: "g")
                ForEach(targets.safetyWarnings ?? [], id: \.self) { warning in
                    Label(warning, systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Color.warningText)
                }
            }
        } else if !viewModel.isLoading {
            Section("Objetivos estimados") {
                Label(
                    viewModel.setupHint ?? "Configura tu perfil y sincroniza un peso para calcularlos.",
                    systemImage: "info.circle")
                    .foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private var todaySection: some View {
        if let summary = viewModel.summary {
            Section("Hoy") {
                valueRow("Consumido", value: summary.calories, unit: "kcal")
                valueRow("Proteínas", value: summary.proteinGrams, unit: "g")
                valueRow("Grasas", value: summary.fatGrams, unit: "g")
                valueRow("Carbohidratos", value: summary.carbohydrateGrams, unit: "g")
                if let adherence = summary.calorieAdherencePercent {
                    valueRow("Adherencia calórica", value: adherence, unit: "%")
                }
            }
        } else if !viewModel.isLoading && viewModel.errorMessage == nil {
            Section("Registro de hoy") {
                Text("Todavía no hay comidas registradas hoy.")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var mealSection: some View {
        Section("Registrar comida") {
            Picker("Comida", selection: $viewModel.mealType) {
                ForEach(MealType.allCases) { meal in
                    Text(meal.title).tag(meal)
                }
            }
            TextField("Nombre", text: $viewModel.mealName)
            numericField("Calorías", text: $viewModel.calories)
            numericField("Proteínas (g)", text: $viewModel.protein)
            numericField("Grasas (g)", text: $viewModel.fat)
            numericField("Carbohidratos (g)", text: $viewModel.carbohydrates)
            Button {
                viewModel.addMeal()
            } label: {
                if viewModel.isSaving { ProgressView("Guardando…") } else { Text("Registrar comida") }
            }
            .disabled(viewModel.isSaving || !network.isOnline)
        }
    }

    private func valueRow(_ title: String, value: Double, unit: String) -> some View {
        HStack {
            Text(title)
            Spacer()
            Text("\(value, specifier: "%.1f") \(unit)")
                .foregroundStyle(.secondary)
        }
    }

    private func numericField(_ title: String, text: Binding<String>) -> some View {
        TextField(title, text: text)
            .keyboardType(.decimalPad)
    }
}
