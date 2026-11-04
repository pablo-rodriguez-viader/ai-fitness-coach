import SwiftUI

struct InsightsView: View {
    @ObservedObject var viewModel: CoachInsightsViewModel

    var body: some View {
        List {
            if let error = viewModel.errorMessage {
                VStack(alignment: .leading) {
                    Text(error).foregroundStyle(Color.errorText)
                    Button("Volver a cargar", action: viewModel.load)
                }
            }
            if let insights = viewModel.insights {
                Section("Hábitos detectados") {
                    if insights.patterns.isEmpty {
                        Text("Necesitamos más actividad e historial de feedback para identificar patrones.")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(insights.patterns) { pattern in
                        VStack(alignment: .leading, spacing: 5) {
                            Text(pattern.description)
                            Text("Confianza \(Int((pattern.confidence * 100).rounded()))% · \(pattern.evidenceCount) registros")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                Section("Estrategias con resultados") {
                    if insights.effectiveRecommendations.isEmpty {
                        Text("Valora tus recomendaciones para conocer qué estrategias te funcionan.")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(insights.effectiveRecommendations) { item in
                        LabeledContent(item.type) {
                            Text("\(Int((item.averageScore * 100).rounded()))% · \(item.outcomeCount) resultados")
                        }
                    }
                }
            } else if viewModel.isLoading {
                ProgressView("Analizando tus hábitos…")
            }
        }
        .navigationTitle("Insights")
        .task { viewModel.load() }
    }
}
