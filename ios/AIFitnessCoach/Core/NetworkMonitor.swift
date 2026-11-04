import Combine
import Foundation
import Network

/// Publishes whether the device currently has a usable network path.
@MainActor
final class NetworkMonitor: ObservableObject {
    @Published private(set) var isOnline = true

    private let monitor = NWPathMonitor()
    private let queue = DispatchQueue(label: "NetworkMonitor")
    private var onReconnect: (@MainActor () -> Void)?

    init() {
        monitor.pathUpdateHandler = { [weak self] path in
            let online = path.status == .satisfied
            Task { @MainActor [weak self] in
                self?.update(isOnline: online)
            }
        }
        monitor.start(queue: queue)
    }

    deinit {
        monitor.cancel()
    }

    /// Called every time the connection comes back, for example to resume queued uploads.
    func setReconnectHandler(_ handler: @escaping @MainActor () -> Void) {
        onReconnect = handler
    }

    private func update(isOnline online: Bool) {
        let reconnected = online && !isOnline
        isOnline = online
        if reconnected {
            onReconnect?()
        }
    }
}
