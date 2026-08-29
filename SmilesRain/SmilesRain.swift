import ScreenSaver

// SMILES Rain — the Matrix digital rain, except the falling glyphs are SMILES
// strings of famous molecules. Read a column top to bottom and you can name
// the compound; every so often the saver names one for you.

private func rnd(_ r: ClosedRange<CGFloat>) -> CGFloat { CGFloat.random(in: r) }

private let molecules: [(name: String, smiles: String)] = [
    ("CAFFEINE", "Cn1cnc2c1c(=O)n(C)c(=O)n2C"),
    ("ASPIRIN", "CC(=O)Oc1ccccc1C(=O)O"),
    ("PARACETAMOL", "CC(=O)Nc1ccc(O)cc1"),
    ("IBUPROFEN", "CC(C)Cc1ccc(cc1)C(C)C(=O)O"),
    ("DOPAMINE", "NCCc1ccc(O)c(O)c1"),
    ("SEROTONIN", "NCCc1c[nH]c2ccc(O)cc12"),
    ("ADRENALINE", "CNCC(O)c1ccc(O)c(O)c1"),
    ("NICOTINE", "CN1CCCC1c1cccnc1"),
    ("VANILLIN", "COc1cc(C=O)ccc1O"),
    ("CAPSAICIN", "COc1cc(CNC(=O)CCCCC=CC(C)C)ccc1O"),
    ("GLUCOSE", "OCC1OC(O)C(O)C(O)C1O"),
    ("TNT", "Cc1c([N+](=O)[O-])cc([N+](=O)[O-])cc1[N+](=O)[O-]"),
    ("CUBANE", "C12C3C4C1C5C2C3C45"),
    ("PENICILLIN G", "CC1(C)SC2C(NC(=O)Cc3ccccc3)C(=O)N2C1C(=O)O"),
    ("CITRIC ACID", "OC(=O)CC(O)(C(=O)O)CC(=O)O"),
    ("UREA", "NC(N)=O"),
    ("BENZENE", "c1ccccc1"),
    ("ETHANOL", "CCO"),
    ("ACETONE", "CC(C)=O"),
    ("MENTHOL", "CC(C)C1CCC(C)CC1O"),
]

private struct Drop {
    var head: CGFloat
    var speed: CGFloat
    var mol: Int
    var offset: Int
    var trail: Int
}

private struct Reveal {
    var text: String
    var x: CGFloat, y: CGFloat
    var age: Int
}

@objc(SmilesRainView)
public final class SmilesRainView: ScreenSaverView {

    private let cellW: CGFloat = 14, cellH: CGFloat = 17
    private var cols = 0, rows = 0
    private var drops: [Drop] = []
    private var glyphs: [Character: [CGImage]] = [:]  // 9 brightness levels
    private var reveal: Reveal?
    private var revealTimer = 0
    private var tick = 0

    public override init?(frame: NSRect, isPreview: Bool) {
        super.init(frame: frame, isPreview: isPreview)
        animationTimeInterval = 1.0 / 30.0
        setup()
    }

    public required init?(coder: NSCoder) {
        super.init(coder: coder)
        animationTimeInterval = 1.0 / 30.0
        setup()
    }

    private func setup() {
        let w = max(bounds.width, 640), h = max(bounds.height, 400)
        cols = Int(w / cellW)
        rows = Int(h / cellH) + 2
        drops = (0..<cols).map { _ in makeDrop(scattered: true) }
        buildGlyphs()
    }

    private func makeDrop(scattered: Bool) -> Drop {
        Drop(head: scattered ? rnd(-30...CGFloat(rows)) : -rnd(0...40),
             speed: rnd(0.28...0.85),
             mol: Int.random(in: 0..<molecules.count),
             offset: Int.random(in: 0..<64),
             trail: Int.random(in: 13...30))
    }

