import SwiftUI

struct CoachMemoryView: View {
    @ObservedObject var viewModel: CoachInsightsViewModel

    var body: some View {
        List {
            if let error = viewModel.errorMessage {
                Text(error).foregroundStyle(Color.errorText)
            }
            if let feedback = viewModel.feedbackMessage {
                Text(feedback).foregroundStyle(Color.successText)
            }
            ForEach(viewModel.recommendationHistory) { recommendation in
                Section(recommendation.type.capitalized) {
                    Text(recommendation.text)
                    Text(recommendation.createdAt.formatted(date: .abbreviated, time: .shortened))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    if let score = recommendation.outcomeScore {
                        Label("Resultado \(Int((score * 100).rounded()))%", systemImage: "chart.bar")
                    } else {
                        Menu("Valorar recomendación") {
                            ForEach(1...5, id: \.self) { score in
                                Button("\(score) de 5") {
                                    viewModel.rate(recommendation, score: score)
                                }
                            }
                        }
                    }
                    if let feedback = recommendation.feedback {
                        Text(feedback).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            if viewModel.recommendationHistory.isEmpty && !viewModel.isLoading {
                ContentUnavailableView(
                    "Sin memoria todavía",
                    systemImage: "brain",
                    description: Text("Las recomendaciones y resultados aparecerán aquí."))
            } else if viewModel.isLoading {
                ProgressView("Cargando memoria…")
            }
        }
        .navigationTitle("Memoria del coach")
        .task { viewModel.load() }
    }
}
