import Combine
import Foundation

@MainActor
final class WorkoutPlanViewModel: ObservableObject {
    @Published private(set) var plan: WorkoutPlan?
    @Published private(set) var history: [WorkoutHistory] = []
    @Published private(set) var recommendations: [WorkoutRecommendation] = []
    @Published private(set) var isLoading = false
    @Published private(set) var isGenerating = false
    @Published private(set) var errorMessage: String?
    @Published private(set) var feedbackMessage: String?
    /// Saving state of each session's feedback, shown on the session screen itself.
    @Published private(set) var feedbackStates: [UUID: FeedbackState] = [:]
    @Published var selectedGoal: WorkoutGoal = .generalHealth
    @Published var availableMinutes = 45
    @Published var availableEquipment: Set<String> = ["Bodyweight"]
    /// Free text for the AI: level, injuries, preferences (max 500 characters, like the server).
    @Published var needs = ""
    static let maximumNeedsLength = 500

    enum FeedbackState: Equatable {
        case sending
        case saved(String)
        case failed(String)
    }

    private let repository: WorkoutRepository
    private enum Operation: Hashable { case feedback(UUID), completion(UUID) }
    private var loadTask: Task<Void, Never>?
    private var generationTask: Task<Void, Never>?
    private var operationTasks: [Operation: Task<Void, Never>] = [:]

    init(repository: WorkoutRepository) {
        self.repository = repository
    }

    func load() {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            // Each part loads on its own so one failing endpoint does not hide the others.
            var failure: Error?
            do { plan = try await repository.latest() } catch { failure = failure ?? error }
            do { history = try await repository.history() } catch { failure = failure ?? error }
            do { recommendations = try await repository.recommendations() } catch { failure = failure ?? error }
            if let failure {
                guard !failure.isCancellation else { return }
                errorMessage = Self.message(for: failure)
                failure.track(area: "workouts_load")
            } else {
                errorMessage = nil
            }
        }
    }

    static func trimmedNeeds(_ text: String) -> String? {
        let trimmed = String(text.trimmingCharacters(in: .whitespacesAndNewlines).prefix(maximumNeedsLength))
        return trimmed.isEmpty ? nil : trimmed
    }

    private static func message(for error: Error) -> String {
        error.appErrorKind == .failure ? error.localizedDescription : error.userMessage
    }

    func generate(days: Int = 7) {
        guard !isGenerating else { return }
        generationTask?.cancel()
        isGenerating = true
        generationTask = Task { [weak self] in
            guard let self else { return }
            defer { isGenerating = false }
            let startedAt = Date()
            do {
                let generatedPlan = try await repository.generate(
                    goal: selectedGoal,
                    days: days,
                    availableMinutes: availableMinutes,
                    equipment: Array(availableEquipment).sorted(),
                    needs: Self.trimmedNeeds(needs))
                plan = generatedPlan
                errorMessage = nil
                feedbackMessage = nil
                AppTelemetry.shared.track("workout_plan_generated", ["days": String(days)])
                ObservabilityEvents.aiRequest(feature: "workout_plan", outcome: "ok", startedAt: startedAt)
                recommendations = (try? await repository.recommendations()) ?? recommendations
                history = (try? await repository.history()) ?? history
            } catch {
                ObservabilityEvents.aiRequest(
                    feature: "workout_plan", outcome: ObservabilityEvents.aiOutcome(for: error),
                    startedAt: startedAt, error: error)
                guard !error.isCancellation else { return }
                errorMessage = Self.message(for: error)
                error.track(area: "workouts_generate")
            }
        }
    }

    func submitFeedback(for day: WorkoutDay, completed: Bool, difficulty: WorkoutFeedbackDifficulty) {
        let operation = Operation.feedback(day.id)
        guard operationTasks[operation] == nil else { return }
        feedbackStates[day.id] = .sending
        operationTasks[operation] = Task { [weak self] in
            guard let self else { return }
            defer { operationTasks[operation] = nil }
            do {
                let response = try await repository.submit(WorkoutFeedbackRequest(
                    workoutDayId: day.id,
                    completed: completed,
                    difficulty: difficulty.rawValue,
                    comment: nil))
                feedbackMessage = response.adaptation
                feedbackStates[day.id] = .saved(response.adaptation)
                errorMessage = nil
                AppTelemetry.shared.track("workout_feedback_sent", [
                    "completed": completed ? "true" : "false",
                    "difficulty": difficulty.rawValue
                ])
                recommendations = (try? await repository.recommendations()) ?? recommendations
                history = (try? await repository.history()) ?? history
            } catch {
                guard !error.isCancellation else {
                    feedbackStates[day.id] = nil
                    return
                }
                feedbackStates[day.id] = .failed(Self.message(for: error))
                errorMessage = Self.message(for: error)
                error.track(area: "workouts_feedback")
            }
        }
    }

    func complete(day: WorkoutDay) {
        let operation = Operation.completion(day.id)
        guard operationTasks[operation] == nil else { return }
        operationTasks[operation] = Task { [weak self] in
            guard let self else { return }
            defer { operationTasks[operation] = nil }
            do {
                _ = try await repository.complete(dayId: day.id)
                feedbackMessage = "Entrenamiento completado y añadido al historial."
                errorMessage = nil
                history = (try? await repository.history()) ?? history
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = Self.message(for: error)
            }
        }
    }

    func cancelTasks() {
        loadTask?.cancel()
        loadTask = nil
    }

    private func cancelAllTasks() {
        cancelTasks()
        generationTask?.cancel()
        operationTasks.values.forEach { $0.cancel() }
        generationTask = nil
        operationTasks = [:]
    }

    func resetForSignOut() {
        cancelAllTasks()
        plan = nil
        history = []
        recommendations = []
        isLoading = false
        isGenerating = false
        errorMessage = nil
        feedbackMessage = nil
        feedbackStates = [:]
    }
}
