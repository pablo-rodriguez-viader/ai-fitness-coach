import XCTest
@testable import AIFitnessCoach

/// Release candidate regressions: screen states (sync pending, load vs save errors) and values
/// that used to reach the UI untranslated.
@MainActor
final class ReleaseCandidateTests: XCTestCase {
    // MARK: Sync pending

    func testSyncPendingStateFollowsTheQueue() {
        let store = HealthSyncStatusStore()
        XCTAssertEqual(store.pendingCount, 0)
        XCTAssertFalse(store.needsAttention)

        store.overview = HealthSyncOverview(state: .offline, pendingCount: 12, lastSuccessAt: nil)
        XCTAssertEqual(store.pendingCount, 12)
        XCTAssertFalse(store.needsAttention, "Offline uploads resume on their own.")

        store.overview = HealthSyncOverview(state: .failed("HTTP 500"), pendingCount: 3, lastSuccessAt: nil)
        XCTAssertTrue(store.needsAttention)

        store.overview = HealthSyncOverview(state: .failed("HTTP 500"), pendingCount: 0, lastSuccessAt: nil)
        XCTAssertFalse(store.needsAttention, "Nothing pending means nothing for the user to fix.")
    }

    func testSyncPendingWordingIsSingularAndPlural() {
        XCTAssertEqual(
            SyncPendingNotice.title(pending: 1, needsAttention: false),
            "1 dato de salud pendiente de enviar. Se enviarán automáticamente.")
        XCTAssertTrue(SyncPendingNotice.title(pending: 7, needsAttention: true)
            .hasPrefix("7 datos de salud pendientes de enviar. La sincronización necesita atención"))
    }

    // MARK: Untranslated values

    func testGoalTypeAndStatusAreShownInSpanish() throws {
        let json = Data(#"""
        [{"id":"0E984725-C51C-4BF0-873B-3D1C8B6CBB5D","type":"FatLoss","status":"Active",
          "targetWeightKilograms":65,"startsOn":"2026-10-01","targetDate":"2027-01-01","createdAt":"2026-10-01T10:00:00Z"},
         {"id":"1E984725-C51C-4BF0-873B-3D1C8B6CBB5D","type":"GeneralHealth","status":"Paused",
          "targetWeightKilograms":null,"startsOn":"2026-09-01","targetDate":null,"createdAt":"2026-09-01T10:00:00Z"}]
        """#.utf8)
        let goals = try Self.decodeGoals(json)

        XCTAssertEqual(goals[0].statusTitle, "Activo")
        XCTAssertEqual(goals[1].statusTitle, "En pausa")
        XCTAssertNotEqual(goals[0].typeTitle, "FatLoss")
        XCTAssertNotEqual(goals[1].typeTitle, "GeneralHealth")
    }

    func testWorkoutEquipmentKeepsApiValuesButShowsSpanishLabels() {
        let options = WorkoutPlanView.equipmentOptions
        XCTAssertEqual(options.map(\.id), ["Dumbbells", "ResistanceBand", "Mat", "Treadmill"])
        XCTAssertFalse(options.contains { $0.title == $0.id }, "Every option needs a Spanish label.")
    }

    // MARK: Helpers

    /// Goal records decode with the same decoder the repository uses; fall back to the default.
    private static func decodeGoals(_ data: Data) throws -> [GoalRecord] {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let value = try decoder.singleValueContainer().decode(String.self)
            return EngagementRepository.parseDate(value) ?? Date(timeIntervalSince1970: 0)
        }
        return try decoder.decode([GoalRecord].self, from: data)
    }
}
