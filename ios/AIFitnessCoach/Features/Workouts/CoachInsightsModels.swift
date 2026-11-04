import Foundation

struct CoachInsightsResponse: Decodable, Sendable {
    let generatedAt: Date
    let patterns: [BehaviorPattern]
    let adherence: TrainingAdherenceSummary
    let trends: [CoachMetricTrend]
    let goals: [GoalAchievementSummary]
    let effectiveRecommendations: [RecommendationEffectiveness]
}

struct ProgressAnalysisResponse: Decodable, Sendable {
    let generatedAt: Date
    let trends: [CoachMetricTrend]
    let goals: [GoalAchievementSummary]
    let summary: String
}

struct BehaviorPattern: Decodable, Identifiable, Sendable {
    var id: String { key }
    let key: String
    let description: String
    let confidence: Double
    let evidenceCount: Int
}

struct CoachMetricTrend: Decodable, Identifiable, Sendable {
    var id: String { metric }
    let metric: String
    let change: Double?
    let unit: String
}

struct TrainingAdherenceSummary: Decodable, Sendable {
    let periodStart: Date
    let periodEnd: Date
    let plannedSessions: Int
    let completedSessions: Int
    let rate: Double

    var percentage: Int { Int((rate * 100).rounded()) }
}

struct GoalAchievementSummary: Decodable, Identifiable, Sendable {
    var id: UUID { goalId }
    let goalId: UUID
    let goal: String
    let status: String
    let progressPercent: Double?
}

struct RecommendationEffectiveness: Decodable, Identifiable, Sendable {
    var id: String { type }
    let type: String
    let outcomeCount: Int
    let averageScore: Double
    let successRate: Double
}

struct CoachRecommendationRecord: Decodable, Identifiable, Sendable {
    let id: UUID
    let workoutPlanId: UUID?
    let type: String
    let text: String
    let createdAt: Date
    let outcomeScore: Double?
    let feedback: String?
}

struct RecommendationRatingRequest: Encodable, Sendable {
    let recommendationId: UUID
    let score: Int
    let comment: String?
}
