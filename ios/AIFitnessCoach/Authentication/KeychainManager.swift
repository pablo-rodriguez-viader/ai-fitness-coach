import Foundation
import Security

struct KeychainManager {
    private let service = "com.aifitnesscoach.authentication"

    func save(_ value: String, for key: String) throws {
        guard let data = value.data(using: .utf8) else {
            throw KeychainError.invalidValue
        }

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key
        ]
        // HealthKit background delivery can launch the app while the device is locked, so the
        // session and device id must be readable after the first unlock. Items never leave
        // this device (no iCloud Keychain, no backups).
        let attributes: [String: Any] = [
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        ]
        let status = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if status == errSecItemNotFound {
            var insert = query
            attributes.forEach { insert[$0.key] = $0.value }
            let insertStatus = SecItemAdd(insert as CFDictionary, nil)
            guard insertStatus == errSecSuccess else {
                throw KeychainError.status(insertStatus)
            }
        } else if status != errSecSuccess {
            throw KeychainError.status(status)
        }
    }

    func read(_ key: String) throws -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound {
            return nil
        }
        guard status == errSecSuccess,
              let data = result as? Data,
              let value = String(data: data, encoding: .utf8)
        else {
            throw KeychainError.status(status)
        }
        return value
    }

    func delete(_ key: String) throws {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key
        ]
        let status = SecItemDelete(query as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw KeychainError.status(status)
        }
    }

    func deleteTokens() throws {
        try delete("access_token")
        try delete("refresh_token")
        try delete("user_id")
        try delete("access_expiry")
    }

    func deviceIdentifier() throws -> String {
        if let existing = try read("device_id") {
            // Re-saving upgrades items written by older builds to the current accessibility class.
            try? save(existing, for: "device_id")
            return existing
        }
        let identifier = UUID().uuidString
        try save(identifier, for: "device_id")
        return identifier
    }

    /// Rewrites the session items so builds that stored them as "when unlocked" migrate.
    func upgradeAccessibility(of keys: [String]) {
        for key in keys {
            guard let value = try? read(key) else { continue }
            try? save(value, for: key)
        }
    }
}

enum KeychainError: Error, LocalizedError {
    case invalidValue
    case status(OSStatus)

    var errorDescription: String? {
        switch self {
        case .invalidValue:
            "No se pudo codificar el token."
        case let .status(status):
            "Error de Keychain (OSStatus \(status))."
        }
    }

    /// The device is locked and the item is not readable yet; retry after unlock.
    var isDeviceLocked: Bool {
        if case let .status(status) = self { return status == errSecInteractionNotAllowed }
        return false
    }
}
