import XCTest
@testable import AIFitnessCoach

/// The "what do you need" text the AI uses to tailor a routine.
@MainActor
final class WorkoutNeedsTests: XCTestCase {
    func testEmptyNeedsAreNotSent() {
        XCTAssertNil(WorkoutPlanViewModel.trimmedNeeds(""))
        XCTAssertNil(WorkoutPlanViewModel.trimmedNeeds("  \n "))
    }

    func testNeedsAreTrimmedAndCappedLikeTheServer() {
        XCTAssertEqual(WorkoutPlanViewModel.trimmedNeeds("  rodilla  "), "rodilla")
        let long = String(repeating: "a", count: WorkoutPlanViewModel.maximumNeedsLength + 20)
        XCTAssertEqual(WorkoutPlanViewModel.trimmedNeeds(long)?.count, WorkoutPlanViewModel.maximumNeedsLength)
    }

    func testRequestEncodesNeeds() throws {
        let request = WorkoutPlanRequest(
            goal: "general_health", days: 1, availableMinutes: 30, equipment: ["Bodyweight"], needs: "principiante")
        let json = try XCTUnwrap(String(data: JSONEncoder().encode(request), encoding: .utf8))
        XCTAssertTrue(json.contains("\"needs\":\"principiante\""))
    }
}
