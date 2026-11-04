import Charts
import SwiftUI

struct ProgressChartsView: View {
    @ObservedObject var viewModel: ProgressViewModel
    @Environment(\.dismiss) private var dismiss

    private struct ChartPoint: Identifiable {
        let date: Date
        let value: Double
        var id: Date { date }
    }

    var body: some View {
        NavigationStack {
            List {
                OfflineBanner()
                    .listRowBackground(Color.clear)
                SyncPendingNotice(impact: "Las gráficas pueden no incluir tus datos más recientes.")
                if viewModel.isLoading && viewModel.progress == nil {
                    ProgressView("Cargando progreso…")
                }
                if let error = viewModel.errorMessage {
                    Section {
                        InlineErrorView(message: error, retryTitle: "Volver a cargar", retry: viewModel.load)
                    }
                }
                if !viewModel.isLoading && viewModel.progress == nil && viewModel.errorMessage == nil {
                    ContentUnavailableView("Aún no hay progreso", systemImage: "chart.xyaxis.line",
                        description: Text("Sincroniza datos de salud y define un objetivo para ver tu evolución."))
                }
                if let goal = viewModel.progress?.activeGoal {
                    Section("Objetivo activo") {
                        Text(goal.typeTitle)
                        if let current = viewModel.progress?.currentWeightKilograms,
                           let target = viewModel.progress?.targetWeightKilograms {
                            Text("Peso: \(current, specifier: "%.1f") → \(target, specifier: "%.1f") kg")
                        }
                        if let percent = viewModel.progress?.goalCompletionPercent {
                            ProgressView(value: percent, total: 100)
                            Text("Progreso estimado: \(percent, specifier: "%.0f")%")
                        }
                    }
                }
                if viewModel.progress != nil {
                    progressSections
                }
            }
            .navigationTitle("Progreso")
            .toolbar { SheetCloseButton(dismiss: dismiss) }
            .refreshable { viewModel.load() }
            .task {
                AppTelemetry.shared.track("screen_view", ["screen": "progress"])
                viewModel.load()
            }
            .onDisappear { viewModel.cancel() }
        }
    }

    @ViewBuilder
    private var progressSections: some View {
        weightChartSection
        if let weeklyChange = viewModel.progress?.weightChangePerWeekKilograms {
            Section("Tendencia de peso") {
                Text("Cambio estimado: \(weeklyChange, specifier: "%+.2f") kg/semana")
            }
        }
        if let warnings = viewModel.progress?.safetyWarnings, !warnings.isEmpty {
            Section("Avisos de progreso") {
                ForEach(warnings, id: \.self) { warning in
                    Label(warning, systemImage: "exclamationmark.triangle")
                }
            }
        }
        chartSection("Evolución de calorías", unit: "kcal") { $0.calories }
        chartSection("Evolución de pasos", unit: "pasos") { $0.steps }
        chartSection("Evolución del sueño", unit: "h") { $0.sleepHours }
    }

    @ViewBuilder
    private var weightChartSection: some View {
        let points = (viewModel.progress?.history ?? []).compactMap { point -> ChartPoint? in
            guard let amount = point.weightKilograms else { return nil }
            return ChartPoint(date: point.date, value: amount)
        }
        Section("Evolución del peso") {
            if points.isEmpty {
                Text("Aún no hay datos suficientes.").foregroundStyle(.secondary)
            } else {
                Chart {
                    ForEach(points) { point in
                        LineMark(x: .value("Fecha", point.date), y: .value("Peso (kg)", point.value))
                        PointMark(x: .value("Fecha", point.date), y: .value("Peso (kg)", point.value))
                    }
                    if let target = viewModel.progress?.targetWeightKilograms {
                        RuleMark(y: .value("Peso objetivo", target))
                            .foregroundStyle(Color.successText)
                            .lineStyle(StrokeStyle(lineWidth: 1.5, dash: [5, 4]))
                            .annotation(position: .top, alignment: .leading) {
                                Text("Objetivo: \(target, specifier: "%.1f") kg")
                                    .font(.caption)
                                    .foregroundStyle(.primary)
                            }
                    }
                }
                .frame(height: 190)
                .accessibilityLabel("Gráfica del peso, su tendencia y el objetivo")
                if viewModel.progress?.targetWeightKilograms != nil {
                    Label("Línea discontinua: peso objetivo", systemImage: "line.diagonal")
                        .font(.caption)
                }
            }
        }
    }

    @ViewBuilder
    private func chartSection(
        _ title: String,
        unit: String,
        value: @escaping (ProgressPoint) -> Double?
    ) -> some View {
        let points = (viewModel.progress?.history ?? []).compactMap { point -> ChartPoint? in
            guard let amount = value(point) else { return nil }
            return ChartPoint(date: point.date, value: amount)
        }
        Section(title) {
            if points.isEmpty {
                Text("Aún no hay datos suficientes.").foregroundStyle(.secondary)
            } else {
                Chart {
                    ForEach(points) { point in
                        LineMark(x: .value("Fecha", point.date), y: .value(unit, point.value))
                        PointMark(x: .value("Fecha", point.date), y: .value(unit, point.value))
                    }
                }
                .frame(height: 170)
                .accessibilityLabel(title)
            }
        }
    }
}

struct WeightProgressView: View {
    @ObservedObject var viewModel: ProgressViewModel

    var body: some View { ProgressChartsView(viewModel: viewModel) }
}
