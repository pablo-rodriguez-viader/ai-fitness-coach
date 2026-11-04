import SwiftUI

/// Feedback Center, bug report and support request: one form with the fields each kind needs.
struct SupportFormView: View {
    @ObservedObject var viewModel: SettingsViewModel
    let kind: SupportReportKind
    @Environment(\.dismiss) private var dismiss
    @State private var draft: SupportReportDraft

    init(viewModel: SettingsViewModel, kind: SupportReportKind) {
        self.viewModel = viewModel
        self.kind = kind
        _draft = State(initialValue: SupportReportDraft(
            kind: kind,
            category: Self.categories(for: kind).first?.id ?? "general",
            message: "",
            rating: kind == .feedback ? 4 : nil,
            contactEmail: "",
            includeDiagnostics: kind == .bug))
    }

    var body: some View {
        NavigationStack {
            Form {
                if kind == .feedback {
                    Section("¿Qué te parece la app?") {
                        RatingPicker(rating: Binding(
                            get: { draft.rating ?? 0 },
                            set: { draft.rating = $0 }))
                    }
                }
                Section("Tema") {
                    Picker("Tema", selection: $draft.category) {
                        ForEach(Self.categories(for: kind)) { category in
                            Text(category.title).tag(category.id)
                        }
                    }
                    .pickerStyle(.menu)
                }
                Section {
                    TextEditor(text: $draft.message)
                        .frame(minHeight: 160)
                        .overlay(alignment: .topLeading) {
                            if draft.message.isEmpty {
                                Text(placeholder)
                                    .foregroundStyle(.tertiary)
                                    .padding(.top, 8)
                                    .padding(.leading, 4)
                                    .allowsHitTesting(false)
                            }
                        }
                } header: {
                    Text(kind == .bug ? "¿Qué ha pasado?" : "Mensaje")
                } footer: {
                    Text("\(draft.trimmedMessage.count)/\(SupportReportDraft.maximumMessageLength) · No incluyas datos médicos que no quieras compartir.")
                }
                if kind != .feedback {
                    Section {
                        TextField("Correo para responderte (opcional)", text: $draft.contactEmail)
                            .keyboardType(.emailAddress)
                            .textContentType(.emailAddress)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                    } footer: {
                        Text("Si lo dejas vacío te responderemos por TestFlight o el canal de la beta.")
                    }
                }
                Section {
                    Toggle("Adjuntar diagnóstico técnico", isOn: $draft.includeDiagnostics)
                } footer: {
                    Text("Versión, dispositivo, conexión, estado de la sincronización y los últimos eventos de la app. Nunca incluye tus valores de salud ni tus conversaciones.")
                }
                if case let .failed(message) = viewModel.submitState {
                    Section {
                        Text(message).foregroundStyle(Color.errorText)
                    }
                }
            }
            .navigationTitle(kind.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Cancelar") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    if viewModel.submitState == .sending {
                        ProgressView()
                    } else {
                        Button("Enviar") { send() }
                            .disabled(draft.trimmedMessage.isEmpty)
                    }
                }
            }
            .alert("¡Gracias!", isPresented: Binding(
                get: { viewModel.submitState == .sent },
                set: { if !$0 { viewModel.resetSubmitState(); dismiss() } })
            ) {
                Button("Aceptar") {}
            } message: {
                Text(kind == .feedback
                    ? "Hemos recibido tus comentarios."
                    : "Hemos recibido tu mensaje. Lo revisaremos lo antes posible.")
            }
            .onAppear {
                viewModel.resetSubmitState()
                AppTelemetry.shared.track("screen_view", ["screen": "support_\(kind.rawValue)"])
            }
        }
    }

    private var placeholder: String {
        switch kind {
        case .feedback: "Qué te gusta, qué echas en falta o qué cambiarías."
        case .bug: "Describe el error y los pasos para reproducirlo: qué pantalla, qué tocaste y qué esperabas."
        case .support: "Cuéntanos en qué podemos ayudarte."
        }
    }

    private func send() {
        Task { _ = await viewModel.submit(draft) }
    }

    struct Category: Identifiable, Hashable {
        let id: String
        let title: String
    }

    static func categories(for kind: SupportReportKind) -> [Category] {
        switch kind {
        case .feedback:
            [Category(id: "general", title: "General"),
             Category(id: "coach", title: "AI Coach"),
             Category(id: "workouts", title: "Entrenamientos"),
             Category(id: "nutrition", title: "Nutrición"),
             Category(id: "goals", title: "Objetivos y progreso")]
        case .bug:
            [Category(id: "sync", title: "Sincronización con Salud"),
             Category(id: "dashboard", title: "Dashboard"),
             Category(id: "coach", title: "AI Coach / Análisis"),
             Category(id: "workouts", title: "Entrenamientos"),
             Category(id: "nutrition", title: "Nutrición / Objetivos"),
             Category(id: "notifications", title: "Recordatorios"),
             Category(id: "login", title: "Inicio de sesión"),
             Category(id: "crash", title: "La app se cierra"),
             Category(id: "other", title: "Otro")]
        case .support:
            [Category(id: "account", title: "Cuenta y acceso"),
             Category(id: "privacy", title: "Privacidad y datos"),
             Category(id: "health", title: "Apple Health"),
             Category(id: "other", title: "Otro")]
        }
    }
}

private struct RatingPicker: View {
    @Binding var rating: Int

    var body: some View {
        HStack(spacing: 12) {
            ForEach(1...5, id: \.self) { value in
                Button {
                    rating = value
                } label: {
                    Image(systemName: value <= rating ? "star.fill" : "star")
                        .font(.title2)
                        .foregroundStyle(value <= rating ? .yellow : .secondary)
                }
                .buttonStyle(.borderless)
                .accessibilityLabel("\(value) de 5")
            }
        }
        .frame(maxWidth: .infinity)
    }
}
