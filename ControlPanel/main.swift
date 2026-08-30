import AppKit
import ScreenSaver
import Sparkle

// After Dork 1.0 — the control panel, styled like it fell out of 1996.
// Pick a module from the list, watch it in the preview monitor, fiddle the
// options, and hit "Set Screen Saver" to install it and make it your Mac's
// actual screen saver.

// MARK: - 1996 palette

let face = NSColor(calibratedRed: 0.78, green: 0.78, blue: 0.78, alpha: 1)
let faceLight = NSColor(calibratedWhite: 1.0, alpha: 1)
let faceShadow = NSColor(calibratedWhite: 0.5, alpha: 1)
let faceDark = NSColor(calibratedWhite: 0.2, alpha: 1)
let selectNavy = NSColor(calibratedRed: 0, green: 0, blue: 0.5, alpha: 1)

func bevel(_ ctx: CGContext, _ r: CGRect, sunken: Bool) {
    let tl = (sunken ? faceShadow : faceLight).cgColor
    let tl2 = (sunken ? faceDark : face).cgColor
    let br = (sunken ? faceLight : faceDark).cgColor
    let br2 = (sunken ? face : faceShadow).cgColor
    ctx.setFillColor(tl)
    ctx.fill(CGRect(x: r.minX, y: r.minY, width: r.width, height: 1))
    ctx.fill(CGRect(x: r.minX, y: r.minY, width: 1, height: r.height))
    ctx.setFillColor(br)
    ctx.fill(CGRect(x: r.minX, y: r.maxY - 1, width: r.width, height: 1))
    ctx.fill(CGRect(x: r.maxX - 1, y: r.minY, width: 1, height: r.height))
    ctx.setFillColor(tl2)
    ctx.fill(CGRect(x: r.minX + 1, y: r.minY + 1, width: r.width - 2, height: 1))
    ctx.fill(CGRect(x: r.minX + 1, y: r.minY + 1, width: 1, height: r.height - 2))
    ctx.setFillColor(br2)
    ctx.fill(CGRect(x: r.minX + 1, y: r.maxY - 2, width: r.width - 2, height: 1))
    ctx.fill(CGRect(x: r.maxX - 2, y: r.minY + 1, width: 1, height: r.height - 2))
}

func retroText(_ s: String, at p: CGPoint, bold: Bool = true, size: CGFloat = 12,
               color: NSColor = .black) {
    let attrs: [NSAttributedString.Key: Any] = [
        .font: bold ? NSFont.boldSystemFont(ofSize: size)
                    : NSFont.systemFont(ofSize: size),
        .foregroundColor: color,
    ]
    NSAttributedString(string: s, attributes: attrs).draw(at: p)
}

// MARK: - Retro widgets (flipped coordinates, hand-drawn bevels)

final class RetroButton: NSView {
    var title: String
    var action: () -> Void = {}
    private var pressed = false
    init(frame: NSRect, title: String) {
        self.title = title
        super.init(frame: frame)
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(face.cgColor)
        ctx.fill(bounds)
        bevel(ctx, bounds, sunken: pressed)
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.boldSystemFont(ofSize: 12), .foregroundColor: NSColor.black,
        ]
        let str = NSAttributedString(string: title, attributes: attrs)
        let sz = str.size()
        let off: CGFloat = pressed ? 1 : 0
        str.draw(at: CGPoint(x: (bounds.width - sz.width) / 2 + off,
                             y: (bounds.height - sz.height) / 2 + off))
    }
    override func mouseDown(with event: NSEvent) {
        pressed = true
        needsDisplay = true
    }
    override func mouseUp(with event: NSEvent) {
        pressed = false
        needsDisplay = true
        if bounds.contains(convert(event.locationInWindow, from: nil)) { action() }
    }
}

final class RetroCheckbox: NSView {
    var label: String
    var value: Bool
    var onChange: (Bool) -> Void = { _ in }
    init(frame: NSRect, label: String, value: Bool) {
        self.label = label
        self.value = value
        super.init(frame: frame)
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(face.cgColor)
        ctx.fill(bounds)
        let box = CGRect(x: 0, y: (bounds.height - 14) / 2, width: 14, height: 14)
        ctx.setFillColor(NSColor.white.cgColor)
        ctx.fill(box)
        bevel(ctx, box, sunken: true)
        if value {
            ctx.setStrokeColor(NSColor.black.cgColor)
            ctx.setLineWidth(2)
            ctx.move(to: CGPoint(x: box.minX + 3, y: box.midY))
            ctx.addLine(to: CGPoint(x: box.midX - 1, y: box.maxY - 4))
            ctx.addLine(to: CGPoint(x: box.maxX - 3, y: box.minY + 3))
            ctx.strokePath()
        }
        retroText(label, at: CGPoint(x: 22, y: (bounds.height - 15) / 2), bold: false)
    }
    override func mouseDown(with event: NSEvent) {
        value.toggle()
        needsDisplay = true
        onChange(value)
    }
}

