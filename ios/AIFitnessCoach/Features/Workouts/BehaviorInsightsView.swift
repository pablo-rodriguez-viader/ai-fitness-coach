import SwiftUI

struct BehaviorInsightsView: View {
    @ObservedObject var viewModel: CoachInsightsViewModel

    var body: some View {
        InsightsView(viewModel: viewModel)
    }
}

struct RecommendationsView: View {
    @ObservedObject var viewModel: CoachInsightsViewModel

    var body: some View {
        List {
            if let error = viewModel.errorMessage {
                Text(error).foregroundStyle(Color.errorText)
            }
            ForEach(viewModel.recommendationHistory) { item in
                VStack(alignment: .leading, spacing: 8) {
                    Text(item.text)
                    Text(item.createdAt.formatted(date: .abbreviated, time: .shortened))
                        .font(.caption).foregroundStyle(.secondary)
                    if let score = item.outcomeScore {
                        Text("Valoración: \(Int((score * 100).rounded()))%")
                            .font(.caption)
                    } else {
                        HStack {
                            ForEach(1...5, id: \.self) { score in
                                Button("\(score)") { viewModel.rate(item, score: score) }
                                    .buttonStyle(.bordered)
                            }
                        }
                    }
                }
            }
            if viewModel.recommendationHistory.isEmpty && !viewModel.isLoading {
                ContentUnavailableView("Sin recomendaciones", systemImage: "sparkles")
            }
        }
        .navigationTitle("Recomendaciones")
        .task { viewModel.load() }
    }
}
