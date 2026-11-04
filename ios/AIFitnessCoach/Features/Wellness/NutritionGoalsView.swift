import SwiftUI

struct NutritionGoalsView: View {
    @ObservedObject var viewModel: GoalViewModel

    var body: some View { GoalsView(viewModel: viewModel) }
}