final class RetroSlider: NSView {
    var minV: Double, maxV: Double, value: Double
    var isInt: Bool
    var onLiveChange: (Double) -> Void = { _ in }
    var onCommit: (Double) -> Void = { _ in }
    private(set) var lastEventWasDrag = false
    init(frame: NSRect, minV: Double, maxV: Double, value: Double, isInt: Bool) {
        self.minV = minV; self.maxV = maxV; self.value = value; self.isInt = isInt
        super.init(frame: frame)
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    private var frac: CGFloat { CGFloat((value - minV) / (maxV - minV)) }
    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(face.cgColor)
        ctx.fill(bounds)
        let track = CGRect(x: 4, y: bounds.midY - 2, width: bounds.width - 8, height: 4)
        ctx.setFillColor(faceShadow.cgColor)
        ctx.fill(track)
        bevel(ctx, track.insetBy(dx: -1, dy: -1), sunken: true)
        let x = 4 + (bounds.width - 8 - 12) * frac
        let thumb = CGRect(x: x, y: bounds.midY - 9, width: 12, height: 18)
        ctx.setFillColor(face.cgColor)
        ctx.fill(thumb)
        bevel(ctx, thumb, sunken: false)
    }
    private func setFromEvent(_ event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        let f = max(0, min(1, (p.x - 4) / (bounds.width - 8)))
        var v = minV + Double(f) * (maxV - minV)
        if isInt { v = v.rounded() }
        value = v
        needsDisplay = true
        onLiveChange(v)
    }
    override func mouseDown(with event: NSEvent) {
        lastEventWasDrag = false
        setFromEvent(event)
    }
    override func mouseDragged(with event: NSEvent) {
        lastEventWasDrag = true
        setFromEvent(event)
    }
    override func mouseUp(with event: NSEvent) { onCommit(value) }
}

final class RetroList: NSView {
    var items: [String]
    var selected = 0
    var onSelect: (Int) -> Void = { _ in }
    let rowH: CGFloat = 20
    init(frame: NSRect, items: [String]) {
        self.items = items
        super.init(frame: frame)
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(NSColor.white.cgColor)
        ctx.fill(bounds)
        bevel(ctx, bounds, sunken: true)
        for (i, item) in items.enumerated() {
            let row = CGRect(x: 2, y: 2 + CGFloat(i) * rowH,
                             width: bounds.width - 4, height: rowH)
            if i == selected {
                ctx.setFillColor(selectNavy.cgColor)
                ctx.fill(row)
            }
            retroText(item, at: CGPoint(x: 8, y: row.minY + 3), bold: false,
                      color: i == selected ? .white : .black)
        }
    }
    override func mouseDown(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        let i = Int((p.y - 2) / rowH)
        if i >= 0 && i < items.count && i != selected {
            selected = i
            needsDisplay = true
            onSelect(i)
        }
    }
}

