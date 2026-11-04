import SwiftUI
import UIKit

/// Status colours with at least 4.5:1 contrast (WCAG AA) for small text on the system
/// backgrounds, in light and dark mode. The plain system red/orange/green are below 4.5:1 on
/// white, so status text uses these instead.
extension Color {
    static let errorText = adaptive(light: 0xC4281C, dark: 0xFF6961)
    static let warningText = adaptive(light: 0xA14600, dark: 0xFFB340)
    static let successText = adaptive(light: 0x1E7B34, dark: 0x30D158)
    /// Background for white text (offline banner): 5:1 or more in both modes.
    static let warningFill = adaptive(light: 0xA14600, dark: 0x8A3D00)

    private static func adaptive(light: UInt32, dark: UInt32) -> Color {
        Color(uiColor: UIColor { traits in
            let hex = traits.userInterfaceStyle == .dark ? dark : light
            return UIColor(
                red: CGFloat((hex >> 16) & 0xFF) / 255,
                green: CGFloat((hex >> 8) & 0xFF) / 255,
                blue: CGFloat(hex & 0xFF) / 255,
                alpha: 1)
        })
    }
}

/// Thin banner shown at the top of a screen while the device is offline.
struct OfflineBanner: View {
    @EnvironmentObject private var network: NetworkMonitor
    var message = "Sin conexión. Mostramos los últimos datos disponibles."

    var body: some View {
        if !network.isOnline {
            Label(message, systemImage: "wifi.slash")
                .font(.footnote.weight(.medium))
                .foregroundStyle(.white)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(Color.warningFill, in: RoundedRectangle(cornerRadius: 10))
                .accessibilityAddTraits(.isStaticText)
                .transition(.move(edge: .top).combined(with: .opacity))
        }
    }
}

/// Error state used inside lists and forms. `retry` is only for failed loads: when saving
/// failed, reloading would not help, so the message is shown alone.
struct InlineErrorView: View {
    let message: String
    var retryTitle = "Reintentar"
    var retry: (() -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label(message, systemImage: "exclamationmark.triangle")
                .foregroundStyle(Color.errorText)
            if let retry {
                Button(retryTitle, action: retry)
                    .buttonStyle(.bordered)
            }
        }
        .accessibilityElement(children: .contain)
    }
}

/// HealthKit upload status shared by every screen that shows or uses synced health data.
/// AppDependencies keeps it in step with the dashboard's sync overview.
@MainActor
final class HealthSyncStatusStore: ObservableObject {
    @Published var overview: HealthSyncOverview = .idle

    var pendingCount: Int { overview.pendingCount }

    var needsAttention: Bool {
        if case .failed = overview.state { return overview.pendingCount > 0 }
        return false
    }
}

/// "Sync pending" state: tells the user that the latest health data has not reached the server
/// yet, so charts, AI answers or targets may not include it. Hidden when nothing is pending.
struct SyncPendingNotice: View {
    @EnvironmentObject private var status: HealthSyncStatusStore
    /// What the pending data affects on this screen, e.g. "Las gráficas pueden no incluirlos."
    var impact: String

    var body: some View {
        if status.pendingCount > 0 {
            Label {
                VStack(alignment: .leading, spacing: 2) {
                    Text(Self.title(pending: status.pendingCount, needsAttention: status.needsAttention))
                        .font(.footnote.weight(.semibold))
                    Text(impact).font(.footnote)
                }
            } icon: {
                Image(systemName: status.needsAttention
                    ? "exclamationmark.arrow.triangle.2.circlepath"
                    : "arrow.triangle.2.circlepath")
            }
            .foregroundStyle(status.needsAttention ? Color.warningText : Color.secondary)
            .accessibilityElement(children: .combine)
        }
    }

    static func title(pending: Int, needsAttention: Bool) -> String {
        let items = pending == 1 ? "1 dato de salud pendiente" : "\(pending) datos de salud pendientes"
        return needsAttention
            ? "\(items) de enviar. La sincronización necesita atención en el Dashboard."
            : "\(items) de enviar. Se enviarán automáticamente."
    }
}

/// Close button for screens presented as sheets.
struct SheetCloseButton: ToolbarContent {
    let dismiss: DismissAction

    var body: some ToolbarContent {
        ToolbarItem(placement: .cancellationAction) {
            Button("Cerrar") { dismiss() }
        }
    }
}
