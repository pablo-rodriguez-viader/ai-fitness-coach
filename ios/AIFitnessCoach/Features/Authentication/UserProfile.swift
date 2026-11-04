import Foundation

enum UserFitnessGoal: String, CaseIterable, Identifiable {
    case generalHealth = "general_health"
    case fatLoss = "fat_loss"
    case muscleGain = "muscle_gain"
    case maintenance = "maintenance"
    case endurance = "endurance"

    var id: String { rawValue }
    var title: String {
        switch self {
        case .generalHealth: "Salud general"
        case .fatLoss: "Pérdida de grasa"
        case .muscleGain: "Ganancia muscular"
        case .maintenance: "Mantenimiento"
        case .endurance: "Resistencia"
        }
    }
}

struct UserProfile: Codable, Sendable {
    let userId: String
    let displayName: String
    let age: Int
    let heightCentimeters: Double
    let goal: String
}

struct UserProfileRequest: Encodable, Sendable {
    let displayName: String
    let age: Int
    let heightCentimeters: Double
    let goal: String
}
