import Combine
import Foundation

@MainActor
final class CoachInsightsViewModel: ObservableObject {
    @Published private(set) var insights: CoachInsightsResponse?
    @Published private(set) var progress: ProgressAnalysisResponse?
    @Published private(set) var adherence: TrainingAdherenceSummary?
    @Published private(set) var recommendationHistory: [CoachRecommendationRecord] = []
    @Published private(set) var isLoading = false
    @Published private(set) var errorMessage: String?
    @Published private(set) var feedbackMessage: String?

    private let repository: WorkoutRepository
    private var loadTask: Task<Void, Never>?
    private var feedbackTasks: [UUID: Task<Void, Never>] = [:]

    init(repository: WorkoutRepository) {
        self.repository = repository
    }

    func load() {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            do {
                async let insightResult = repository.coachInsights()
                async let progressResult = repository.progressAnalysis()
                async let adherenceResult = repository.adherence()
                async let historyResult = repository.recommendationHistory()
                (insights, progress, adherence, recommendationHistory) = try await (
                    insightResult,
                    progressResult,
                    adherenceResult,
                    historyResult)
                errorMessage = nil
            } catch is CancellationError {
                return
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.displayMessage
            }
        }
    }

    func rate(_ recommendation: CoachRecommendationRecord, score: Int) {
        guard feedbackTasks[recommendation.id] == nil else { return }
        feedbackTasks[recommendation.id] = Task { [weak self] in
            guard let self else { return }
            defer { feedbackTasks[recommendation.id] = nil }
            do {
                try await repository.rateRecommendation(RecommendationRatingRequest(
                    recommendationId: recommendation.id,
                    score: score,
                    comment: nil))
                feedbackMessage = "Gracias. Tu valoración ayudará a personalizar las próximas recomendaciones."
                recommendationHistory = try await repository.recommendationHistory()
                insights = try await repository.coachInsights()
                errorMessage = nil
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.displayMessage
            }
        }
    }

    func cancelTasks() {
        loadTask?.cancel()
        feedbackTasks.values.forEach { $0.cancel() }
        loadTask = nil
        feedbackTasks = [:]
    }

    func resetForSignOut() {
        cancelTasks()
        insights = nil
        progress = nil
        adherence = nil
        recommendationHistory = []
        isLoading = false
        errorMessage = nil
        feedbackMessage = nil
    }
}
