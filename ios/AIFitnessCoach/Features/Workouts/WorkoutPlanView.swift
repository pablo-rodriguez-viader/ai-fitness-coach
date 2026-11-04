import SwiftUI

struct WorkoutPlanView: View {
    @ObservedObject var viewModel: WorkoutPlanViewModel
    var insightsViewModel: CoachInsightsViewModel?
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var network: NetworkMonitor

    var body: some View {
        NavigationStack {
            List {
                if !network.isOnline {
                    OfflineBanner(message: "Sin conexión. Generar planes y guardar feedback requiere internet.")
                        .listRowBackground(Color.clear)
                }
                SyncPendingNotice(impact: "El plan se adapta a los datos ya enviados.")
                Section("Objetivo") {
                    Picker("Objetivo", selection: $viewModel.selectedGoal) {
                        ForEach(WorkoutGoal.allCases) { goal in
                            Text(goal.title).tag(goal)
                        }
                    }
                    Picker("Tiempo disponible", selection: $viewModel.availableMinutes) {
                        Text("20 min").tag(20)
                        Text("30 min").tag(30)
                        Text("45 min").tag(45)
                        Text("60 min").tag(60)
                    }
                    ForEach(Self.equipmentOptions) { option in
                        Toggle(option.title, isOn: Binding(
                            get: { viewModel.availableEquipment.contains(option.id) },
                            set: { enabled in
                                if enabled { viewModel.availableEquipment.insert(option.id) }
                                else { viewModel.availableEquipment.remove(option.id) }
                            }))
                    }
                    Button {
                        viewModel.generate()
                    } label: {
                        if viewModel.isGenerating {
                            ProgressView("Generando plan…")
                        } else {
                            Label("Generar plan semanal", systemImage: "sparkles")
                        }
                    }
                    .disabled(viewModel.isGenerating)
                    Button {
                        viewModel.generate(days: 1)
                    } label: {
                        Label("Generar rutina para hoy", systemImage: "sun.max")
                    }
                    .disabled(viewModel.isGenerating)
                }

                if let error = viewModel.errorMessage {
                    Section {
                        Text(error).foregroundStyle(Color.errorText)
                        Button("Volver a cargar", action: viewModel.load)
                    }
                }
                if let feedback = viewModel.feedbackMessage {
                    Section("Ajuste") {
                        Text(feedback)
                    }
                }
                if let plan = viewModel.plan {
                    Section("Plan actual · \(WorkoutGoal(rawValue: plan.goal)?.title ?? plan.goal)") {
                        ForEach(plan.days) { day in
                            WorkoutDayRow(day: day, viewModel: viewModel)
                        }
                    }
                } else if viewModel.isLoading {
                    ProgressView("Cargando plan…")
                } else {
                    ContentUnavailableView(
                        "Aún no tienes un plan",
                        systemImage: "figure.run",
                        description: Text("Genera un plan adaptado a tus datos recientes."))
                }

                Section {
                    NavigationLink("Historial de entrenamientos") {
                        WorkoutHistoryView(viewModel: viewModel)
                    }
                    NavigationLink("Recomendaciones") {
                        WorkoutRecommendationView(viewModel: viewModel)
                    }
                    if let insightsViewModel {
                        NavigationLink("Insights del coach") {
                            InsightsView(viewModel: insightsViewModel)
                        }
                        NavigationLink("Análisis de progreso") {
                            ProgressAnalysisView(viewModel: insightsViewModel)
                        }
                        NavigationLink("Adherencia") {
                            AdherenceView(viewModel: insightsViewModel)
                        }
                        NavigationLink("Memoria del coach") {
                            CoachMemoryView(viewModel: insightsViewModel)
                        }
                    }
                }
            }
            .navigationTitle("Entrenamientos")
            .toolbar { SheetCloseButton(dismiss: dismiss) }
            .task {
                AppTelemetry.shared.track("screen_view", ["screen": "workouts"])
                viewModel.load()
            }
            .onDisappear { viewModel.cancelTasks() }
        }
    }

    /// API values (sent as-is) with the Spanish label shown to the user.
    struct EquipmentOption: Identifiable, Hashable {
        let id: String
        let title: String
    }

    static let equipmentOptions = [
        EquipmentOption(id: "Dumbbells", title: "Mancuernas"),
        EquipmentOption(id: "ResistanceBand", title: "Banda elástica"),
        EquipmentOption(id: "Mat", title: "Esterilla"),
        EquipmentOption(id: "Treadmill", title: "Cinta de correr")
    ]
}

private struct WorkoutDayRow: View {
    let day: WorkoutDay
    @ObservedObject var viewModel: WorkoutPlanViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(day.date.formatted(date: .complete, time: .omitted))
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(day.title).font(.headline)
            NavigationLink("Ver sesión") {
                WorkoutSessionView(day: day, viewModel: viewModel)
            }
            if !day.recoveryNote.isEmpty {
                Text(day.recoveryNote).font(.subheadline)
            }
            ForEach(day.exercises) { exercise in
                VStack(alignment: .leading, spacing: 3) {
                    Text(exercise.name).font(.subheadline.weight(.medium))
                    Text([exercise.sets.map { "\($0) series" },
                          exercise.repetitions.map { "\($0) reps" },
                          exercise.durationMinutes.map { "\($0) min" }]
                        .compactMap { $0 }
                        .joined(separator: " · "))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    if let instructions = exercise.instructions {
                        Text(instructions).font(.caption).foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 3)
            }
        }
        .padding(.vertical, 8)
    }
}