final class RootView: NSView {
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(face.cgColor)
        ctx.fill(bounds)
        // Preview monitor bezel
        bevel(ctx, CGRect(x: 222, y: 14, width: 486, height: 306), sunken: true)
        // Settings group box (etched)
        let g = CGRect(x: 222, y: 334, width: 486, height: 152)
        ctx.setFillColor(faceShadow.cgColor)
        ctx.stroke(g)
        ctx.setStrokeColor(faceShadow.cgColor)
        ctx.setLineWidth(1)
        ctx.stroke(g)
        ctx.setStrokeColor(faceLight.cgColor)
        ctx.stroke(g.offsetBy(dx: 1, dy: 1))
        ctx.setFillColor(face.cgColor)
        ctx.fill(CGRect(x: g.minX + 10, y: g.minY - 8, width: 90, height: 16))
        retroText("Settings", at: CGPoint(x: g.minX + 14, y: g.minY - 9), bold: false)
        // Module list label
        retroText("Module:", at: CGPoint(x: 14, y: 16))
        // Monitor group box (etched): system idle + display sleep
        let m = CGRect(x: 14, y: 214, width: 194, height: 146)
        ctx.setStrokeColor(faceShadow.cgColor)
        ctx.setLineWidth(1)
        ctx.stroke(m)
        ctx.setStrokeColor(faceLight.cgColor)
        ctx.stroke(m.offsetBy(dx: 1, dy: 1))
        ctx.setFillColor(face.cgColor)
        ctx.fill(CGRect(x: m.minX + 10, y: m.minY - 8, width: 66, height: 16))
        retroText("Monitor", at: CGPoint(x: m.minX + 14, y: m.minY - 9), bold: false)
        retroText("Sleep changes need admin OK.",
                  at: CGPoint(x: 22, y: 338), bold: false, size: 10,
                  color: faceShadow)
        // Logo block
        retroText("AFTER", at: CGPoint(x: 16, y: 370), bold: true, size: 30,
                  color: NSColor(calibratedRed: 0.55, green: 0.1, blue: 0.55, alpha: 1))
        retroText("DORK", at: CGPoint(x: 16, y: 402), bold: true, size: 30,
                  color: .black)
        retroText("Chemistry screen savers", at: CGPoint(x: 16, y: 442),
                  bold: false, size: 11)
        let ver = (Bundle.main.infoDictionary?["CFBundleShortVersionString"]
                       as? String) ?? "?"
        retroText("v\(ver) \u{00A9} 1996 Gassensmith Labs", at: CGPoint(x: 16, y: 458),
                  bold: false, size: 11)
        retroText("All molecules biblically accurate.", at: CGPoint(x: 16, y: 474),
                  bold: false, size: 11)
        // Bottom bar divider
        ctx.setFillColor(faceShadow.cgColor)
        ctx.fill(CGRect(x: 8, y: 498, width: bounds.width - 16, height: 1))
        ctx.setFillColor(faceLight.cgColor)
        ctx.fill(CGRect(x: 8, y: 499, width: bounds.width - 16, height: 1))
    }
}

// MARK: - Module catalog

struct OptionSpec {
    enum Kind { case slider(min: Double, max: Double, isInt: Bool), check }
    var key: String
    var label: String
    var kind: Kind
    var def: Double  // for checks: 1 = on
}

struct Module {
    var id: String       // bundle/dir name
    var display: String
    var make: (NSRect, Bool) -> ScreenSaverView
    var options: [OptionSpec]
}

let catalog: [Module] = [
    Module(id: "FlyingFlasks", display: "Flying Flasks",
           make: { FlyingFlasksView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "flock", label: "Flock size",
                          kind: .slider(min: 4, max: 40, isInt: true), def: 15),
               OptionSpec(key: "drips", label: "Drip liquid", kind: .check, def: 1),
           ]),
    Module(id: "GlasswarePipes", display: "Schlenk Pipes",
           make: { GlasswarePipesView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "speed", label: "Growth speed",
                          kind: .slider(min: 0.3, max: 3, isInt: false), def: 1),
               OptionSpec(key: "alembics", label: "Fancy glassware",
                          kind: .check, def: 1),
           ]),
    Module(id: "LatticeMaze", display: "Lattice Maze",
           make: { LatticeMazeView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "speed", label: "Crawl speed",
                          kind: .slider(min: 0.3, max: 3, isInt: false), def: 1),
               OptionSpec(key: "gas", label: "Gas molecules",
                          kind: .slider(min: 0, max: 60, isInt: true), def: 24),
           ]),
    Module(id: "MystifyPolymers", display: "Mystify Origami",
           make: { MystifyPolymersView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "speed", label: "Speed",
                          kind: .slider(min: 0.3, max: 3, isInt: false), def: 1),
               OptionSpec(key: "echo", label: "Echo depth",
                          kind: .slider(min: 4, max: 30, isInt: true), def: 18),
           ]),
    Module(id: "StoddartReef", display: "Stoddart Reef",
           make: { StoddartReefView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "population", label: "Population",
                          kind: .slider(min: 3, max: 20, isInt: true), def: 12),
               OptionSpec(key: "bubbles", label: "Bubbles", kind: .check, def: 1),
           ]),
    Module(id: "OrbitalBox", display: "Orbital Box",
           make: { OrbitalBoxView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "spin", label: "Spin speed",
                          kind: .slider(min: 0.2, max: 3, isInt: false), def: 1),
               OptionSpec(key: "morph", label: "Morph speed",
                          kind: .slider(min: 0.3, max: 3, isInt: false), def: 1),
           ]),
    Module(id: "SmilesRain", display: "SMILES Rain",
           make: { SmilesRainView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "speed", label: "Rain speed",
                          kind: .slider(min: 0.3, max: 3, isInt: false), def: 1),
               OptionSpec(key: "reveals", label: "Reveal names", kind: .check, def: 1),
           ]),
    Module(id: "CastawayChemist", display: "Castaway Chemist",
           make: { CastawayChemistView(frame: $0, isPreview: $1)! },
           options: [
               OptionSpec(key: "chaos", label: "Chaos",
                          kind: .slider(min: 0.3, max: 3, isInt: false), def: 1),
               OptionSpec(key: "quench", label: "NMR quenches", kind: .check, def: 1),
           ]),
]

