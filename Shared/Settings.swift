import Foundation

// Shared settings store for all After Dork modules. One plist holds a dict
// per module. The savers read it directly from disk (bypassing cfprefsd so a
// fresh activation always sees the latest values). The control panel writes
// it to BOTH the real preferences folder and the legacyScreenSaver sandbox
// container, because the sandboxed saver resolves ~/Library to its container.

enum AfterDork {
    static let prefsRelative = "/Library/Preferences/com.gassensmith.afterdork.plist"

    static func allSettings() -> [String: [String: Any]] {
        let path = NSHomeDirectory() + prefsRelative
        guard let data = FileManager.default.contents(atPath: path),
              let plist = try? PropertyListSerialization.propertyList(
                  from: data, options: [], format: nil),
              let dict = plist as? [String: [String: Any]] else { return [:] }
        return dict
    }

    static func value(_ module: String, _ key: String, _ def: Double) -> Double {
        let v = allSettings()[module]?[key]
        if let d = v as? Double { return d }
        if let i = v as? Int { return Double(i) }
        return def
    }

    static func flag(_ module: String, _ key: String, _ def: Bool) -> Bool {
        (allSettings()[module]?[key] as? Bool) ?? def
    }

    static func write(_ settings: [String: [String: Any]]) {
        guard let data = try? PropertyListSerialization.data(
            fromPropertyList: settings, format: .xml, options: 0) else { return }
        let container = "/Library/Containers/com.apple.ScreenSaver.Engine.legacyScreenSaver/Data"
        for path in [NSHomeDirectory() + prefsRelative,
                     NSHomeDirectory() + container + prefsRelative] {
            let dir = (path as NSString).deletingLastPathComponent
            try? FileManager.default.createDirectory(
                atPath: dir, withIntermediateDirectories: true)
            try? data.write(to: URL(fileURLWithPath: path))
        }
    }
}
