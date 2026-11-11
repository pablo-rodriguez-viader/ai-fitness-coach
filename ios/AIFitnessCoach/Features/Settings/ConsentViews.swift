import SwiftUI

/// Explicit consent for processing synced health data (GDPR art. 9(2)(a)), shown before the
/// Apple Health permission sheet. Apple's sheet decides what the app may read on the iPhone;
/// this decides whether we may store and process it on our servers.
struct HealthDataConsentView: View {
    let onDecision: (Bool) async -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var isSaving = false

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    Label("Tus datos de salud", systemImage: "heart.text.square")
                        .font(.title2.bold())
                    consentPoint("Qué datos",
                        "Pasos, energía activa y en reposo, frecuencia cardiaca, peso, sueño y entrenamientos de Apple Health. Solo lectura: nunca escribimos en Salud.")
                    consentPoint("Para qué",
                        "Mostrar tu resumen diario y tu progreso y calcular tus objetivos de nutrición. Si aceptas también el consentimiento del Coach, sus respuestas usan resúmenes de estos datos.")
                    consentPoint("Dónde y cuánto tiempo",
                        "Se guardan cifrados en nuestros servidores mientras tengas la cuenta. Nunca se usan para publicidad ni se venden.")
                    consentPoint("Tu control",
                        "Puedes retirar este consentimiento cuando quieras en Ajustes › Desconectar Salud y borrar sus datos, o eliminar la cuenta. Sin este consentimiento la app funciona, pero sin datos de Salud.")
                    Text("Base legal: tu consentimiento explícito (art. 9.2.a RGPD).")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
                .padding()
            }
            .safeAreaInset(edge: .bottom) {
                VStack(spacing: 10) {
                    Button {
                        decide(true)
                    } label: {
                        if isSaving { ProgressView() } else { Text("Acepto").frame(maxWidth: .infinity) }
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    Button("Ahora no") { decide(false) }
                        .controlSize(.large)
                }
                .disabled(isSaving)
                .padding()
                .background(.bar)
            }
            .navigationTitle("Consentimiento")
            .navigationBarTitleDisplayMode(.inline)
            .interactiveDismissDisabled(isSaving)
        }
    }

    private func consentPoint(_ title: LocalizedStringKey, _ text: LocalizedStringKey) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).font(.headline)
            Text(text)
        }
        .accessibilityElement(children: .combine)
    }

    private func decide(_ granted: Bool) {
        isSaving = true
        Task {
            await onDecision(granted)
            isSaving = false
            dismiss()
        }
    }
}

/// One-time question about usage analytics and diagnostics (crashes, errors, screens).
struct AnalyticsConsentCard: View {
    @ObservedObject var consents: ConsentStore
    @State private var isSaving = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("Datos de uso y diagnóstico", systemImage: "chart.bar.doc.horizontal")
                .font(.headline)
            Text("¿Nos permites recibir datos de uso y diagnóstico (pantallas abiertas, errores y fallos) para mejorar la app? Nunca incluyen tus valores de salud ni tus conversaciones. Puedes cambiarlo en Ajustes.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
            HStack {
                Button("Permitir") { decide(true) }
                    .buttonStyle(.borderedProminent)
                Button("No permitir") { decide(false) }
                    .buttonStyle(.bordered)
            }
            .disabled(isSaving)
            if let error = consents.errorMessage {
                Text(error).font(.footnote).foregroundStyle(Color.errorText)
            }
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
        .brandCard()
    }

    private func decide(_ granted: Bool) {
        isSaving = true
        Task {
            await consents.set(.analytics, granted: granted)
            isSaving = false
        }
    }
}

/// Settings switch for usage analytics and diagnostics; the change is stored on the server.
struct ConsentToggle: View {
    @ObservedObject var consents: ConsentStore
    @State private var isSaving = false

    var body: some View {
        Toggle(isOn: Binding(
            get: { consents.isGranted(.analytics) },
            set: { granted in
                isSaving = true
                Task {
                    await consents.set(.analytics, granted: granted)
                    isSaving = false
                }
            })
        ) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Datos de uso y diagnóstico")
                Text("Pantallas, errores y fallos. Nunca valores de salud.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
        }
        .disabled(isSaving)
    }
}