// MARK: - App assembly

let app = NSApplication.shared
app.setActivationPolicy(.regular)

let winW: CGFloat = 720, winH: CGFloat = 540
let win = NSWindow(contentRect: NSRect(x: 0, y: 0, width: winW, height: winH),
                   styleMask: [.titled, .closable, .miniaturizable],
                   backing: .buffered, defer: false)
win.title = "After Dork "
    + ((Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "")
let root = RootView(frame: NSRect(x: 0, y: 0, width: winW, height: winH))
win.contentView = root

var settings = AfterDork.allSettings()
var currentIdx = 0
var previewView: ScreenSaverView?
var optionViews: [NSView] = []

// List height derives from the catalog so a 9th module can't silently fall
// off the bottom.
let moduleList = RetroList(frame: NSRect(x: 14, y: 36, width: 194,
                                         height: CGFloat(catalog.count) * 20 + 4),
                           items: catalog.map { $0.display })
root.addSubview(moduleList)

// Preview monitor: black screen inside the bezel
let previewHost = NSView(frame: NSRect(x: 226, y: 18, width: 478, height: 298))
previewHost.wantsLayer = true
previewHost.layer?.backgroundColor = NSColor.black.cgColor
root.addSubview(previewHost)

let updaterController = SPUStandardUpdaterController(startingUpdater: true,
                                                     updaterDelegate: nil,
                                                     userDriverDelegate: nil)

let statusLabel = NSTextField(labelWithString: "Welcome to After Dork.")
statusLabel.frame = NSRect(x: 14, y: winH - 532, width: 400, height: 18)
statusLabel.font = NSFont.systemFont(ofSize: 11)
statusLabel.textColor = .black

func optionValue(_ module: Module, _ spec: OptionSpec) -> Double {
    if let v = settings[module.id]?[spec.key] {
        if let d = v as? Double { return d }
        if let i = v as? Int { return Double(i) }
        if let b = v as? Bool { return b ? 1 : 0 }
    }
    return spec.def
}

func storeOption(_ module: Module, _ spec: OptionSpec, _ value: Double) {
    var m = settings[module.id] ?? [:]
    if case .check = spec.kind {
        m[spec.key] = value > 0.5
    } else if case .slider(_, _, let isInt) = spec.kind, isInt {
        m[spec.key] = Int(value)
    } else {
        m[spec.key] = value
    }
    settings[module.id] = m
    AfterDork.write(settings)
}

func recreatePreview() {
    // Stop the old view's animation timer or it keeps simulating (and
    // allocating) forever after removal.
    previewView?.stopAnimation()
    previewView?.removeFromSuperview()
    let v = catalog[currentIdx].make(previewHost.bounds, true)
    previewHost.addSubview(v)
    previewView = v
    v.startAnimation()
}

func rebuildOptions() {
    for v in optionViews { v.removeFromSuperview() }
    optionViews = []
    let module = catalog[currentIdx]
    var y: CGFloat = 352
    for spec in module.options {
        let label = NSTextField(labelWithString: spec.label + ":")
        label.frame = NSRect(x: 238, y: y, width: 130, height: 18)
        label.font = NSFont.systemFont(ofSize: 12)
        label.textColor = .black
        label.drawsBackground = false
        root.addSubview(label)
        optionViews.append(label)
        switch spec.kind {
        case .slider(let mn, let mx, let isInt):
            let valueLabel = NSTextField(labelWithString: "")
            valueLabel.frame = NSRect(x: 620, y: y, width: 70, height: 18)
            valueLabel.font = NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .regular)
            valueLabel.textColor = .black
            func fmt(_ v: Double) -> String { isInt ? "\(Int(v))" : String(format: "%.1fx", v) }
            let slider = RetroSlider(frame: NSRect(x: 380, y: y - 2, width: 226, height: 22),
                                     minV: mn, maxV: mx,
                                     value: optionValue(module, spec), isInt: isInt)
            valueLabel.stringValue = fmt(slider.value)
            slider.onLiveChange = { v in valueLabel.stringValue = fmt(v) }
            slider.onCommit = { v in
                storeOption(module, spec, v)
                recreatePreview()
            }
            root.addSubview(slider)
            root.addSubview(valueLabel)
            optionViews.append(slider)
            optionViews.append(valueLabel)
        case .check:
            let check = RetroCheckbox(frame: NSRect(x: 380, y: y - 2, width: 226, height: 22),
                                      label: "On", value: optionValue(module, spec) > 0.5)
            check.onChange = { v in
                storeOption(module, spec, v ? 1 : 0)
                recreatePreview()
            }
            root.addSubview(check)
            optionViews.append(check)
        }
        y += 34
    }
}

