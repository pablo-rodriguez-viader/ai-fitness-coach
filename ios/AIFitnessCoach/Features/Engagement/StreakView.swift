import SwiftUI

/// Days in a row with progress (a workout, a meal, feedback or a Coach question).
struct StreakView: View {
    let streak: StreakInfo
    var compact = false

    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            Image(systemName: streak.current > 0 ? "flame.fill" : "flame")
                .font(.system(size: compact ? 28 : 40))
                .foregroundStyle(streak.current > 0 ? Color.orange : Color.secondary)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 4) {
                Text(streak.current == 1 ? "1 día seguido" : "\(streak.current) días seguidos")
                    .font(compact ? .headline : .title2.bold())
                Text(statusText)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                if !compact, streak.longest > 0 {
                    Text("Mejor racha: \(streak.longest) día\(streak.longest == 1 ? "" : "s")")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 0)
        }
        .accessibilityElement(children: .combine)
    }

    private var statusText: String {
        if streak.current == 0 {
            return streak.longest == 0
                ? "Completa un entrenamiento o registra una comida para empezar tu racha."
                : "Tu racha se reinició. Hoy es buen día para empezar otra."
        }
        return streak.activeToday
            ? "Hoy ya has sumado. ¡Sigue así!"
            : "Registra algo hoy para no perder tu racha."
    }
}
