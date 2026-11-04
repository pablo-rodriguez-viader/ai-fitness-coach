import Combine
import Foundation

@MainActor
final class GoalViewModel: ObservableObject {
    @Published private(set) var goals: [GoalRecord] = []
    @Published private(set) var isLoading = false
    @Published private(set) var isSaving = false
    @Published private(set) var errorMessage: String?
    /// True when `errorMessage` comes from loading (retry helps), false when saving failed.
    @Published private(set) var loadFailed = false
    @Published var selectedGoal: FitnessGoal = .generalHealth
    @Published var targetWeight = ""
    @Published var targetDate = Date.now.addingTimeInterval(60 * 60 * 24 * 90)

    private let repository: WellnessRepository
    private var loadTask: Task<Void, Never>?
    private var saveTask: Task<Void, Never>?

    init(repository: WellnessRepository) {
        self.repository = repository
    }

    func load() {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            do {
                goals = try await repository.goals()
                errorMessage = nil
                loadFailed = false
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.displayMessage
                loadFailed = true
                error.track(area: "goals_load")
            }
        }
    }

    func save() {
        guard !isSaving else { return }
        loadTask?.cancel()
        isSaving = true
        saveTask = Task { [weak self] in
            guard let self else { return }
            defer { isSaving = false }
            do {
                let targetWeightInput = targetWeight.trimmingCharacters(in: .whitespacesAndNewlines)
                let weight = targetWeightInput.isEmpty
                    ? nil
                    : Double(targetWeightInput.replacingOccurrences(of: ",", with: "."))
                guard targetWeightInput.isEmpty || weight != nil else {
                    errorMessage = "Introduce un peso objetivo válido."
                    loadFailed = false
                    return
                }
                let formatter = DateFormatter()
                formatter.calendar = Calendar(identifier: .iso8601)
                formatter.timeZone = .current
                formatter.dateFormat = "yyyy-MM-dd"
                let record = try await repository.createGoal(GoalRequest(
                    type: selectedGoal.rawValue,
                    targetWeightKilograms: weight,
                    targetDate: formatter.string(from: targetDate)))
                goals.insert(record, at: 0)
                errorMessage = nil
                loadFailed = false
                AppTelemetry.shared.track("goal_created", ["type": selectedGoal.rawValue])
                // The server pauses the previous active goal; reload so its status is current.
                goals = (try? await repository.goals()) ?? goals
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.displayMessage
                loadFailed = false
                error.track(area: "goals_save")
            }
        }
    }

    func cancel() {
        loadTask?.cancel()
        loadTask = nil
    }

    func resetForSignOut() {
        cancel()
        saveTask?.cancel()
        saveTask = nil
        goals = []
        errorMessage = nil
        loadFailed = false
    }
}
