import Foundation

/// Mirrors `GET /api/v1/engagement/summary`. Days ("yyyy-MM-dd") stay strings: they are calendar
/// days in the user's time zone, not instants.
struct EngagementSummary: Decodable, Equatable, Sendable {
    let generatedAt: Date
    let streak: StreakInfo
    let weeklySummary: WeeklySummaryInfo
    let achievements: [AchievementInfo]
    let adherence: AdherenceInfo
    let reminders: [ReminderInfo]

    var unlockedAchievements: Int { achievements.filter(\.unlocked).count }
}

struct StreakInfo: Decodable, Equatable, Sendable {
    let current: Int
    let longest: Int
    let activeToday: Bool
    let lastActiveDay: String?
}

struct WeeklySummaryInfo: Decodable, Equatable, Sendable {
    let from: String
    let to: String
    let activeDays: Int
    let workoutsCompleted: Int
    let workoutsPlanned: Int
    let previousWeekWorkoutsCompleted: Int
    let mealsLogged: Int
    let coachQueries: Int
    let feedbackSubmitted: Int
    let averageDailySteps: Double?
    let headline: String
    let highlights: [String]
}

struct AchievementInfo: Decodable, Equatable, Identifiable, Sendable {
    let id: String
    let title: String
    let description: String
    let unlocked: Bool
    let unlockedOn: String?
    let progress: Int
    let target: Int

    var fractionComplete: Double { target == 0 ? 0 : min(1, Double(progress) / Double(target)) }
}

struct AdherenceInfo: Decodable, Equatable, Sendable {
    let from: String
    let to: String
    let training: AdherenceRateInfo
    let nutrition: AdherenceRateInfo
    let goals: AdherenceRateInfo
}

struct AdherenceRateInfo: Decodable, Equatable, Sendable {
    let achieved: Int
    let expected: Int
    /// 0–1; nil when there was nothing to measure (no plan, no goal, feature unused).
    let rate: Double?
}

struct ReminderInfo: Decodable, Equatable, Sendable {
    let kind: String
    let title: String
    let body: String
    let deepLink: String
    let date: String
}

enum EngagementDay {
    /// Year, month and day of a "yyyy-MM-dd" string, independent of time zone.
    static func components(_ value: String) -> DateComponents? {
        let parts = value.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        return DateComponents(year: parts[0], month: parts[1], day: parts[2])
    }

    /// Readable day ("24 oct") in the user's calendar.
    static func display(_ value: String?, calendar: Calendar = .current) -> String? {
        guard let value, var components = components(value) else { return nil }
        components.hour = 12
        guard let date = calendar.date(from: components) else { return nil }
        return date.formatted(.dateTime.day().month(.abbreviated))
    }
}
