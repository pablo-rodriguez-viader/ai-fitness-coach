import Combine
import Foundation

@MainActor
final class UserProfileViewModel: ObservableObject {
    enum State: Equatable { case loading, needsSetup, ready, failed }

    @Published private(set) var state: State = .loading
    @Published private(set) var profile: UserProfile?
    @Published private(set) var isSaving = false
    @Published private(set) var errorMessage: String?
    @Published var displayName = ""
    @Published var age = ""
    @Published var heightCentimeters = ""
    @Published var goal: UserFitnessGoal = .generalHealth

    private let repository: any UserProfileRepositoryProtocol
    private let keychain: KeychainManager
    private var task: Task<Void, Never>?
    private var cachedUserId: String?

    init(repository: any UserProfileRepositoryProtocol, keychain: KeychainManager = KeychainManager()) {
        self.repository = repository
        self.keychain = keychain
    }

    func load(userId: String?) {
        if let cachedUserId, cachedUserId != userId {
            try? keychain.delete(Self.cacheKey(userId: cachedUserId))
        }
        cachedUserId = userId
        task?.cancel()
        task = Task { [weak self] in
            guard let self else { return }
            state = .loading
            let cached = userId.flatMap { try? readCache(userId: $0) }
            do {
                let profile = try await repository.fetch()
                apply(profile)
                writeCache(profile)
                state = .ready
                errorMessage = nil
            } catch let error as UserProfileError where error.isMissing {
                state = .needsSetup
                errorMessage = nil
            } catch {
                guard !error.isCancellation else { return }
                if let cached {
                    apply(cached)
                    state = .ready
                    errorMessage = "Sin conexión: se muestra el último perfil guardado."
                } else {
                    state = .failed
                    errorMessage = error.displayMessage
                    error.track(area: "profile_load")
                }
            }
        }
    }

    func save() {
        guard !isSaving else { return }
        guard let ageValue = Int(age), (13...120).contains(ageValue),
              let heightValue = Double(heightCentimeters.replacingOccurrences(of: ",", with: ".")),
              heightValue > 0, heightValue <= 300,
              !displayName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            errorMessage = "Introduce nombre, edad entre 13 y 120 años y altura válida."
            return
        }
        task?.cancel()
        isSaving = true
        task = Task { [weak self] in
            guard let self else { return }
            defer { isSaving = false }
            do {
                let saved = try await repository.save(UserProfileRequest(
                    displayName: displayName.trimmingCharacters(in: .whitespacesAndNewlines),
                    age: ageValue,
                    heightCentimeters: heightValue,
                    goal: goal.rawValue))
                apply(saved)
                writeCache(saved)
                state = .ready
                errorMessage = nil
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }

    func retry(userId: String?) { load(userId: userId) }

    func resetForSignOut() {
        task?.cancel()
        if let cachedUserId {
            try? keychain.delete(Self.cacheKey(userId: cachedUserId))
        }
        cachedUserId = nil
        profile = nil
        state = .loading
        errorMessage = nil
    }

    private func apply(_ profile: UserProfile) {
        self.profile = profile
        displayName = profile.displayName
        age = String(profile.age)
        heightCentimeters = String(profile.heightCentimeters)
        goal = UserFitnessGoal(rawValue: profile.goal) ?? .generalHealth
    }

    private static func cacheKey(userId: String) -> String { "user-profile-\(userId)" }

    private func writeCache(_ profile: UserProfile) {
        guard let data = try? JSONEncoder().encode(profile),
              let value = String(data: data, encoding: .utf8) else { return }
        try? keychain.save(value, for: Self.cacheKey(userId: profile.userId))
    }

    private func readCache(userId: String) throws -> UserProfile? {
        guard let value = try keychain.read(Self.cacheKey(userId: userId)),
              let data = value.data(using: .utf8) else { return nil }
        return try JSONDecoder().decode(UserProfile.self, from: data)
    }
}
