import Foundation

enum BiologicalSexForEquation {
    case male
    case female
}

enum MetabolicCalculator {
    static func basalMetabolicRate(
        weightKilograms: Double,
        heightCentimeters: Double,
        ageYears: Int,
        sex: BiologicalSexForEquation
    ) throws -> Double {
        guard weightKilograms > 0, heightCentimeters > 0, ageYears > 0 else {
            throw CalculationError.invalidMeasurement
        }

        let sexAdjustment = sex == .male ? 5.0 : -161.0
        return 10 * weightKilograms + 6.25 * heightCentimeters - 5 * Double(ageYears)
            + sexAdjustment
    }

    enum CalculationError: Error {
        case invalidMeasurement
    }
}
