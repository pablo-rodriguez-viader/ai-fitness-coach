import SwiftUI

struct CoachView: View {
    @ObservedObject var viewModel: CoachViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var question = ""
    @State private var isShowingHistory = false
    @FocusState private var isInputFocused: Bool

    private let suggestedQuestions = [
        "¿Cómo ha ido mi semana?",
        "¿Cómo han cambiado mis pasos?",
        "¿Qué datos de sueño tengo registrados?",
        "¿Estoy siendo constante con mis entrenamientos?",
        "¿Qué tendencias generales muestran mis datos?"
    ]

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                OfflineBanner(message: "Sin conexión. El Coach necesita internet para responder.")
                    .padding(.horizontal)
                SyncPendingNotice(impact: "Las respuestas pueden no tener en cuenta tus datos más recientes.")
                    .padding(.horizontal)
                    .padding(.vertical, 4)
                conversation
                composer
            }
            .background(Color(uiColor: .systemGroupedBackground))
            .navigationTitle("AI Coach")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                SheetCloseButton(dismiss: dismiss)
                ToolbarItem(placement: .topBarLeading) {
                    Button {
                        isShowingHistory = true
                        viewModel.loadConversations()
                    } label: {
                        Image(systemName: "clock.arrow.circlepath")
                    }
                    .accessibilityLabel("Historial de conversaciones")
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Nuevo", systemImage: "square.and.pencil") {
                        viewModel.startNewConversation()
                    }
                }
                if viewModel.consentGranted {
                    ToolbarItem(placement: .topBarTrailing) {
                        Menu {
                            Button("Revocar consentimiento", systemImage: "hand.raised.slash", role: .destructive) {
                                viewModel.revokeConsent()
                            }
                            .disabled(viewModel.isSending)
                        } label: {
                            Image(systemName: "hand.raised")
                        }
                        .accessibilityLabel("Privacidad del Coach")
                    }
                }
            }
            .sheet(isPresented: $isShowingHistory) {
                conversationHistory
            }
            .onAppear {
                AppTelemetry.shared.track("screen_view", ["screen": "coach"])
                viewModel.loadConsent()
            }
            .onDisappear { viewModel.cancelTasks() }
        }
    }

    private var conversation: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 14) {
                    if !viewModel.consentGranted {
                        if viewModel.isCheckingConsent && !viewModel.hasLoadedConsent {
                            ProgressView("Comprobando tu consentimiento…")
                                .frame(maxWidth: .infinity)
                                .padding()
                        } else {
                            consentNotice
                        }
                    } else if viewModel.messages.isEmpty && viewModel.pendingQuestion == nil {
                        introduction
                        suggestions
                    } else {
                        ForEach(viewModel.messages) { message in
                            messageCard(message)
                                .id(message.id)
                        }
                    }
                    if let pendingQuestion = viewModel.pendingQuestion {
                        Text(pendingQuestion)
                            .padding()
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Color.accentColor.opacity(0.12), in: RoundedRectangle(cornerRadius: 16))
                            .accessibilityLabel("Tu pregunta: \(pendingQuestion)")
                    }
                    if viewModel.isSending {
                        HStack(spacing: 10) {
                            ProgressView()
                            Text("Preparando un análisis de tus datos agregados…")
                                .font(.footnote)
                                .foregroundStyle(.secondary)
                            Spacer()
                            Button("Cancelar") { viewModel.cancelTasks() }
                                .accessibilityLabel("Cancelar envío")
                        }
                        .padding(.horizontal)
                        .accessibilityElement(children: .combine)
                    }
                    if let error = viewModel.errorMessage {
                        VStack(alignment: .leading, spacing: 8) {
                            Label(error, systemImage: "exclamationmark.triangle")
                                .font(.footnote)
                                .foregroundStyle(Color.errorText)
                                .accessibilityAddTraits(.updatesFrequently)
                            if viewModel.state == .offline {
                                Text("El Coach requiere conexión. Tu mensaje no se ha puesto en cola.")
                                    .font(.footnote)
                                    .foregroundStyle(.secondary)
                            }
                            if viewModel.state == .rateLimited {
                                Text("Puedes volver a intentarlo cuando se renueve tu cuota.")
                                    .font(.footnote)
                                    .foregroundStyle(.secondary)
                            }
                            if viewModel.state == .failed || viewModel.state == .offline {
                                Button("Reintentar") { viewModel.retry() }
                                    .buttonStyle(.bordered)
                                    .disabled(viewModel.isSending)
                            }
                        }
                        .padding(.horizontal)
                    }
                }
                .padding()
            }
            .onChange(of: viewModel.messages.count) {
                guard let lastMessage = viewModel.messages.last else { return }
                withAnimation {
                    proxy.scrollTo(lastMessage.id, anchor: .bottom)
                }
            }
        }
    }

    private var consentNotice: some View {
        VStack(alignment: .leading, spacing: 12) {
            Label("Controlas el uso de tus datos", systemImage: "hand.raised")
                .font(.headline)
            Text("Si aceptas, enviaremos a OpenAI, un proveedor externo de IA, tu pregunta y un resumen mínimo de tu perfil, objetivos y actividad (por ejemplo, pasos medios o sueño de la semana) para generar la respuesta. No se envían muestras de Salud sin procesar, tu nombre ni tu correo. Puedes retirar el consentimiento con el botón de privacidad (mano) de esta pantalla, y borrar tus conversaciones en Ajustes.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            Text("El Coach ofrece orientación general de actividad y bienestar; no diagnostica ni sustituye a profesionales sanitarios.")
                .font(.footnote)
                .foregroundStyle(.secondary)
            Button("Aceptar y continuar") { viewModel.grantConsent() }
                .buttonStyle(.borderedProminent)
                .frame(minHeight: 44)
                .disabled(viewModel.isSending)
                .accessibilityHint("Autoriza el uso de datos de salud agregados para generar respuestas del Coach.")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
    }

    private var introduction: some View {
        VStack(alignment: .leading, spacing: 10) {
            Image(systemName: "sparkles")
                .font(.largeTitle)
                .foregroundStyle(.tint)
            Text("Tu progreso, con contexto")
                .font(.title2.bold())
            Text("Puedo ayudarte a entender tendencias generales de actividad física usando solo los datos agregados que sean relevantes para tu pregunta.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            Text("No doy consejos médicos, no creo rutinas completas y no modifico tus datos.")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding()
        .background(.background, in: RoundedRectangle(cornerRadius: 16))
    }

    private var suggestions: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Prueba a preguntar")
                .font(.headline)
            ForEach(suggestedQuestions, id: \.self) { suggestion in
                Button(suggestion) { send(suggestion) }
                    .buttonStyle(.bordered)
                    .controlSize(.small)
                    .frame(minHeight: 44)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func messageCard(_ message: CoachDisplayMessage) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(message.role == "user" ? "Tú" : "AI Coach")
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
                Spacer()
                Text(message.createdAt.formatted(date: .omitted, time: .shortened))
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
            Text(message.content)
                .font(.body)
                .textSelection(.enabled)
            if let analysis = message.analysis {
                ForEach(analysis.observations) { observation in
                    VStack(alignment: .leading, spacing: 6) {
                        Text(observation.title).font(.subheadline.bold())
                        Text(observation.description).font(.subheadline)
                        ForEach(observation.evidence) { evidence in
                            Label("\(evidence.metric): \(evidence.value) · \(evidence.period)",
                                  systemImage: "chart.bar")
                                .font(.caption)
                                .accessibilityLabel("Evidencia: \(evidence.metric), \(evidence.value), periodo \(evidence.period)")
                        }
                        Text("Confianza: \(observation.confidence)")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    .padding(10)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color(uiColor: .tertiarySystemGroupedBackground),
                                in: RoundedRectangle(cornerRadius: 10))
                }
                if !analysis.suggestions.isEmpty {
                    ForEach(analysis.suggestions) { suggestion in
                        Label("\(suggestion.title): \(suggestion.description) (requiere confirmación)",
                              systemImage: "lightbulb")
                            .font(.footnote)
                    }
                }
                if !analysis.missingData.isEmpty {
                    Label("Datos no disponibles: \(analysis.missingData.joined(separator: ", "))",
                          systemImage: "questionmark.circle")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
                ForEach(analysis.followUpQuestions, id: \.self) { followUp in
                    Button(followUp) { send(followUp) }
                        .buttonStyle(.bordered)
                        .controlSize(.small)
                }
                if let safetyNotice = analysis.safetyNotice {
                    Label(safetyNotice, systemImage: "info.circle")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
                HStack {
                    Button("Útil", systemImage: "hand.thumbsup") {
                        viewModel.submitFeedback(messageId: message.id, helpful: true)
                    }
                    .accessibilityLabel("Marcar respuesta como útil")
                    Button("No útil", systemImage: "hand.thumbsdown") {
                        viewModel.submitFeedback(messageId: message.id, helpful: false)
                    }
                    .accessibilityLabel("Marcar respuesta como no útil")
                }
                .buttonStyle(.borderless)
                .font(.caption)
            }
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            message.role == "user"
                ? Color.accentColor.opacity(0.12)
                : Color(uiColor: .secondarySystemGroupedBackground),
            in: RoundedRectangle(cornerRadius: 16))
    }

    private var composer: some View {
        HStack(alignment: .bottom, spacing: 10) {
            TextField("Pregunta sobre tu actividad…", text: $question, axis: .vertical)
                .lineLimit(1...5)
                .focused($isInputFocused)
                .textFieldStyle(.roundedBorder)
                .disabled(!viewModel.consentGranted || viewModel.isSending)
                .accessibilityLabel("Mensaje para AI Coach")
            Button {
                send(question)
            } label: {
                Image(systemName: "arrow.up.circle.fill")
                    .font(.system(size: 32))
            }
            .disabled(question.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                || viewModel.isSending || !viewModel.consentGranted)
            .accessibilityLabel("Enviar pregunta")
        }
        .padding()
        .background(.background)
    }

    private var conversationHistory: some View {
        NavigationStack {
            Group {
                if viewModel.isLoading && viewModel.conversations.isEmpty {
                    ProgressView("Cargando historial…")
                } else if viewModel.conversations.isEmpty {
                    ContentUnavailableView(
                        "Sin conversaciones",
                        systemImage: "bubble.left.and.bubble.right",
                        description: Text("Las conversaciones con el Coach aparecerán aquí."))
                } else {
                    List {
                        ForEach(viewModel.conversations) { item in
                            Button {
                                viewModel.openConversation(id: item.id)
                                isShowingHistory = false
                            } label: {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(item.lastMessagePreview ?? "Conversación")
                                        .lineLimit(2)
                                        .foregroundStyle(.primary)
                                    Text(item.updatedAt.formatted(date: .abbreviated, time: .shortened))
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                            }
                            .swipeActions {
                                Button("Eliminar", role: .destructive) {
                                    viewModel.deleteConversation(id: item.id)
                                }
                            }
                        }
                    }
                }
            }
            .navigationTitle("Historial")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Cerrar") { isShowingHistory = false }
                }
            }
        }
    }

    private func send(_ content: String) {
        guard !content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        question = ""
        isInputFocused = false
        viewModel.send(content)
    }
}
