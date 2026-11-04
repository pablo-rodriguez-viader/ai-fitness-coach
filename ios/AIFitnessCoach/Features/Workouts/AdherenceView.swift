import SwiftUI

struct AdherenceView: View {
    @ObservedObject var viewModel: CoachInsightsViewModel

    var body: some View {
        List {
            if let error = viewModel.errorMessage {
                VStack(alignment: .leading) {
                    Text(error).foregroundStyle(Color.errorText)
                    Button("Volver a cargar", action: viewModel.load)
                }
            }
            if let adherence = viewModel.adherence {
                Section("Últimos 28 días") {
                    LabeledContent("Adherencia", value: "\(adherence.percentage)%")
                    LabeledContent("Sesiones completadas", value: "\(adherence.completedSessions)")
                    LabeledContent("Sesiones planificadas", value: "\(adherence.plannedSessions)")
                    ProgressView(value: adherence.rate)
                    Text("\(adherence.periodStart.formatted(date: .abbreviated, time: .omitted)) – \(adherence.periodEnd.formatted(date: .abbreviated, time: .omitted))")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                if adherence.plannedSessions == 0 {
                    ContentUnavailableView(
                        "Sin sesiones planificadas",
                        systemImage: "calendar",
                        description: Text("Genera un plan para empezar a medir la adherencia."))
                }
            } else if viewModel.isLoading {
                ProgressView("Calculando adherencia…")
            }
        }
        .navigationTitle("Adherencia")
        .task { viewModel.load() }
    }
}