moduleList.onSelect = { i in
    currentIdx = i
    rebuildOptions()
    recreatePreview()
    statusLabel.stringValue = "\(catalog[i].display) selected."
}

// Bottom bar
let setButton = RetroButton(frame: NSRect(x: winW - 178, y: 508, width: 164, height: 26),
                            title: "Set Screen Saver")
let demoButton = RetroButton(frame: NSRect(x: winW - 330, y: 508, width: 68, height: 26),
                             title: "Demo")
let quitButton = RetroButton(frame: NSRect(x: winW - 254, y: 508, width: 68, height: 26),
                             title: "Quit")
let updatesButton = RetroButton(frame: NSRect(x: winW - 448, y: 508, width: 110, height: 26),
                                title: "Updates\u{2026}")
statusLabel.frame = NSRect(x: 14, y: 512, width: 252, height: 18)
root.addSubview(setButton)
root.addSubview(demoButton)
root.addSubview(quitButton)
root.addSubview(updatesButton)
root.addSubview(statusLabel)

updatesButton.action = { updaterController.checkForUpdates(nil) }

quitButton.action = { app.terminate(nil) }

// MARK: - Monitor group: screen saver idle time + display sleep

/// Screen saver activation delay in seconds (0 = never). Same per-host prefs
/// domain "Set Screen Saver" already writes; missing key means the macOS
/// default of 20 minutes.
func readIdleSeconds() -> Int {
    let v = CFPreferencesCopyValue("idleTime" as CFString,
                                   "com.apple.screensaver" as CFString,
                                   kCFPreferencesCurrentUser, kCFPreferencesCurrentHost)
    return (v as? NSNumber)?.intValue ?? 1200
}

func writeIdleSeconds(_ seconds: Int) {
    CFPreferencesSetValue("idleTime" as CFString, seconds as CFNumber,
                          "com.apple.screensaver" as CFString,
                          kCFPreferencesCurrentUser, kCFPreferencesCurrentHost)
    CFPreferencesSynchronize("com.apple.screensaver" as CFString,
                             kCFPreferencesCurrentUser, kCFPreferencesCurrentHost)
}

/// Display sleep in minutes for the active power source (0 = never), read
/// without privileges from `pmset -g`.
func readDisplaySleepMinutes() -> Int? {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/usr/bin/pmset")
    p.arguments = ["-g"]
    let pipe = Pipe()
    p.standardOutput = pipe
    do { try p.run() } catch { return nil }
    p.waitUntilExit()
    let out = String(data: pipe.fileHandleForReading.readDataToEndOfFile(),
                     encoding: .utf8) ?? ""
    for line in out.split(separator: "\n") {
        let parts = line.split(separator: " ", omittingEmptySubsequences: true)
        if parts.count >= 2, parts[0] == "displaysleep" { return Int(parts[1]) }
    }
    return nil
}

/// Setting display sleep is root-only (`pmset -a`), so this is the one place
/// the app may put up an admin prompt — and only when the user drags the
/// sleep slider. Returns false if they cancel.
func setDisplaySleepMinutes(_ minutes: Int) -> Bool {
    let src = "do shell script \"pmset -a displaysleep \(minutes)\""
        + " with administrator privileges"
    var err: NSDictionary?
    NSAppleScript(source: src)?.executeAndReturnError(&err)
    return err == nil
}

func fmtMinutes(_ m: Int) -> String { m == 0 ? "Never" : "\(m) min" }

/// Backlight brightness (0-1) via the private DisplayServices framework —
/// the nriley/brightness trick. No public API reaches the backlight on
/// Apple Silicon, so we dlopen the framework out of the shared cache; if a
/// symbol is missing on some future macOS this degrades to "n/a".
private typealias DSGetBrightness =
    @convention(c) (UInt32, UnsafeMutablePointer<Float>) -> Int32
private typealias DSSetBrightness = @convention(c) (UInt32, Float) -> Int32

