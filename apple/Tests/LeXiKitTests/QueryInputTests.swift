import Testing
@testable import LeXiKit

// Return in the iOS query field looks the query up wherever the cursor is; pasted line breaks stay.
struct QueryInputTests {
    @Test(arguments: [
        ("lucid", "lucid\n"), ("lucid", "luc\nid"), ("lucid", "\nlucid"), ("lucid", "\n"),
        ("serendipity", "seren\nity"), ("a\nb", "a\nb\n"), ("aa", "a\na"),
    ])
    func returnLooksUp(old: String, value: String) {
        #expect(QueryInput.typedReturn(old, value), "Return typed into \(old.debugDescription)")
    }

    @Test(arguments: [("", "foo\nbar"), ("hello", "hello world\nagain"), ("lucd", "lucid"), ("lucid", "lucd"), ("", "")])
    func otherEditsDoNot(old: String, value: String) {
        #expect(!QueryInput.typedReturn(old, value), "\(old.debugDescription) → \(value.debugDescription)")
    }
}
