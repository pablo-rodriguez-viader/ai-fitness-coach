import Combine
import Foundation

@MainActor
final class NutritionViewModel: ObservableObject {
    @Published private(set) var targets: NutritionTargets?
    @Published private(set) var summary: NutritionSummary?
    @Published private(set) var isLoading = false
    @Published private(set) var isSaving = false
    @Published private(set) var errorMessage: String?
    /// True when `errorMessage` comes from loading (retry helps), false when saving failed.
    @Published private(set) var loadFailed = false
    /// Why targets are not available yet (no nutrition profile, no weight); not an error.
    @Published private(set) var setupHint: String?
    @Published var selectedSex: NutritionSex = .female
    @Published var activityLevel: NutritionActivityLevel = .moderate
    @Published var mealType: MealType = .breakfast
    @Published var mealName = ""
    @Published var calories = ""
    @Published var protein = ""
    @Published var fat = ""
    @Published var carbohydrates = ""

    private let repository: WellnessRepository
    private var loadTask: Task<Void, Never>?
    private var mutationTask: Task<Void, Never>?

    init(repository: WellnessRepository) {
        self.repository = repository
    }

    func load() {
        loadTask?.cancel()
        loadTask = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            // Loaded separately: a new user has no targets yet but can already log meals.
            var failure: Error?
            do {
                targets = try await repository.nutritionTargets()
                setupHint = nil
            } catch let error as WellnessError where error.isMissingSetup {
                targets = nil
                setupHint = error.localizedDescription
            } catch {
                failure = error
            }
            do {
                summary = try await repository.nutritionSummary()
            } catch {
                failure = failure ?? error
            }
            if let failure {
                guard !failure.isCancellation else { return }
                errorMessage = failure.displayMessage
                loadFailed = true
                failure.track(area: "nutrition_load")
            } else {
                errorMessage = nil
                loadFailed = false
            }
        }
    }

    func saveProfile() {
        guard !isSaving else { return }
        loadTask?.cancel()
        isSaving = true
        loadFailed = false
        mutationTask = Task { [weak self] in
            guard let self else { return }
            defer { isSaving = false }
            do {
                targets = try await repository.setNutritionProfile(NutritionProfileRequest(
                    sex: selectedSex.rawValue,
                    activityLevel: activityLevel.rawValue))
                errorMessage = nil
                setupHint = nil
            } catch let error as WellnessError where error.isMissingSetup {
                // Usually no weight in Apple Health yet: guidance, not a failure.
                setupHint = error.localizedDescription
                errorMessage = nil
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.displayMessage
            }
        }
    }

    func addMeal() {
        guard !isSaving else { return }
        guard let caloriesValue = Self.parse(calories),
              let proteinValue = Self.parse(protein),
              let fatValue = Self.parse(fat),
              let carbsValue = Self.parse(carbohydrates),
              !mealName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        else {
            errorMessage = "Completa nombre, calorías y macronutrientes con valores válidos."
            loadFailed = false
            return
        }
        loadTask?.cancel()
        isSaving = true
        loadFailed = false
        mutationTask = Task { [weak self] in
            guard let self else { return }
            defer { isSaving = false }
            do {
                summary = try await repository.addNutritionLog(NutritionLogRequest(
                    mealType: mealType.rawValue,
                    name: mealName,
                    calories: caloriesValue,
                    proteinGrams: proteinValue,
                    fatGrams: fatValue,
                    carbohydrateGrams: carbsValue,
                    date: nil))
                mealName = ""
                calories = ""
                protein = ""
                fat = ""
                carbohydrates = ""
                errorMessage = nil
                AppTelemetry.shared.track("meal_logged", ["meal_type": mealType.rawValue])
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.displayMessage
            }
        }
    }

    func cancel() {
        loadTask?.cancel()
        loadTask = nil
    }

    func resetForSignOut() {
        cancel()
        mutationTask?.cancel()
        mutationTask = nil
        targets = nil
        summary = nil
        errorMessage = nil
        loadFailed = false
        setupHint = nil
    }

    private static func parse(_ value: String) -> Double? {
        Double(value.replacingOccurrences(of: ",", with: "."))
    }
}