let displayServices = dlopen(
    "/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices",
    RTLD_NOW)
// Symbols resolved once — not per drag event.
private let dsGetBrightness: DSGetBrightness? = displayServices
    .flatMap { dlsym($0, "DisplayServicesGetBrightness") }
    .map { unsafeBitCast($0, to: DSGetBrightness.self) }
private let dsSetBrightness: DSSetBrightness? = displayServices
    .flatMap { dlsym($0, "DisplayServicesSetBrightness") }
    .map { unsafeBitCast($0, to: DSSetBrightness.self) }

func readBrightness() -> Float? {
    guard let get = dsGetBrightness else { return nil }
    var b: Float = 0
    return get(CGMainDisplayID(), &b) == 0 ? b : nil
}

@discardableResult
func setBrightness(_ v: Float) -> Bool {
    guard let set = dsSetBrightness else { return false }
    return set(CGMainDisplayID(), v) == 0
}

func monitorLabel(_ text: String, x: CGFloat, y: CGFloat, w: CGFloat,
                  right: Bool = false) -> NSTextField {
    let l = NSTextField(labelWithString: text)
    l.frame = NSRect(x: x, y: y, width: w, height: 16)
    l.font = NSFont.systemFont(ofSize: 11)
    l.textColor = .black
    l.alignment = right ? .right : .left
    root.addSubview(l)
    return l
}

var displaySleepNow = readDisplaySleepMinutes() ?? 0
let idleMinutes = readIdleSeconds() / 60
// Row pitch is 38pt: 16pt label + 20pt slider with no overlap, so labels
// never sit on the sliders' hit areas.
_ = monitorLabel("Start saver after:", x: 22, y: 220, w: 110)
let saverValue = monitorLabel(fmtMinutes(idleMinutes), x: 132, y: 220, w: 70,
                              right: true)
let saverSlider = RetroSlider(frame: NSRect(x: 22, y: 238, width: 178, height: 20),
                              minV: 0, maxV: Double(max(60, idleMinutes)),
                              value: Double(idleMinutes), isInt: true)
saverSlider.onLiveChange = { v in saverValue.stringValue = fmtMinutes(Int(v)) }
saverSlider.onCommit = { v in
    writeIdleSeconds(Int(v) * 60)
    if Int(v) != 0, displaySleepNow != 0, displaySleepNow <= Int(v) {
        statusLabel.stringValue =
            "Careful: display sleeps first (\(fmtMinutes(displaySleepNow)))."
    } else {
        statusLabel.stringValue = Int(v) == 0
            ? "Screen saver will never start."
            : "Screen saver starts after \(fmtMinutes(Int(v)))."
    }
}
root.addSubview(saverSlider)

_ = monitorLabel("Display sleep after:", x: 22, y: 258, w: 110)
let sleepValue = monitorLabel(fmtMinutes(displaySleepNow), x: 132, y: 258, w: 70,
                              right: true)
let sleepSlider = RetroSlider(frame: NSRect(x: 22, y: 276, width: 178, height: 20),
                              minV: 0, maxV: Double(max(60, displaySleepNow)),
                              value: Double(displaySleepNow), isInt: true)
sleepSlider.onLiveChange = { v in sleepValue.stringValue = fmtMinutes(Int(v)) }
sleepSlider.onCommit = { v in
    let m = Int(v)
    if m == displaySleepNow { return }
    if setDisplaySleepMinutes(m) {
        displaySleepNow = m
        statusLabel.stringValue = m == 0
            ? "Display will never sleep."
            : "Display sleeps after \(fmtMinutes(m)) (all power sources)."
    } else {
        sleepSlider.value = Double(displaySleepNow)
        sleepSlider.needsDisplay = true
        sleepValue.stringValue = fmtMinutes(displaySleepNow)
        statusLabel.stringValue = "Display sleep unchanged."
    }
}
root.addSubview(sleepSlider)

