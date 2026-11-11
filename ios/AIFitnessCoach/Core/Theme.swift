import SwiftUI

/// Look of the app, taken from the logo: lime to teal on an almost black canvas.
/// The app always runs in dark mode (set on the root view), so these are fixed colours.
enum Brand {
    static let lime = Color(red: 0xB8 / 255, green: 0xEB / 255, blue: 0x3A / 255)
    static let teal = Color(red: 0x22 / 255, green: 0xB3 / 255, blue: 0xA6 / 255)
    /// Screen background.
    static let canvas = Color(red: 0x0B / 255, green: 0x0F / 255, blue: 0x14 / 255)
    /// Cards on top of the canvas.
    static let surface = Color(red: 0x16 / 255, green: 0x1D / 255, blue: 0x26 / 255)
    /// Text on lime/teal fills: 12:1 or more against both.
    static let onAccent = canvas

    static let gradient = LinearGradient(
        colors: [lime, teal],
        startPoint: .topLeading,
        endPoint: .bottomTrailing)
}

/// Rounded dark card with a faint lime-to-teal edge.
struct BrandCardModifier: ViewModifier {
    var cornerRadius: CGFloat = 18

    func body(content: Content) -> some View {
        content
            .background(Brand.surface, in: RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                    .strokeBorder(
                        LinearGradient(
                            colors: [Brand.lime.opacity(0.35), Brand.teal.opacity(0.35)],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing),
                        lineWidth: 1))
    }
}

extension View {
    func brandCard(cornerRadius: CGFloat = 18) -> some View {
        modifier(BrandCardModifier(cornerRadius: cornerRadius))
    }

    /// Lists and forms on the brand canvas instead of the system grey.
    func brandListBackground() -> some View {
        scrollContentBackground(.hidden)
            .background(Brand.canvas)
    }
}

/// Main call to action: gradient pill with dark text.
struct BrandButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.headline)
            .foregroundStyle(Brand.onAccent)
            .padding(.vertical, 14)
            .padding(.horizontal, 20)
            .frame(maxWidth: .infinity)
            .background(Brand.gradient, in: Capsule())
            .opacity(isEnabled ? (configuration.isPressed ? 0.8 : 1) : 0.45)
            .scaleEffect(configuration.isPressed ? 0.98 : 1)
            .animation(.easeOut(duration: 0.15), value: configuration.isPressed)
    }
}

extension ButtonStyle where Self == BrandButtonStyle {
    static var brand: BrandButtonStyle { BrandButtonStyle() }
}

/// The app logo (Gemini design) with iOS-style rounded corners.
struct BrandLogo: View {
    var size: CGFloat = 64

    var body: some View {
        Image("Logo")
            .resizable()
            .interpolation(.high)
            .scaledToFit()
            .frame(width: size, height: size)
            .clipShape(RoundedRectangle(cornerRadius: size * 0.225, style: .continuous))
            .accessibilityHidden(true)
    }
}
