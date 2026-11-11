import Foundation

struct WorkoutPlanRequest: Encodable, Sendable {
    let goal: String
    let days: Int
    let availableMinutes: Int
    let equipment: [String]
    /// Level, injuries and preferences in the user's words; nil when empty.
    let needs: String?
}

struct WorkoutFeedbackRequest: Encodable, Sendable {
    let workoutDayId: UUID
    let completed: Bool
    let difficulty: String
    let comment: String?
}

struct WorkoutPlan: Decodable, Equatable, Identifiable, Sendable {
    let id: UUID
    let goal: String
    let startsOn: Date
    let endsOn: Date
    let createdAt: Date
    let days: [WorkoutDay]
}

struct WorkoutDay: Decodable, Equatable, Identifiable, Sendable {
    let id: UUID
    let date: Date
    let title: String
    let recoveryNote: String
    let exercises: [WorkoutExercise]
}

struct WorkoutExercise: Decodable, Equatable, Identifiable, Sendable {
    let id: UUID
    let position: Int
    let name: String
    let sets: String?
    let repetitions: String?
    let durationMinutes: Int?
    let restSeconds: Int?
    let instructions: String?
}

struct WorkoutRecommendation: Decodable, Equatable, Identifiable, Sendable {
    var id: String { "\(type)-\(createdAt.timeIntervalSince1970)" }
    let type: String
    let text: String
    let createdAt: Date
}

struct WorkoutHistory: Decodable, Equatable, Identifiable, Sendable {
    let id: UUID
    let workoutPlanId: UUID
    let workoutDayId: UUID
    let goal: String
    let date: Date
    let title: String
    let completed: Bool
    let difficulty: String?
    let recordedAt: Date
}

struct WorkoutFeedbackResponse: Decodable, Sendable {
    let id: UUID
    let completed: Bool
    let difficulty: String
    let adaptation: String
}

struct WorkoutCompletionResponse: Decodable, Sendable {
    let id: UUID
    let workoutDayId: UUID
    let completed: Bool
    let recordedAt: Date
}

struct RecommendationFeedbackResponse: Decodable, Sendable {
    let accepted: Bool
}

enum WorkoutGoal: String, CaseIterable, Identifiable, Hashable {
    case fatLoss = "fat_loss"
    case muscleGain = "muscle_gain"
    case maintenance
    case cardiovascularEndurance = "cardiovascular_endurance"
    case running
    case generalHealth = "general_health"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .fatLoss: "Pérdida de grasa"
        case .muscleGain: "Ganancia muscular"
        case .maintenance: "Mantenimiento"
        case .cardiovascularEndurance: "Resistencia cardiovascular"
        case .running: "Running"
        case .generalHealth: "Salud general"
        }
    }
}

enum WorkoutFeedbackDifficulty: String, CaseIterable, Identifiable, Hashable {
    case veryEasy = "VeryEasy"
    case easy = "Easy"
    case appropriate = "Appropriate"
    case hard = "Hard"
    case veryHard = "VeryHard"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .veryEasy: "Muy fácil"
        case .easy: "Fácil"
        case .appropriate: "Adecuado"
        case .hard: "Difícil"
        case .veryHard: "Muy difícil"
        }
    }
}

enum WorkoutRepositoryError: Error, LocalizedError {
    case invalidResponse
    case unauthorized
    case server(statusCode: Int)
    case unavailable

    var errorDescription: String? {
        switch self {
        case .invalidResponse: "El servidor devolvió una respuesta de entrenamiento no válida."
        case .unauthorized: "La sesión ha caducado. Inicia sesión de nuevo."
        case .server(429): "Has solicitado varios planes seguidos. Espera un minuto y vuelve a intentarlo."
        case let .server(statusCode): "No se pudo completar la solicitud (HTTP \(statusCode))."
        case .unavailable: "El entrenador no está disponible temporalmente."
        }
    }
}
