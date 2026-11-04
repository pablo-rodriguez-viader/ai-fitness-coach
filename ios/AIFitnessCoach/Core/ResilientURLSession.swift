import Foundation

enum ResilientURLSession {
    static func data(
        for request: URLRequest,
        using session: URLSession,
        attempts: Int = 3
    ) async throws -> (Data, URLResponse) {
        let retryableMethod = request.httpMethod == "GET" || request.httpMethod == "PUT"
        let maximumAttempts = retryableMethod ? max(1, attempts) : 1

        for attempt in 0..<maximumAttempts {
            do {
                let response = try await session.data(for: request)
                if attempt + 1 < maximumAttempts,
                   let http = response.1 as? HTTPURLResponse,
                   [408, 429, 502, 503, 504].contains(http.statusCode) {
                    try await pause(beforeAttempt: attempt + 1, response: http)
                    continue
                }
                return response
            } catch let error as URLError where attempt + 1 < maximumAttempts && isTransient(error) {
                try await pause(beforeAttempt: attempt + 1, response: nil)
            }
        }
        return try await session.data(for: request)
    }

    private static func isTransient(_ error: URLError) -> Bool {
        switch error.code {
        case .timedOut, .cannotFindHost, .cannotConnectToHost, .networkConnectionLost,
             .notConnectedToInternet, .dnsLookupFailed, .resourceUnavailable:
            true
        default:
            false
        }
    }

    private static func pause(beforeAttempt attempt: Int, response: HTTPURLResponse?) async throws {
        if let header = response?.value(forHTTPHeaderField: "Retry-After"),
           let seconds = Double(header), seconds >= 0, seconds <= 3 {
            try await Task.sleep(for: .seconds(seconds))
        } else {
            let milliseconds = attempt == 1 ? 350 : 900
            try await Task.sleep(for: .milliseconds(milliseconds))
        }
    }
}
