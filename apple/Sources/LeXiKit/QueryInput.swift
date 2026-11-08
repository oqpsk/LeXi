import Foundation

// How typing in the query field reads, for front ends whose field takes several lines (iOS).
public enum QueryInput {
    // The edit Return makes: one line break at the cursor, or in place of the selected text (after
    // coming back to LeXi the query is selected). A paste brings more than a line break, so pasted
    // paragraphs keep theirs for translation.
    public static func typedReturn(_ old: String, _ value: String) -> Bool {
        let prefix = zip(old, value).prefix { $0 == $1 }.count
        let rest = min(old.count, value.count) - prefix
        let suffix = zip(old.reversed(), value.reversed()).prefix(rest).prefix { $0 == $1 }.count
        return value.count - prefix - suffix == 1 && value.dropFirst(prefix).first == "\n"
    }
}
