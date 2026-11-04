import XCTest
@testable import AIFitnessCoach

final class MetabolicCalculatorTests: XCTestCase {
    func testMifflinStJeorForBothEquationSexes() throws {
        XCTAssertEqual(
            try MetabolicCalculator.basalMetabolicRate(
                weightKilograms: 80,
                heightCentimeters: 180,
                ageYears: 30,
                sex: .male
            ),
            1_780
        )
        XCTAssertEqual(
            try MetabolicCalculator.basalMetabolicRate(
                weightKilograms: 80,
                heightCentimeters: 180,
                ageYears: 30,
                sex: .female
            ),
            1_614
        )
    }

    func testRejectsInvalidMeasurements() {
        XCTAssertThrowsError(
            try MetabolicCalculator.basalMetabolicRate(
                weightKilograms: 0,
                heightCentimeters: 180,
                ageYears: 30,
                sex: .female
            )
        )
    }
}
