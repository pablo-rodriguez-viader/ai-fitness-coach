import Combine
import Foundation

@MainActor
final class ProgressViewModel: ObservableObject {
    @Published private(set) var progress: ProgressSnapshot?
    @Published private(set) var isLoading = false
    @Published private(set) var errorMessage: String?

    private let repository: WellnessRepository
    private var task: Task<Void, Never>?

    init(repository: WellnessRepository) {
        self.repository = repository
    }

    func load() {
        task?.cancel()
        task = Task { [weak self] in
            guard let self else { return }
            isLoading = true
            defer { isLoading = false }
            do {
                progress = try await repository.progress()
                errorMessage = nil
            } catch {
                guard !error.isCancellation else { return }
                errorMessage = error.displayMessage
            }
        }
    }

    func cancel() {
        task?.cancel()
        task = nil
    }

    func resetForSignOut() {
        cancel()
        progress = nil
        errorMessage = nil
    }
}
