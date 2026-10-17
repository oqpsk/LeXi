import AVFoundation
import Observation

// Local speech with system voices, started only by an explicit click. The voice follows the
// text's language, matching the Windows "auto" choice. Online speech services are not yet
// available on macOS (FEATURES.md).
@MainActor @Observable
public final class SpeechController: NSObject, AVSpeechSynthesizerDelegate {
    public private(set) var speaking = false
    @ObservationIgnored private let synthesizer = AVSpeechSynthesizer()

    public override init() {
        super.init()
        synthesizer.delegate = self
    }

    public func toggle(_ text: String) {
        if speaking { stop(); return }
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed.count <= 5000 else { return }
        let utterance = AVSpeechUtterance(string: trimmed)
        utterance.voice = AVSpeechSynthesisVoice(language: Self.containsChinese(trimmed) ? "zh-CN" : "en-US")
        speaking = true
        synthesizer.speak(utterance)
    }

    public func stop() {
        synthesizer.stopSpeaking(at: .immediate)
        speaking = false
    }

    static func containsChinese(_ text: String) -> Bool {
        text.unicodeScalars.contains { (0x3400...0x9FFF).contains($0.value) }
    }

    nonisolated public func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didFinish utterance: AVSpeechUtterance) {
        Task { @MainActor in self.speaking = false }
    }
    nonisolated public func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didCancel utterance: AVSpeechUtterance) {
        Task { @MainActor in self.speaking = false }
    }
}