_ = monitorLabel("Brightness:", x: 22, y: 296, w: 110)
let brightValue = monitorLabel("n/a", x: 132, y: 296, w: 70, right: true)
if let b0 = readBrightness() {
    func fmtBrightness(_ v: Double) -> String {
        Int(v) == 0 ? "Off" : "\(Int(v))%"
    }
    brightValue.stringValue = fmtBrightness(Double((b0 * 100).rounded()))
    let brightSlider = RetroSlider(
        frame: NSRect(x: 22, y: 314, width: 178, height: 20),
        minV: 0, maxV: 100, value: Double((b0 * 100).rounded()), isInt: true)
    // Tracks the drag live like the hardware brightness keys, but the live
    // path floors at 2% so a mid-drag (or misaimed click) can never black
    // the screen; only a deliberate DRAG released at 0 kills the backlight.
    brightSlider.onLiveChange = { v in
        setBrightness(max(Float(v), 2) / 100)
        brightValue.stringValue = fmtBrightness(v)
    }
    brightSlider.onCommit = { v in
        if Int(v) == 0 && !brightSlider.lastEventWasDrag {
            // A stray click at the left edge is not consent for darkness.
            brightSlider.value = 2
            brightSlider.needsDisplay = true
            setBrightness(0.02)
            brightValue.stringValue = fmtBrightness(2)
            statusLabel.stringValue = "Drag to 0 to switch the backlight off."
            return
        }
        setBrightness(Float(v) / 100)
        statusLabel.stringValue = Int(v) == 0
            ? "Backlight off. The brightness key brings it back."
            : "Brightness \(Int(v))%."
    }
    root.addSubview(brightSlider)
}

/// Tahoe's idle activation ignores the legacy moduleDict and reads the
/// Wallpaper store instead, so "Set Screen Saver" must rewrite the store's
/// Idle entries to a screen-saver provider pointing at our module. The
/// agents get SIGKILL first so they cannot flush stale state back over the
/// edit; launchd respawns them and they read the fresh file.
func setWallpaperStoreIdle(moduleURL: URL) {
    let storePath = NSHomeDirectory()
        + "/Library/Application Support/com.apple.wallpaper/Store/Index.plist"
    let kill = Process()
    kill.executableURL = URL(fileURLWithPath: "/bin/zsh")
    kill.arguments = ["-c",
        "pkill -9 WallpaperAgent 2>/dev/null; pkill -9 legacyScreenSaver 2>/dev/null; true"]
    try? kill.run()
    kill.waitUntilExit()

    guard let cfg = try? PropertyListSerialization.data(
        fromPropertyList: ["module": ["relative": moduleURL.absoluteString]],
        format: .binary, options: 0),
        let idleOpts = try? PropertyListSerialization.data(
            fromPropertyList: ["values": [String: Any]()],
            format: .binary, options: 0) else { return }
    let idleChoice: [String: Any] = [
        "Configuration": cfg, "Files": [Any](),
        "Provider": "com.apple.wallpaper.choice.screen-saver",
    ]
    let now = Date()

    var root: [String: Any]
    if let data = FileManager.default.contents(atPath: storePath),
       let parsed = (try? PropertyListSerialization.propertyList(
           from: data, options: [], format: nil)) as? [String: Any] {
        root = parsed
    } else {
        root = ["AllSpacesAndDisplays": [String: Any](),
                "Displays": [String: Any](), "Spaces": [String: Any](),
                "SystemDefault": [String: Any]()]
    }

    func patched(_ scopeIn: [String: Any]) -> [String: Any] {
        var scope = scopeIn
        scope["Type"] = "individual"
        // Keep an existing Desktop choice; otherwise fall back to the default
        // wallpaper provider so converting from "linked" keeps the wallpaper.
        if scope["Desktop"] == nil {
            let desktopChoice: [String: Any] = [
                "Configuration": Data(), "Files": [Any](), "Provider": "default",
            ]
            scope["Desktop"] = [
                "Content": ["Choices": [desktopChoice],
                            "EncodedOptionValues": "$null", "Shuffle": "$null"],
                "LastSet": now, "LastUse": now,
            ]
        }
        scope["Idle"] = [
            "Content": ["Choices": [idleChoice],
                        "EncodedOptionValues": idleOpts, "Shuffle": "$null"],
            "LastSet": now, "LastUse": now,
        ]
        scope.removeValue(forKey: "Linked")
        return scope
    }

    for key in ["AllSpacesAndDisplays", "SystemDefault"] {
        root[key] = patched(root[key] as? [String: Any] ?? [:])
    }
    // Per-display / per-space overrides would shadow our setting: patch them too.
    for group in ["Displays", "Spaces"] {
        if var g = root[group] as? [String: Any] {
            for k in g.keys {
                if let scope = g[k] as? [String: Any] { g[k] = patched(scope) }
            }
            root[group] = g
        }
    }

    if let out = try? PropertyListSerialization.data(
        fromPropertyList: root, format: .binary, options: 0) {
        try? out.write(to: URL(fileURLWithPath: storePath))
    }
}

