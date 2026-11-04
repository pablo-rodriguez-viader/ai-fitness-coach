import Foundation

enum FitnessGoal: String, CaseIterable, Identifiable, Hashable {
    case fatLoss = "fat_loss"
    case muscleGain = "muscle_gain"
    case weightMaintenance = "weight_maintenance"
    case endurance
    case generalHealth = "general_health"

    var id: String { rawValue }
    var title: String {
        switch self {
        case .fatLoss: "Perder grasa"
        case .muscleGain: "Ganar músculo"
        case .weightMaintenance: "Mantener peso"
        case .endurance: "Mejorar resistencia"
        case .generalHealth: "Mejorar salud general"
        }
    }
}

enum NutritionSex: String, CaseIterable, Identifiable, Hashable {
    case male = "Male"
    case female = "Female"
    var id: String { rawValue }
}

enum NutritionActivityLevel: String, CaseIterable, Identifiable, Hashable {
    case sedentary = "Sedentary"
    case light = "Light"
    case moderate = "Moderate"
    case high = "High"
    case athlete = "Athlete"

    var id: String { rawValue }
    var title: String {
        switch self {
        case .sedentary: "Sedentario"
        case .light: "Ligero"
        case .moderate: "Moderado"
        case .high: "Alto"
        case .athlete: "Atleta"
        }
    }
}

enum MealType: String, CaseIterable, Identifiable, Hashable {
    case breakfast = "Breakfast"
    case lunch = "Lunch"
    case dinner = "Dinner"
    case snack = "Snack"

    var id: String { rawValue }
    var title: String {
        switch self {
        case .breakfast: "Desayuno"
        case .lunch: "Comida"
        case .dinner: "Cena"
        case .snack: "Snack"
        }
    }
}

struct GoalRequest: Encodable, Sendable {
    let type: String
    let targetWeightKilograms: Double?
    let targetDate: String?
}

struct GoalRecord: Decodable, Equatable, Identifiable, Sendable {
    let id: UUID
    let type: String
    let status: String
    let targetWeightKilograms: Double?
    let startsOn: Date
    let targetDate: Date?
    let createdAt: Date
}

struct NutritionProfileRequest: Encodable, Sendable {
    let sex: String
    let activityLevel: String
}

struct NutritionTargets: Decodable, Equatable, Sendable {
    let weightKilograms: Double
    let bmr: Double
    let tdee: Double
    let dailyCalorieTarget: Double
    let deficitOrSurplusCalories: Double
    let proteinGrams: Double
    let fatGrams: Double
    let carbohydrateGrams: Double
    let goal: String
    let safetyWarnings: [String]?
}

struct NutritionLogRequest: Encodable, Sendable {
    let mealType: String
    let name: String
    let calories: Double
    let proteinGrams: Double
    let fatGrams: Double
    let carbohydrateGrams: Double
    let date: String?
}

struct NutritionSummary: Decodable, Equatable, Sendable {
    let date: Date
    let calories: Double
    let proteinGrams: Double
    let fatGrams: Double
    let carbohydrateGrams: Double
    let calorieTarget: Double?
    let calorieAdherencePercent: Double?
}

struct ProgressPoint: Decodable, Equatable, Identifiable, Sendable {
    var id: Date { date }
    let date: Date
    let weightKilograms: Double?
    let calories: Double?
    let steps: Double?
    let sleepHours: Double?
}

struct ProgressSnapshot: Decodable, Equatable, Sendable {
    let activeGoal: GoalRecord?
    let currentWeightKilograms: Double?
    let targetWeightKilograms: Double?
    let weightChangeKilograms: Double?
    let goalCompletionPercent: Double?
    let history: [ProgressPoint]
    let weightChangePerWeekKilograms: Double?
    let safetyWarnings: [String]?
}

enum WellnessError: Error, LocalizedError {
    case invalidResponse
    case unauthorized
    case server(statusCode: Int)
    /// 400 from the API with its validation message (for example, missing profile or weight).
    case validation(String)

    var errorDescription: String? {
        switch self {
        case .invalidResponse: "El servidor devolvió datos no válidos."
        case .unauthorized: "La sesión ha caducado. Inicia sesión de nuevo."
        case let .server(code): "No se pudo completar la solicitud (HTTP \(code))."
        case let .validation(message): Self.localized(message)
        }
    }

    /// Targets cannot be calculated yet: an empty state to guide the user, not a failure.
    var isMissingSetup: Bool {
        guard case let .validation(message) = self else { return false }
        let text = message.lowercased()
        return text.contains("nutrition profile")
            || text.contains("user profile")
            || text.contains("weight measurement")
    }

    /// The API answers in English; show the known cases in Spanish.
    static func localized(_ message: String) -> String {
        let text = message.lowercased()
        if text.contains("weight measurement") {
            return "Necesitas al menos un peso registrado en Apple Health para calcular tus objetivos."
        }
        if text.contains("nutrition profile") {
            return "Elige tu sexo para la fórmula y tu nivel de actividad, y pulsa «Calcular mis objetivos»."
        }
        if text.contains("user profile") {
            return "Completa tu perfil antes de calcular objetivos."
        }
        if text.contains("18") {
            return "Los objetivos nutricionales solo están disponibles para mayores de 18 años."
        }
        return message
    }
}