    private func buildGlyphs() {
        var charset = Set<Character>()
        for m in molecules { for c in m.smiles { charset.insert(c) } }
        let font = NSFont(name: "Menlo-Bold", size: 15)
            ?? NSFont.monospacedSystemFont(ofSize: 15, weight: .bold)
        let gw = Int(cellW), gh = Int(cellH)
        for ch in charset {
            var levels: [CGImage] = []
            for level in 0...8 {
                let t = CGFloat(min(level, 7)) / 7
                let color: NSColor = level == 8
                    ? NSColor(calibratedRed: 0.82, green: 1.0, blue: 0.86, alpha: 1)
                    : NSColor(calibratedRed: 0.30 * t * t,
                              green: 0.16 + 0.84 * t,
                              blue: 0.10 + 0.25 * t * t, alpha: 1)
                let ctx = CGContext(data: nil, width: gw, height: gh,
                                    bitsPerComponent: 8, bytesPerRow: gw * 4,
                                    space: CGColorSpaceCreateDeviceRGB(),
                                    bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue
                                        | CGBitmapInfo.byteOrder32Little.rawValue)!
                let ns = NSGraphicsContext(cgContext: ctx, flipped: false)
                NSGraphicsContext.saveGraphicsState()
                NSGraphicsContext.current = ns
                let str = NSAttributedString(string: String(ch), attributes: [
                    .font: font, .foregroundColor: color,
                ])
                let sz = str.size()
                str.draw(at: CGPoint(x: (CGFloat(gw) - sz.width) / 2, y: 1))
                NSGraphicsContext.restoreGraphicsState()
                levels.append(ctx.makeImage()!)
            }
            glyphs[ch] = levels
        }
    }

    private func charAt(_ drop: Drop, row: Int) -> Character? {
        let s = Array(molecules[drop.mol].smiles)
        let n = s.count + 3  // gap between repeats
        let idx = ((row + drop.offset) % n + n) % n
        return idx < s.count ? s[idx] : nil
    }

    public override func animateOneFrame() {
        tick += 1
        for i in drops.indices {
            drops[i].head += drops[i].speed
            if drops[i].head - CGFloat(drops[i].trail) > CGFloat(rows) {
                drops[i] = makeDrop(scattered: false)
            }
        }
        revealTimer += 1
        if reveal == nil && revealTimer > 240 {
            revealTimer = 0
            let m = molecules.randomElement()!
            reveal = Reveal(text: "\(m.name)  \u{2261}  \(m.smiles)",
                            x: rnd(60...max(bounds.width - 460, 120)),
                            y: rnd(80...max(bounds.height - 120, 160)),
                            age: 0)
        }
        if reveal != nil {
            reveal!.age += 1
            if reveal!.age > 150 { reveal = nil }
        }
        needsDisplay = true
    }

    public override func draw(_ rect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(CGColor(red: 0, green: 0.012, blue: 0, alpha: 1))
        ctx.fill(bounds)
        let h = bounds.height
        for (c, drop) in drops.enumerated() {
            let headRow = Int(drop.head.rounded(.down))
            for k in 0..<drop.trail {
                let row = headRow - k
                if row < 0 || row >= rows { continue }
                guard let ch = charAt(drop, row: row),
                      let levels = glyphs[ch] else { continue }
                let level = k == 0 ? 8 : max(0, 7 - (k * 8) / drop.trail)
                let x = CGFloat(c) * cellW
                let y = h - CGFloat(row + 1) * cellH
                ctx.draw(levels[level], in: CGRect(x: x, y: y,
                                                   width: cellW, height: cellH))
            }
        }
        if let r = reveal {
            let alpha = 0.9 * sin(.pi * CGFloat(r.age) / 150)
            let attrs: [NSAttributedString.Key: Any] = [
                .font: NSFont(name: "Menlo-Bold", size: 19)
                    ?? NSFont.monospacedSystemFont(ofSize: 19, weight: .bold),
                .foregroundColor: NSColor(calibratedRed: 0.62, green: 1.0,
                                          blue: 0.68, alpha: alpha),
            ]
            NSAttributedString(string: r.text, attributes: attrs)
                .draw(at: CGPoint(x: r.x, y: r.y))
        }
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { SmilesRainView(frame: $0, isPreview: false)! }
#endif