/// Installs the module's .saver bundle, writes the shared settings, and
/// selects it in the legacy screen saver defaults (what ScreenSaverEngine
/// reads). Returns the installed path.
@discardableResult
func installAndSelectLegacy(_ module: Module) -> String {
    let fm = FileManager.default
    let destDir = NSHomeDirectory() + "/Library/Screen Savers"
    let dest = destDir + "/\(module.id).saver"
    if let src = Bundle.main.resourceURL?
        .appendingPathComponent("Savers/\(module.id).saver").path,
        fm.fileExists(atPath: src) {
        try? fm.createDirectory(atPath: destDir, withIntermediateDirectories: true)
        try? fm.removeItem(atPath: dest)
        try? fm.copyItem(atPath: src, toPath: dest)
    }
    AfterDork.write(settings)
    let dict: [String: Any] = ["moduleName": module.id, "path": dest, "type": 0]
    CFPreferencesSetValue("moduleDict" as CFString, dict as CFDictionary,
                          "com.apple.screensaver" as CFString,
                          kCFPreferencesCurrentUser, kCFPreferencesCurrentHost)
    CFPreferencesSynchronize("com.apple.screensaver" as CFString,
                             kCFPreferencesCurrentUser, kCFPreferencesCurrentHost)
    return dest
}

setButton.action = {
    let module = catalog[currentIdx]
    let dest = installAndSelectLegacy(module)
    // Also select it in the Wallpaper store (the path Tahoe's idle honors).
    setWallpaperStoreIdle(moduleURL: URL(fileURLWithPath: dest))
    NSSound.beep()
    statusLabel.stringValue = "\u{2713} \(module.display) is now your screen saver."
}

// Demo runs the module full-screen from inside this app — the After Dark
// way — rather than trusting ScreenSaverEngine, which on Tahoe consults the
// wallpaper store and happily ignores the module we just selected.
final class DemoWindow: NSWindow {
    override var canBecomeKey: Bool { true }
}

var demoWindows: [NSWindow] = []
var demoViews: [ScreenSaverView] = []
var demoMonitor: Any?

func endDemo() {
    if let m = demoMonitor {
        NSEvent.removeMonitor(m)
        demoMonitor = nil
    }
    for v in demoViews { v.stopAnimation() }
    demoViews = []
    for w in demoWindows { w.orderOut(nil) }
    demoWindows = []
    NSCursor.unhide()
    statusLabel.stringValue = "Demo ended."
}

func startDemo(_ module: Module) {
    AfterDork.write(settings)  // the demo honors the current option values
    for screen in NSScreen.screens {
        let w = DemoWindow(contentRect: screen.frame, styleMask: .borderless,
                           backing: .buffered, defer: false)
        w.level = .screenSaver
        w.backgroundColor = .black
        w.isReleasedWhenClosed = false
        w.acceptsMouseMovedEvents = true
        let v = module.make(NSRect(origin: .zero, size: screen.frame.size), false)
        w.contentView = v
        v.startAnimation()
        w.makeKeyAndOrderFront(nil)
        demoWindows.append(w)
        demoViews.append(v)
    }
    NSCursor.hide()
    let started = Date()
    demoMonitor = NSEvent.addLocalMonitorForEvents(
        matching: [.mouseMoved, .keyDown, .leftMouseDown, .rightMouseDown,
                   .scrollWheel]) { ev in
        if Date().timeIntervalSince(started) > 0.7 {
            endDemo()
            return nil
        }
        return ev
    }
}

demoButton.action = {
    let module = catalog[currentIdx]
    statusLabel.stringValue = "Demonstrating \(module.display)\u{2026}"
    startDemo(module)
}

rebuildOptions()
recreatePreview()
win.center()
win.makeKeyAndOrderFront(nil)
app.activate(ignoringOtherApps: true)

let timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { _ in
    previewView?.animateOneFrame()
    for v in demoViews { v.animateOneFrame() }
}
RunLoop.main.add(timer, forMode: .common)

NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification,
                                       object: win, queue: nil) { _ in
    app.terminate(nil)
}

// Hidden flag: render the window contents to a PNG for visual verification.
if let idx = CommandLine.arguments.firstIndex(of: "--snapshot"),
   CommandLine.arguments.count > idx + 1 {
    for _ in 0..<45 { previewView?.animateOneFrame() }
    if let rep = root.bitmapImageRepForCachingDisplay(in: root.bounds) {
        root.cacheDisplay(in: root.bounds, to: rep)
        if let png = rep.representation(using: .png, properties: [:]) {
            try? png.write(to: URL(fileURLWithPath: CommandLine.arguments[idx + 1]))
        }
    }
    exit(0)
}

app.run()
