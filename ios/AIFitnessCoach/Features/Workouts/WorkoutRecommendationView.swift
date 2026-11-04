import SwiftUI

struct WorkoutRecommendationView: View {
    @ObservedObject var viewModel: WorkoutPlanViewModel

    var body: some View {
        List {
            if let error = viewModel.errorMessage {
                Section { Label(error, systemImage: "wifi.exclamationmark").foregroundStyle(Color.errorText) }
            }
            ForEach(viewModel.recommendations) { recommendation in
                VStack(alignment: .leading, spacing: 6) {
                    Text(recommendation.type.capitalized).font(.headline)
                    Text(recommendation.text)
                    Text(recommendation.createdAt.formatted(date: .abbreviated, time: .shortened))
                        .font(.caption).foregroundStyle(.secondary)
                }
                .padding(.vertical, 5)
            }
            if viewModel.isLoading {
                ProgressView("Cargando recomendaciones…")
            } else if viewModel.recommendations.isEmpty && viewModel.errorMessage == nil {
                ContentUnavailableView("Sin recomendaciones", systemImage: "sparkles",
                    description: Text("Genera un plan para recibir recomendaciones personalizadas."))
            }
        }
        .navigationTitle("Recomendaciones")
    }
}
