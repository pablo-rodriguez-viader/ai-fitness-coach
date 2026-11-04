import SwiftUI

struct ActivityAnalysisView: View {
    @ObservedObject var viewModel: ActivityAnalysisViewModel
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var network: NetworkMonitor
    @State private var question = "¿Cómo ha evolucionado mi actividad esta semana?"
    @State private var period = "last7Days"

    private let suggestedQuestions = [
        "¿Cómo ha evolucionado mi actividad esta semana?",
        "¿Qué tendencia muestran mis pasos?",
        "¿He mantenido una actividad constante?",
        "¿Qué área general podría priorizar?"
    ]

    var body: some View {
        NavigationStack {
            List {
                if !network.isOnline {
                    OfflineBanner(message: "Sin conexión. El análisis necesita internet.")
                        .listRowBackground(Color.clear)
                }
                SyncPendingNotice(impact: "El análisis puede no incluir tus datos más recientes.")
                Section {
                    Text("El análisis usa resúmenes agregados de actividad, sueño, peso y entrenamientos. No ofrece diagnósticos ni consejo médico.")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                if viewModel.isCheckingConsent {
                    Section("Consentimiento") {
                        ProgressView("Comprobando tu consentimiento…")
                    }
                } else if !viewModel.consentGranted {
                    Section("Consentimiento") {
                        Text("Si aceptas, enviaremos a OpenAI, un proveedor externo de IA, tu pregunta y resúmenes agregados de tu actividad, sueño, peso y entrenamientos para generar el análisis. No se envían muestras de Salud sin procesar, tu nombre ni tu correo. Puedes revocar el consentimiento en esta pantalla y borrar tus datos en Ajustes.")
                        Button("Aceptar y continuar") { viewModel.grantConsent() }
                            .accessibilityHint("Autoriza el uso de datos agregados de salud para el análisis")
                    }
                } else {
                    Section("Consentimiento") {
                        Label("Uso de datos agregados autorizado", systemImage: "checkmark.shield")
                        Button("Revocar consentimiento", role: .destructive) {
                            viewModel.revokeConsent()
                        }
                    }
                }
                Section("Pregunta") {
                    ForEach(suggestedQuestions, id: \.self) { suggested in
                        Button(suggested) { question = suggested }
                            .accessibilityLabel("Pregunta sugerida: \(suggested)")
                    }
                    TextField("¿Qué quieres analizar?", text: $question, axis: .vertical)
                        .lineLimit(2...5)
                    Picker("Periodo", selection: $period) {
                        Text("7 días").tag("last7Days")
                        Text("30 días").tag("last30Days")
                    }
                    Button {
                        viewModel.analyze(question: question, period: period)
                    } label: {
                        if viewModel.isLoading {
                            ProgressView("Analizando…")
                        } else {
                            Label("Analizar mi actividad", systemImage: "sparkles")
                        }
                    }
                    .disabled(viewModel.isLoading || !viewModel.consentGranted || question.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    if viewModel.isLoading {
                        Button("Cancelar análisis", role: .cancel) { viewModel.cancel() }
                    }
                }
                if let error = viewModel.errorMessage {
                    Section {
                        Text(error).foregroundStyle(Color.errorText)
                        Button("Reintentar") { viewModel.retry(question: question, period: period) }
                            .disabled(viewModel.isLoading)
                    }
                }
                if let analysis = viewModel.response?.analysis {
                    Section("Resumen") { Text(analysis.summary) }
                    Section("Observaciones") {
                        ForEach(analysis.observations) { item in
                            VStack(alignment: .leading, spacing: 5) {
                                Text(item.title ?? "\(item.category.capitalized) · \(item.trend.replacingOccurrences(of: "_", with: " "))")
                                    .font(.headline)
                                Text(item.description).foregroundStyle(.secondary)
                                ForEach(item.evidence ?? [], id: \.self) { evidence in
                                    Label(evidence, systemImage: "chart.bar").font(.caption)
                                }
                            }
                        }
                        if analysis.observations.isEmpty {
                            Text("No hay observaciones suficientes para este análisis.")
                                .foregroundStyle(.secondary)
                        }
                    }
                    if let suggestions = analysis.suggestions, !suggestions.isEmpty {
                        Section("Sugerencias generales") {
                            ForEach(suggestions) { suggestion in
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(suggestion.title).font(.headline)
                                    Text(suggestion.description).foregroundStyle(.secondary)
                                    Text("Prioridad: \(suggestion.priority)").font(.caption)
                                }
                            }
                        }
                    }
                    if let missingData = analysis.missingData, !missingData.isEmpty {
                        Section("Datos ausentes") {
                            ForEach(missingData, id: \.self) { Text($0) }
                        }
                    }
                    Section("Alcance y seguridad") {
                        Text(analysis.safetyNotice ?? analysis.limitations)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .navigationTitle("Análisis de actividad")
            .toolbar { SheetCloseButton(dismiss: dismiss) }
            .task {
                AppTelemetry.shared.track("screen_view", ["screen": "activity_analysis"])
                viewModel.loadConsent()
            }
            .onDisappear { viewModel.cancel() }
        }
    }
}
