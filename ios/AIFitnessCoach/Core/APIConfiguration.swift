import Foundation

struct APIConfiguration {
    let baseURL: URL

    init(bundle: Bundle = .main) throws {
        guard let value = bundle.object(forInfoDictionaryKey: "APIBaseURL") as? String,
              let url = URL(string: value),
              let scheme = url.scheme?.lowercased(),
              scheme == "https" || Self.isDebugLocalURL(url)
        else {
            throw APIConfigurationError.invalidBaseURL
        }
        baseURL = url
    }

    private static func isDebugLocalURL(_ url: URL) -> Bool {
        #if DEBUG
        return url.scheme == "http" && ["localhost", "127.0.0.1"].contains(url.host ?? "")
        #else
        return false
        #endif
    }
}

enum APIConfigurationError: Error, LocalizedError {
    case invalidBaseURL

    var errorDescription: String? {
        "Configura APIBaseURL con la URL HTTPS del backend."
    }
}
