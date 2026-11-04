import SwiftUI

struct DashboardView: View {
    @ObservedObject var viewModel: DashboardViewModel
    var coachViewModel: CoachViewModel?
    var activityAnalysisViewModel: ActivityAnalysisViewModel?
    var workoutPlanViewModel: WorkoutPlanViewModel?
    var coachInsightsViewModel: CoachInsightsViewModel?
    var goalViewModel: GoalViewModel?
    var nutritionViewModel: NutritionViewModel?
    var progressViewModel: ProgressViewModel?
    var engagementViewModel: EngagementViewModel?
    var sessionManager: SessionManager?
    var onSignOut: (() async throws -> Void)?
    var settingsViewModel: SettingsViewModel?
    @EnvironmentObject private var router: DeepLinkRouter
    @EnvironmentObject private var appConfig: AppConfigStore
    @EnvironmentObject private var betaFeedback: BetaFeedbackPrompt
    @EnvironmentObject private var consents: ConsentStore
    @State private var isShowingHealthConsent = false
    @State private var isShowingSettings = false
    @State private var isShowingBetaFeedback = false
    @State private var isShowingCoach = false
    @State private var isShowingActivityAnalysis = false
    @State private var isShowingWorkouts = false
    @State private var wellnessDestination: WellnessDestination?
    @State private var engagementDestination: EngagementDestination?
    @State private var isConfirmingSignOut = false
    @State private var isSigningOut = false

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    OfflineBanner()
                    if appConfig.requiresUpdate {
                        Label("Esta versión ya no está soportada. Actualiza la app desde TestFlight o el App Store.",
                              systemImage: "arrow.down.app")
                            .font(.footnote.weight(.semibold))
                            .foregroundStyle(Color.warningText)
                    }
                    header
                    if needsHealthConsentToContinue {
                        healthConsentCard
                    }
                    if consents.hasLoaded, consents.decision(.analytics) == .undecided {
                        AnalyticsConsentCard(consents: consents)
                    }
                    if betaFeedback.isVisible, settingsViewModel != nil {
                        betaFeedbackCard
                    }
                    connectionNotice
                    syncStatus
                    if case let .failed(error) = viewModel.summaryState {
                        ContentUnavailableView {
                            Label("No se pudieron cargar los datos", systemImage: "heart.slash")
                        } description: {
                            Text(error)
                        } actions: {
                            Button("Reintentar") { viewModel.retryHealthConnection() }
                        }
                    }
                    if let engagementViewModel, appConfig.isEnabled(.engagement) {
                        EngagementCard(viewModel: engagementViewModel) { engagementDestination = $0 }
                    }
                    metrics
                    healthNotice
                }
                .padding()
            }
            .background(Color(uiColor: .systemGroupedBackground))
            .navigationTitle("AI Fitness Coach")
            .toolbar {
                if sessionManager != nil {
                    ToolbarItem(placement: .topBarLeading) {
                        Button("Cerrar sesión", systemImage: "rectangle.portrait.and.arrow.right") {
                            isConfirmingSignOut = true
                        }
                        .disabled(isSigningOut)
                    }
                }
                if settingsViewModel != nil {
                    ToolbarItem(placement: .topBarLeading) {
                        Button("Ajustes", systemImage: "gearshape") {
                            isShowingSettings = true
                        }
                    }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Actualizar", systemImage: "arrow.clockwise") {
                        viewModel.refresh()
                    }
                    .disabled(viewModel.summaryState == .loading
                        || viewModel.connectionState != .connected)
                }
                if showsCoach {
                    ToolbarItem(placement: .topBarTrailing) {
                        Button("Coach", systemImage: "sparkles") {
                            isShowingCoach = true
                        }
                    }
                }
                if showsAnalysis {
                    ToolbarItem(placement: .topBarTrailing) {
                        Button("Análisis IA", systemImage: "chart.xyaxis.line") {
                            isShowingActivityAnalysis = true
                        }
                    }
                }
                if showsWorkouts {
                    ToolbarItem(placement: .topBarTrailing) {
                        Button("Rutinas", systemImage: "figure.run") {
                            isShowingWorkouts = true
                        }
                    }
                }
                if showsGoals || showsNutrition || showsProgress || showsEngagement {
                    ToolbarItem(placement: .topBarTrailing) {
                        Menu("Bienestar", systemImage: "target") {
                            if showsGoals {
                                Button("Objetivos") { wellnessDestination = .goals }
                            }
                            if showsNutrition {
                                Button("Nutrición") { wellnessDestination = .nutrition }
                            }
                            if showsProgress {
                                Button("Progreso") { wellnessDestination = .progress }
                            }
                            if showsEngagement {
                                Button("Resumen semanal") { engagementDestination = .weeklySummary }
                                Button("Logros") { engagementDestination = .achievements }
                            }
                        }
                    }
                }
            }
            .confirmationDialog(
                "¿Cerrar sesión?",
                isPresented: $isConfirmingSignOut,
                titleVisibility: .visible
            ) {
                Button("Cerrar sesión", role: .destructive) { signOut() }
                Button("Cancelar", role: .cancel) {}
            } message: {
                if viewModel.syncOverview.pendingCount > 0 {
                    Text("Hay \(viewModel.syncOverview.pendingCount) datos de salud sin enviar que se borrarán de este dispositivo. Volverán a leerse de Apple Health cuando inicies sesión.")
                } else {
                    Text("Tendrás que volver a iniciar sesión con Apple.")
                }
            }
            .sheet(isPresented: $isShowingHealthConsent) {
                HealthDataConsentView { granted in
                    guard await consents.set(.healthData, granted: granted), granted else { return }
                    if viewModel.connectionState == .connected {
                        // Already connected before consent existed: resume the paused uploads.
                        viewModel.resumeHealthSync()
                    } else {
                        viewModel.connectHealth()
                    }
                }
            }
            .sheet(isPresented: $isShowingBetaFeedback) {
                if let settingsViewModel {
                    SupportFormView(viewModel: settingsViewModel, kind: .feedback)
                }
            }
            .sheet(isPresented: $isShowingSettings) {
                if let settingsViewModel {
                    SettingsView(viewModel: settingsViewModel)
                        .environmentObject(appConfig)
                }
            }
            .sheet(isPresented: $isShowingCoach, onDismiss: refreshEngagement) {
                if let coachViewModel {
                    CoachView(viewModel: coachViewModel)
                }
            }
            .sheet(isPresented: $isShowingActivityAnalysis, onDismiss: refreshEngagement) {
                if let activityAnalysisViewModel {
                    ActivityAnalysisView(viewModel: activityAnalysisViewModel)
                }
            }
            .sheet(isPresented: $isShowingWorkouts, onDismiss: refreshEngagement) {
                if let workoutPlanViewModel {
                    WorkoutPlanView(
                        viewModel: workoutPlanViewModel,
                        insightsViewModel: coachInsightsViewModel)
                }
            }
            .sheet(item: $wellnessDestination, onDismiss: refreshEngagement) { destination in
                switch destination {
                case .goals:
                    if let goalViewModel { NutritionGoalsView(viewModel: goalViewModel) }
                case .nutrition:
                    if let nutritionViewModel { NutritionView(viewModel: nutritionViewModel) }
                case .progress:
                    if let progressViewModel { WeightProgressView(viewModel: progressViewModel) }
                }
            }
            .sheet(item: $engagementDestination, onDismiss: refreshEngagement) { destination in
                if let engagementViewModel {
                    switch destination {
                    case .weeklySummary: WeeklySummaryView(viewModel: engagementViewModel)
                    case .achievements: AchievementsView(viewModel: engagementViewModel)
                    }
                }
            }
            .onAppear {
                AppTelemetry.shared.track("screen_view", ["screen": "dashboard"])
                engagementViewModel?.load()
                openPendingDestination()
            }
            .onChange(of: router.pending) { _, _ in
                openPendingDestination()
            }
            .onDisappear {
                viewModel.cancelPendingWork()
            }
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(Date.now.formatted(date: .complete, time: .omitted))
                .font(.subheadline)
                .foregroundStyle(.secondary)
            Text("Tu resumen de hoy")
                .font(.largeTitle.bold())
            if viewModel.connectionState == .notConnected {
                Button {
                    // Explicit consent first (GDPR), then Apple's permission sheet.
                    if consents.isGranted(.healthData) {
                        viewModel.connectHealth()
                    } else {
                        isShowingHealthConsent = true
                    }
                } label: {
                    Label("Conectar Apple Health", systemImage: "heart.text.square")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .accessibilityHint("Abre la hoja de permisos de Salud")
                // Shown before the system sheet: what is read, why, and that it is read only.
                Text("Solo leemos pasos, energía, frecuencia cardiaca, peso, sueño y entrenamientos para tu resumen, tu progreso y tus objetivos. No escribimos en Salud ni usamos estos datos para publicidad. Puedes cambiar el permiso en Ajustes › Salud.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            } else if viewModel.permissionState == .requesting
                || viewModel.summaryState == .loading {
                ProgressView("Cargando datos de salud…")
            }
            if let updatedAt = viewModel.lastUpdatedAt {
                if viewModel.isShowingCachedData {
                    Label(
                        "Datos guardados el \(updatedAt.formatted(date: .abbreviated, time: .shortened)). Se actualizarán al recuperar la conexión.",
                        systemImage: "clock.arrow.circlepath")
                        .font(.footnote)
                        .foregroundStyle(Color.warningText)
                } else {
                    Text("Actualizado a las \(updatedAt.formatted(date: .omitted, time: .shortened))")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    /// Connected before the explicit health-data consent existed (or after withdrawing it):
    /// the server rejects uploads until the user decides.
    private var needsHealthConsentToContinue: Bool {
        viewModel.connectionState == .connected && consents.hasLoaded && !consents.isGranted(.healthData)
    }

    private var healthConsentCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("Sincronización en pausa", systemImage: "pause.circle")
                .font(.headline)
            Text("Necesitamos tu consentimiento explícito para guardar tus datos de Salud en nuestros servidores. Hasta entonces se quedan solo en tu iPhone.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            Button("Revisar y decidir") { isShowingHealthConsent = true }
                .buttonStyle(.borderedProminent)
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
        .accessibilityElement(children: .contain)
    }

    /// TestFlight builds only (see BetaFeedbackPrompt).
    private var betaFeedbackCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("¿Qué tal la beta?", systemImage: "star.bubble")
                .font(.headline)
            Text("Llevas unos días usando AI Coach. Dos minutos de comentarios nos ayudan a mejorarla.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            HStack {
                Button("Enviar comentarios") {
                    betaFeedback.dismiss(sentFeedback: true)
                    isShowingBetaFeedback = true
                }
                .buttonStyle(.borderedProminent)
                Button("Ahora no") { betaFeedback.dismiss(sentFeedback: false) }
                    .buttonStyle(.bordered)
            }
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
    }

    @ViewBuilder
    private var connectionNotice: some View {
        switch viewModel.connectionState {
        case .unavailable:
            Label(
                "Apple Health no está disponible en este dispositivo.",
                systemImage: "heart.slash"
            )
            .foregroundStyle(.secondary)
        case .notConnected:
            Label(
                "Conecta Apple Health para consultar tus métricas.",
                systemImage: "heart.text.square"
            )
            .foregroundStyle(.secondary)
        case .connected:
            switch viewModel.summaryState {
            case .noData:
                Text("No hay datos visibles. Comprueba que existan muestras y revisa los permisos de lectura en Salud. Apple no permite a la app confirmar si un permiso de lectura fue denegado.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            case .available:
                Label("Datos de Apple Health disponibles", systemImage: "checkmark.heart")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            case .notLoaded, .loading, .failed(_):
                EmptyView()
            }
        }
    }

    @ViewBuilder
    private var syncStatus: some View {
        let overview = viewModel.syncOverview
        if viewModel.connectionState == .connected {
            switch overview.state {
            case let .failed(message) where overview.pendingCount > 0:
                VStack(alignment: .leading, spacing: 6) {
                    Label("La sincronización necesita atención", systemImage: "exclamationmark.arrow.triangle.2.circlepath")
                        .font(.footnote.weight(.semibold))
                    Text(message)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                    Button("Reintentar sincronización") { viewModel.retryHealthConnection() }
                        .font(.footnote)
                }
            case .offline, .retrying:
                Label(
                    "\(overview.pendingCount) datos pendientes. Se enviarán automáticamente.",
                    systemImage: "arrow.triangle.2.circlepath")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            case .syncing:
                Label("Sincronizando con el servidor…", systemImage: "arrow.triangle.2.circlepath")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            default:
                if let lastSync = overview.lastSuccessAt {
                    Label(
                        "Sincronizado a las \(lastSync.formatted(date: .omitted, time: .shortened))",
                        systemImage: "checkmark.icloud")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    private var metrics: some View {
        LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 12) {
            metricCard("Pasos", value: viewModel.summary.steps, format: "%.0f", unit: "pasos", icon: "figure.walk")
            metricCard("Calorías", value: viewModel.summary.totalEnergyKilocalories ?? viewModel.summary.activeEnergyKilocalories, format: "%.0f", unit: "kcal", icon: "flame")
            metricCard("Pulso medio", value: viewModel.summary.averageHeartRateBeatsPerMinute, format: "%.0f", unit: "lpm", icon: "heart")
            metricCard("Último peso", value: viewModel.summary.latestWeightKilograms, format: "%.1f", unit: "kg", icon: "scalemass")
            metricCard("Sueño", value: viewModel.summary.sleepSeconds.map { $0 / 3_600 }, format: "%.1f", unit: "h", icon: "bed.double")
        }
    }

    private var healthNotice: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label("Tu entrenador", systemImage: "sparkles")
                .font(.headline)
            Text("Conecta Apple Health para ver tus métricas. Las recomendaciones de salud no sustituyen el consejo de profesionales sanitarios.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
    }

    private func metricCard(
        _ title: String,
        value: Double?,
        format: String,
        unit: String,
        icon: String
    ) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Label(title, systemImage: icon)
                .font(.subheadline)
                .foregroundStyle(.secondary)
            Text(value.map { String(format: format, $0) } ?? "—")
                .font(.title2.bold())
            Text(value == nil ? "Sin datos disponibles" : unit)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, minHeight: 105, alignment: .leading)
        .padding()
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
    }

    private func signOut() {
        guard !isSigningOut else { return }
        isSigningOut = true
        Task {
            defer { isSigningOut = false }
            do {
                if let onSignOut {
                    try await onSignOut()
                } else {
                    try await sessionManager?.signOut()
                }
            } catch {
                // The session is cleared locally either way; AppDependencies shows the error.
                NSLog("Session revocation failed: %@", error.localizedDescription)
            }
        }
    }

    private func openPendingDestination() {
        guard let destination = router.consume() else { return }
        isShowingCoach = false
        isShowingActivityAnalysis = false
        isShowingWorkouts = false
        wellnessDestination = nil
        engagementDestination = nil
        switch destination {
        case .dashboard:
            break
        case .coach:
            isShowingCoach = available(showsCoach, destination)
        case .analysis:
            isShowingActivityAnalysis = available(showsAnalysis, destination)
        case .workouts:
            isShowingWorkouts = available(showsWorkouts, destination)
        case .goals:
            wellnessDestination = available(showsGoals, destination) ? .goals : nil
        case .nutrition:
            wellnessDestination = available(showsNutrition, destination) ? .nutrition : nil
        case .progress:
            wellnessDestination = available(showsProgress, destination) ? .progress : nil
        case .summary:
            engagementDestination = available(showsEngagement, destination) ? .weeklySummary : nil
        case .achievements:
            engagementDestination = available(showsEngagement, destination) ? .achievements : nil
        }
    }

    /// A deep link or reminder can point at a screen that a feature flag has turned off.
    private func available(_ isShown: Bool, _ destination: AppDestination) -> Bool {
        if !isShown {
            AppTelemetry.shared.track("screen_unavailable", ["destination": destination.rawValue])
        }
        return isShown
    }

    // Each entry point needs its view model (configured backend) and its remote flag.
    private var showsCoach: Bool { coachViewModel != nil && appConfig.isEnabled(.coach) }
    private var showsAnalysis: Bool { activityAnalysisViewModel != nil && appConfig.isEnabled(.activityAnalysis) }
    private var showsWorkouts: Bool { workoutPlanViewModel != nil && appConfig.isEnabled(.workouts) }
    private var showsGoals: Bool { goalViewModel != nil && appConfig.isEnabled(.goals) }
    private var showsNutrition: Bool { nutritionViewModel != nil && appConfig.isEnabled(.nutrition) }
    private var showsProgress: Bool { progressViewModel != nil && appConfig.isEnabled(.progress) }
    private var showsEngagement: Bool { engagementViewModel != nil && appConfig.isEnabled(.engagement) }

    /// Reminders depend on what the user just did (a logged meal cancels today's goal reminder).
    private func refreshEngagement() {
        engagementViewModel?.load()
    }
}

private enum WellnessDestination: String, Identifiable {
    case goals
    case nutrition
    case progress

    var id: String { rawValue }
}

private enum EngagementDestination: String, Identifiable {
    case weeklySummary
    case achievements

    var id: String { rawValue }
}

/// Streak and shortcuts to the weekly summary and achievements.
private struct EngagementCard: View {
    @ObservedObject var viewModel: EngagementViewModel
    let open: (EngagementDestination) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let summary = viewModel.summary {
                StreakView(streak: summary.streak, compact: true)
                HStack {
                    Button("Resumen semanal", systemImage: "calendar") { open(.weeklySummary) }
                    Spacer()
                    Button("Logros \(summary.unlockedAchievements)/\(summary.achievements.count)", systemImage: "rosette") {
                        open(.achievements)
                    }
                }
                .font(.subheadline)
                .buttonStyle(.borderless)
                if let notifications = viewModel.notifications {
                    ReminderPrompt(notifications: notifications) { viewModel.enableReminders() }
                }
            } else if case .failed = viewModel.state {
                Button("Ver tu progreso semanal", systemImage: "calendar") { open(.weeklySummary) }
            } else {
                ProgressView("Cargando tu racha…")
            }
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
    }
}

private struct ReminderPrompt: View {
    @ObservedObject var notifications: NotificationScheduler
    let enable: () -> Void

    var body: some View {
        if notifications.canAskForPermission {
            Button("Activar recordatorios", systemImage: "bell", action: enable)
                .font(.footnote)
                .buttonStyle(.bordered)
        }
    }
}
