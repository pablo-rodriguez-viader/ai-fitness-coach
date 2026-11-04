import SwiftUI

struct WorkoutSessionView: View {
    let day: WorkoutDay
    @ObservedObject var viewModel: WorkoutPlanViewModel
    @State private var showingFeedback = false
    @State private var hasTrackedStart = false

    var body: some View {
        List {
            Section(day.title) {
                if !day.recoveryNote.isEmpty {
                    Label(day.recoveryNote, systemImage: "heart.text.clipboard")
                }
                ForEach(day.exercises) { exercise in
                    VStack(alignment: .leading, spacing: 5) {
                        Text(exercise.name).font(.headline)
                        Text([exercise.sets.map { "\($0) series" },
                              exercise.repetitions.map { "\($0) reps" },
                              exercise.durationMinutes.map { "\($0) min" }]
                            .compactMap { $0 }
                            .joined(separator: " · "))
                            .font(.subheadline).foregroundStyle(.secondary)
                        if let instructions = exercise.instructions { Text(instructions) }
                    }
                    .padding(.vertical, 4)
                }
            }
            Section {
                OfflineBanner(message: "Sin conexión. Podrás guardar la valoración cuando vuelva la conexión.")
                switch viewModel.feedbackStates[day.id] {
                case .sending:
                    ProgressView("Guardando valoración…")
                case let .saved(adaptation):
                    Label("Valoración guardada", systemImage: "checkmark.circle.fill")
                        .foregroundStyle(Color.successText)
                    if !adaptation.isEmpty { Text(adaptation).font(.subheadline) }
                case let .failed(message):
                    InlineErrorView(message: message, retryTitle: "Volver a valorar") { showingFeedback = true }
                case nil:
                    EmptyView()
                }
                Button("Completar y valorar sesión", systemImage: "checkmark.circle") {
                    showingFeedback = true
                }
                .disabled(viewModel.feedbackStates[day.id] == .sending)
                .sheet(isPresented: $showingFeedback) {
                    WorkoutFeedbackView { completed, difficulty in
                        viewModel.submitFeedback(for: day, completed: completed, difficulty: difficulty)
                    }
                }
                .accessibilityHint("Guarda el estado y la dificultad en una sola operación.")
            }
        }
        .navigationTitle(day.date.formatted(date: .abbreviated, time: .omitted))
        .onAppear {
            // "Workout started" = the session was opened; counted once per screen visit.
            guard !hasTrackedStart else { return }
            hasTrackedStart = true
            AppTelemetry.shared.track("workout_started", [
                "workout_day_id": day.id.uuidString,
                "exercises": String(day.exercises.count)
            ])
        }
    }
}

struct WorkoutFeedbackView: View {
    let submit: (Bool, WorkoutFeedbackDifficulty) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var completed = true
    @State private var difficulty = WorkoutFeedbackDifficulty.appropriate

    var body: some View {
        NavigationStack {
            Form {
                Toggle("Completé el entrenamiento", isOn: $completed)
                Picker("Dificultad percibida", selection: $difficulty) {
                    ForEach(WorkoutFeedbackDifficulty.allCases) { value in
                        Text(value.title).tag(value)
                    }
                }
            }
            .navigationTitle("Feedback")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancelar") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Guardar") {
                        submit(completed, difficulty)
                        dismiss()
                    }
                }
            }
        }
    }
}
