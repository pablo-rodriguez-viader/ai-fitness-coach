import SwiftUI

struct AchievementsView: View {
    @ObservedObject var viewModel: EngagementViewModel
    @Environment(\.dismiss) private var dismiss

    private let columns = [GridItem(.adaptive(minimum: 150), spacing: 12)]

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    OfflineBanner()
                    content
                }
                .padding()
            }
            .background(Color(uiColor: .systemGroupedBackground))
            .navigationTitle("Logros")
            .toolbar { SheetCloseButton(dismiss: dismiss) }
            .refreshable { viewModel.load() }
            .task {
                AppTelemetry.shared.track("screen_view", ["screen": "achievements"])
                viewModel.load()
            }
        }
    }

    @ViewBuilder
    private var content: some View {
        switch (viewModel.state, viewModel.summary) {
        case (_, .some(let summary)):
            StreakView(streak: summary.streak)
                .padding()
                .background(.background, in: RoundedRectangle(cornerRadius: 16))
            Text("\(summary.unlockedAchievements) de \(summary.achievements.count) logros desbloqueados")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            LazyVGrid(columns: columns, spacing: 12) {
                ForEach(sorted(summary.achievements)) { achievement in
                    card(achievement)
                }
            }
        case (.failed(let message), .none):
            InlineErrorView(message: message, retry: viewModel.load)
        default:
            ProgressView("Cargando logros…")
                .frame(maxWidth: .infinity)
        }
    }

    /// Unlocked first, then the ones closest to being unlocked.
    private func sorted(_ achievements: [AchievementInfo]) -> [AchievementInfo] {
        achievements.sorted { left, right in
            if left.unlocked != right.unlocked { return left.unlocked }
            return left.fractionComplete > right.fractionComplete
        }
    }

    private func card(_ achievement: AchievementInfo) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Image(systemName: achievement.unlocked ? "rosette" : "lock")
                .font(.title2)
                .foregroundStyle(achievement.unlocked ? Color.yellow : Color.secondary)
            Text(achievement.title)
                .font(.headline)
            Text(achievement.description)
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            if achievement.unlocked {
                if let day = EngagementDay.display(achievement.unlockedOn) {
                    Text("Conseguido el \(day)").font(.caption2).foregroundStyle(.secondary)
                }
            } else {
                ProgressView(value: achievement.fractionComplete)
                Text("\(achievement.progress) / \(achievement.target)")
                    .font(.caption2)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, minHeight: 150, alignment: .topLeading)
        .padding()
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
        .opacity(achievement.unlocked ? 1 : 0.85)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(achievement.unlocked
            ? "\(achievement.title), conseguido"
            : "\(achievement.title), \(achievement.progress) de \(achievement.target)")
    }
}
