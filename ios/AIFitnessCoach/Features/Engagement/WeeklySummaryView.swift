import SwiftUI

struct WeeklySummaryView: View {
    @ObservedObject var viewModel: EngagementViewModel
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var network: NetworkMonitor

    var body: some View {
        NavigationStack {
            List {
                if !network.isOnline || viewModel.isShowingStaleData {
                    OfflineBanner(message: "Sin conexión. Mostramos tu último resumen.")
                        .listRowBackground(Color.clear)
                }
                content
            }
            .navigationTitle("Resumen semanal")
            .toolbar { SheetCloseButton(dismiss: dismiss) }
            .refreshable { viewModel.load() }
            .task {
                AppTelemetry.shared.track("screen_view", ["screen": "weekly_summary"])
                viewModel.load()
                await viewModel.notifications?.refreshStatus()
            }
        }
    }

    @ViewBuilder
    private var content: some View {
        switch (viewModel.state, viewModel.summary) {
        case (_, .some(let summary)):
            summarySections(summary)
        case (.failed(let message), .none):
            InlineErrorView(message: message, retry: viewModel.load)
        default:
            ProgressView("Preparando tu resumen…")
                .frame(maxWidth: .infinity)
        }
    }

    @ViewBuilder
    private func summarySections(_ summary: EngagementSummary) -> some View {
        let week = summary.weeklySummary
        Section {
            VStack(alignment: .leading, spacing: 6) {
                Text(week.headline).font(.headline)
                if let from = EngagementDay.display(week.from), let to = EngagementDay.display(week.to) {
                    Text("\(from) – \(to)").font(.caption).foregroundStyle(.secondary)
                }
            }
            StreakView(streak: summary.streak)
        }
        if viewModel.isEmpty {
            Section {
                ContentUnavailableView(
                    "Tu primera semana empieza hoy",
                    systemImage: "calendar.badge.plus",
                    description: Text("Genera un plan, completa un entrenamiento o registra una comida y aquí verás tu evolución."))
            }
        }
        Section("Esta semana") {
            stat("Días activos", "\(week.activeDays) de 7", icon: "calendar")
            stat("Entrenamientos", week.workoutsPlanned > 0
                 ? "\(week.workoutsCompleted) de \(week.workoutsPlanned)"
                 : "\(week.workoutsCompleted)", icon: "figure.run")
            stat("Semana anterior", "\(week.previousWeekWorkoutsCompleted) entrenamientos", icon: "arrow.uturn.backward")
            stat("Comidas registradas", "\(week.mealsLogged)", icon: "fork.knife")
            stat("Preguntas al Coach", "\(week.coachQueries)", icon: "sparkles")
            if let steps = week.averageDailySteps {
                stat("Pasos de media", steps.formatted(.number.precision(.fractionLength(0))), icon: "figure.walk")
            }
        }
        if !week.highlights.isEmpty {
            Section("Lo más destacado") {
                ForEach(week.highlights, id: \.self) { highlight in
                    Label(highlight, systemImage: "star")
                }
            }
        }
        Section {
            adherenceRow("Entrenamiento", summary.adherence.training, emptyText: "Sin entrenamientos planificados")
            adherenceRow("Nutrición", summary.adherence.nutrition, emptyText: "Aún no registras comidas")
            adherenceRow("Objetivos", summary.adherence.goals, emptyText: "Sin objetivo activo")
        } header: {
            Text("Adherencia · últimas 4 semanas")
        } footer: {
            Text("Entrenamiento: sesiones planificadas completadas. Nutrición: días con comidas registradas. Objetivos: días con progreso mientras el objetivo está activo.")
        }
        remindersSection
    }

    @ViewBuilder
    private var remindersSection: some View {
        if let notifications = viewModel.notifications {
            RemindersSection(notifications: notifications) { viewModel.enableReminders() }
        }
    }

    private struct RemindersSection: View {
        @ObservedObject var notifications: NotificationScheduler
        let enable: () -> Void

        var body: some View {
            Section("Recordatorios") {
                if notifications.isEnabled {
                    Label("Activados: entrenamiento pendiente (18:00), objetivo diario (20:00) y resumen del domingo (19:00).",
                          systemImage: "bell.badge")
                        .font(.footnote)
                } else if notifications.canAskForPermission {
                    Button("Activar recordatorios", systemImage: "bell", action: enable)
                } else {
                    Label("Los recordatorios están desactivados en Ajustes del iPhone.", systemImage: "bell.slash")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    private func stat(_ title: String, _ value: String, icon: String) -> some View {
        LabeledContent {
            Text(value).monospacedDigit()
        } label: {
            Label(title, systemImage: icon)
        }
    }

    private func adherenceRow(_ title: String, _ rate: AdherenceRateInfo, emptyText: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                Spacer()
                if let value = rate.rate {
                    Text(value, format: .percent.precision(.fractionLength(0)))
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }
            }
            if let value = rate.rate {
                ProgressView(value: value)
                Text("\(rate.achieved) de \(rate.expected) días")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                Text(emptyText).font(.caption).foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .combine)
    }
}
