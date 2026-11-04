import SwiftUI

struct WorkoutHistoryView: View {
    @ObservedObject var viewModel: WorkoutPlanViewModel

    var body: some View {
        List {
            if let error = viewModel.errorMessage {
                Section { Label(error, systemImage: "wifi.exclamationmark").foregroundStyle(Color.errorText) }
            }
            ForEach(viewModel.history) { item in
                VStack(alignment: .leading, spacing: 5) {
                    Text(item.title).font(.headline)
                    Text("\(item.goal) · \(item.date.formatted(date: .complete, time: .omitted))")
                        .font(.subheadline).foregroundStyle(.secondary)
                    Label(item.completed ? "Completado" : "No completado",
                          systemImage: item.completed ? "checkmark.circle" : "xmark.circle")
                    if let difficulty = item.difficulty {
                        Text(difficulty).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            if viewModel.isLoading {
                ProgressView("Cargando historial…")
            } else if viewModel.history.isEmpty && viewModel.errorMessage == nil {
                ContentUnavailableView("Sin entrenamientos registrados", systemImage: "clock",
                    description: Text("Completa o valora una sesión para crear tu historial."))
            }
        }
        .navigationTitle("Historial")
    }
}
