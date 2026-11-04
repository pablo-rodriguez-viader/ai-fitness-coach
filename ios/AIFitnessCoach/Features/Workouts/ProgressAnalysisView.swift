import SwiftUI

struct ProgressAnalysisView: View {
    @ObservedObject var viewModel: CoachInsightsViewModel

    var body: some View {
        List {
            if let error = viewModel.errorMessage {
                Text(error).foregroundStyle(Color.errorText)
            }
            if let progress = viewModel.progress {
                Section("Tendencias recientes") {
                    ForEach(progress.trends) { trend in
                        LabeledContent(trend.metric.capitalized) {
                            if let change = trend.change {
                                Text("\(change, specifier: "%.2f") \(trend.unit)")
                            } else {
                                Text("Datos insuficientes").foregroundStyle(.secondary)
                            }
                        }
                    }
                }
                Section("Objetivos") {
                    ForEach(progress.goals) { goal in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(goal.goal)
                            Text(goal.progressPercent.map { "\(Int($0.rounded()))% · \(goal.status)" } ?? goal.status)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                Section("Análisis") {
                    Text(progress.summary)
                        .foregroundStyle(.secondary)
                }
            } else if viewModel.isLoading {
                ProgressView("Analizando progreso…")
            }
        }
        .navigationTitle("Análisis de progreso")
        .task { viewModel.load() }
    }
}
