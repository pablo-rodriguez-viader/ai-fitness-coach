import XCTest
@testable import AIFitnessCoach

final class UserProfileViewModelTests: XCTestCase {
    @MainActor
    func testMissingProfileCanBeCompletedAndOpensTheExistingFlow() async {
        let repository = FakeUserProfileRepository()
        let viewModel = UserProfileViewModel(repository: repository)
        viewModel.displayName = "Ana"
        viewModel.age = "35"
        viewModel.heightCentimeters = "168"
        viewModel.goal = .generalHealth

        viewModel.load(userId: "user-1")
        await waitUntil { viewModel.state == .needsSetup }
        viewModel.save()
        await waitUntil { viewModel.state == .ready }

        XCTAssertEqual(viewModel.profile?.displayName, "Ana")
        XCTAssertEqual(viewModel.profile?.age, 35)
        XCTAssertNil(viewModel.errorMessage)
    }

    @MainActor
    func testInvalidProfileInputIsKeptLocalAndExplained() {
        let viewModel = UserProfileViewModel(repository: FakeUserProfileRepository())
        viewModel.displayName = "Ana"
        viewModel.age = "12"
        viewModel.heightCentimeters = "168"

        viewModel.save()

        XCTAssertEqual(viewModel.state, .loading)
        XCTAssertTrue(viewModel.errorMessage?.contains("edad") == true)
    }

    @MainActor
    private func waitUntil(
        timeout: TimeInterval = 2,
        condition: @escaping @MainActor () -> Bool
    ) async {
        let deadline = Date.now.addingTimeInterval(timeout)
        while !condition() && Date.now < deadline {
            try? await Task.sleep(nanoseconds: 10_000_000)
        }
    }
}

private actor FakeUserProfileRepository: UserProfileRepositoryProtocol {
    private var profile: UserProfile?

    func fetch() async throws -> UserProfile {
        guard let profile else { throw UserProfileError.server(statusCode: 404) }
        return profile
    }

    func save(_ request: UserProfileRequest) async throws -> UserProfile {
        let saved = UserProfile(
            userId: "user-1",
            displayName: request.displayName,
            age: request.age,
            heightCentimeters: request.heightCentimeters,
            goal: request.goal)
        profile = saved
        return saved
    }
}
