import XCTest
@testable import AIFitnessCoach

final class AIChatInfrastructureTests: XCTestCase {
    func testConversationStoreCachesAndClearsUserConversationData() async {
        let store = ConversationStore()
        let conversationId = UUID()
        let createdAt = Date(timeIntervalSince1970: 1_800_000_000)
        let message = AIMessage(
            id: UUID(),
            role: "assistant",
            content: "Activity summary",
            createdAt: createdAt)
        let conversation = AIConversation(
            id: conversationId,
            createdAt: createdAt,
            updatedAt: createdAt,
            contextVersion: 1,
            inputTokens: 12,
            outputTokens: 5,
            estimatedCost: 0.0001,
            messages: [message])
        let summary = AIConversationSummary(
            id: conversationId,
            createdAt: createdAt,
            updatedAt: createdAt,
            contextVersion: 1,
            inputTokens: 12,
            outputTokens: 5,
            estimatedCost: 0.0001,
            lastMessagePreview: "Activity summary")

        await store.update(conversation)
        await store.updateSummaries([summary])

        let cachedConversation = await store.conversation(id: conversationId)
        let cachedSummaries = await store.allSummaries()
        XCTAssertEqual(cachedConversation, conversation)
        XCTAssertEqual(cachedSummaries, [summary])

        await store.clear()

        let clearedConversation = await store.conversation(id: conversationId)
        let clearedSummaries = await store.allSummaries()
        XCTAssertNil(clearedConversation)
        XCTAssertTrue(clearedSummaries.isEmpty)
    }
}
