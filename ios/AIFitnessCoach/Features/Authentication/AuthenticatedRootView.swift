import SwiftUI

struct AuthenticatedRootView: View {
    @ObservedObject var profileViewModel: UserProfileViewModel
    let userId: String?
    let dashboard: DashboardView

    var body: some View {
        Group {
            if profileViewModel.state == .ready {
                dashboard
            } else {
                UserProfileSetupView(viewModel: profileViewModel, userId: userId)
            }
        }
    }
}
