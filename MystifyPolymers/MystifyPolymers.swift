import ScreenSaver

// Mystify Polymers — the Windows "Mystify" bouncing-bezier saver reimagined as
// nucleic acids changing conformation: two DNA double helices (crossing
// backbones, color-coded base-pair rungs, 5'/3' ends) and a single-stranded
// RNA with lettered bases, plus fading ghosts of past conformations and a
// benzene ring bouncing DVD-logo style.

private func rnd(_ r: ClosedRange<CGFloat>) -> CGFloat { CGFloat.random(in: r) }

private struct Bouncer {
    var p: CGPoint
    var v: CGVector
    mutating func step(in r: CGRect) {
        p.x += v.dx
        p.y += v.dy
        if p.x < r.minX { p.x = r.minX; v.dx = abs(v.dx) }
        if p.x > r.maxX { p.x = r.maxX; v.dx = -abs(v.dx) }
        if p.y < r.minY { p.y = r.minY; v.dy = abs(v.dy) }
        if p.y > r.maxY { p.y = r.maxY; v.dy = -abs(v.dy) }
    }
}

private struct Chain {
    var points: [Bouncer]
    var history: [[CGPoint]] = []
    var hue: CGFloat
    var style: Int  // 0, 2 = DNA duplex; 1 = RNA single strand
    var recordCounter = 0
}

@objc(MystifyPolymersView)
public final class MystifyPolymersView: ScreenSaverView {

    private var chains: [Chain] = []
    private var benzene = Bouncer(p: .zero, v: .zero)
    private var benzeneTrail: [CGPoint] = []
    private var benzeneHue: CGFloat = 0.55
    private var benzeneRot: CGFloat = 0
    private var t: CGFloat = 0
    private let historyLen = 14

    // A green, T red, G blue, C yellow; U purple stands in for T in RNA.
    private let baseColors: [NSColor] = [
        NSColor(calibratedRed: 0.30, green: 0.85, blue: 0.42, alpha: 1),
        NSColor(calibratedRed: 0.95, green: 0.38, blue: 0.30, alpha: 1),
        NSColor(calibratedRed: 0.36, green: 0.56, blue: 0.98, alpha: 1),
        NSColor(calibratedRed: 0.95, green: 0.80, blue: 0.25, alpha: 1),
    ]
    private let uColor = NSColor(calibratedRed: 0.75, green: 0.45, blue: 0.95, alpha: 1)

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
        let r = bounds.insetBy(dx: 10, dy: 10)
        let w = max(r.width, 600), h = max(r.height, 400)
        let speed: CGFloat = isPreview ? 2.0 : 4.2
        chains = (0..<3).map { i in
            let pts = (0..<6).map { _ -> Bouncer in
                var b = Bouncer(p: CGPoint(x: rnd(10...w), y: rnd(10...h)), v: .zero)
                let a = rnd(0...(2 * .pi))
                let s = rnd(0.55...1.0) * speed
                b.v = CGVector(dx: cos(a) * s, dy: sin(a) * s)
                return b
            }
            return Chain(points: pts, hue: CGFloat(i) / 3 + rnd(0...0.08), style: i)
        }
        benzene = Bouncer(p: CGPoint(x: w / 2, y: h / 2),
                          v: CGVector(dx: speed * 0.7, dy: speed * 0.55))
    }

    public override func animateOneFrame() {
        t += 1.0 / 30.0
        let r = bounds.insetBy(dx: 8, dy: 8)
        for c in chains.indices {
            for i in chains[c].points.indices { chains[c].points[i].step(in: r) }
            chains[c].recordCounter += 1
            if chains[c].recordCounter >= 2 {
                chains[c].recordCounter = 0
                chains[c].history.append(chains[c].points.map { $0.p })
                if chains[c].history.count > historyLen { chains[c].history.removeFirst() }
            }
        }
        let ring = bounds.insetBy(dx: 42, dy: 42)
        let before = benzene.v
        benzene.step(in: ring)
        if before.dx != benzene.v.dx || before.dy != benzene.v.dy {
            benzeneHue = rnd(0...1)  // wall hit: new color, DVD-logo style
        }
        benzeneRot += 0.02
        benzeneTrail.append(benzene.p)
        if benzeneTrail.count > 7 { benzeneTrail.removeFirst() }
        needsDisplay = true
    }

    public override func draw(_ rect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 1))
        ctx.fill(bounds)
        ctx.setLineJoin(.round)
        ctx.setLineCap(.round)
        for c in chains { drawChain(ctx, c) }
        drawBenzene(ctx)
    }

    // MARK: - Curve helpers

    private func addSmoothPath(_ ctx: CGContext, _ pts: [CGPoint]) {
        guard pts.count > 2 else { return }
        ctx.move(to: pts[0])
        for i in 0..<(pts.count - 1) {
            let p0 = pts[max(i - 1, 0)]
            let p1 = pts[i]
            let p2 = pts[i + 1]
            let p3 = pts[min(i + 2, pts.count - 1)]
            let c1 = CGPoint(x: p1.x + (p2.x - p0.x) / 6, y: p1.y + (p2.y - p0.y) / 6)
            let c2 = CGPoint(x: p2.x - (p3.x - p1.x) / 6, y: p2.y - (p3.y - p1.y) / 6)
            ctx.addCurve(to: p2, control1: c1, control2: c2)
        }
    }

    private func sampleCurve(_ pts: [CGPoint], per: Int) -> [CGPoint] {
        guard pts.count > 2 else { return pts }
        var out: [CGPoint] = []
        for i in 0..<(pts.count - 1) {
            let p0 = pts[max(i - 1, 0)]
            let p1 = pts[i]
            let p2 = pts[i + 1]
            let p3 = pts[min(i + 2, pts.count - 1)]
            let c1 = CGPoint(x: p1.x + (p2.x - p0.x) / 6, y: p1.y + (p2.y - p0.y) / 6)
            let c2 = CGPoint(x: p2.x - (p3.x - p1.x) / 6, y: p2.y - (p3.y - p1.y) / 6)
            for s in 0..<per {
                let u = CGFloat(s) / CGFloat(per)
                let mu = 1 - u
                let x = mu*mu*mu*p1.x + 3*mu*mu*u*c1.x + 3*mu*u*u*c2.x + u*u*u*p2.x
                let y = mu*mu*mu*p1.y + 3*mu*mu*u*c1.y + 3*mu*u*u*c2.y + u*u*u*p2.y
                out.append(CGPoint(x: x, y: y))
            }
        }
        out.append(pts[pts.count - 1])
        return out
    }

    /// Even arc-length samples along the smooth curve, with unit tangents.
    private func resample(_ pts: [CGPoint], spacing: CGFloat) -> [(p: CGPoint, t: CGVector)] {
        let dense = sampleCurve(pts, per: 14)
        guard dense.count > 2 else { return [] }
        var positions: [CGPoint] = [dense[0]]
        var acc: CGFloat = 0
        var prev = dense[0]
        for q in dense.dropFirst() {
            let d = hypot(q.x - prev.x, q.y - prev.y)
            acc += d
            if acc >= spacing {
                acc = 0
                positions.append(q)
            }
            prev = q
        }
        var out: [(CGPoint, CGVector)] = []
        for (i, p) in positions.enumerated() {
            let a = positions[max(i - 1, 0)]
            let b = positions[min(i + 1, positions.count - 1)]
            let len = max(hypot(b.x - a.x, b.y - a.y), 0.001)
            out.append((p, CGVector(dx: (b.x - a.x) / len, dy: (b.y - a.y) / len)))
        }
        return out
    }

    // MARK: - Labels

    private func drawLabel(_ s: String, at p: CGPoint, color: NSColor, size: CGFloat) {
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.boldSystemFont(ofSize: size),
            .foregroundColor: color,
        ]
        let str = NSAttributedString(string: s, attributes: attrs)
        let sz = str.size()
        if let ctx = NSGraphicsContext.current?.cgContext {
            ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 1))
            ctx.fillEllipse(in: CGRect(x: p.x - sz.width / 2 - 1.5,
                                       y: p.y - sz.height / 2 - 0.5,
                                       width: sz.width + 3, height: sz.height + 1))
        }
        str.draw(at: CGPoint(x: p.x - sz.width / 2, y: p.y - sz.height / 2))
    }

    // MARK: - Chains

    private func drawChain(_ ctx: CGContext, _ c: Chain) {
        let n = c.history.count
        guard n > 0 else { return }
        // Ghost conformations
        for (i, snapshot) in c.history.enumerated() where i < n - 1 {
            let age = CGFloat(i + 1) / CGFloat(n)
            let hue = (c.hue + t * 0.02 + CGFloat(i) * 0.012)
                .truncatingRemainder(dividingBy: 1)
            let color = NSColor(calibratedHue: hue < 0 ? hue + 1 : hue,
                                saturation: 0.85,
                                brightness: 0.55 + 0.35 * age,
                                alpha: 0.08 + 0.30 * age)
            ctx.setStrokeColor(color.cgColor)
            ctx.setLineWidth(1.3)
            addSmoothPath(ctx, snapshot)
            ctx.strokePath()
        }
        // Current conformation as a nucleic acid
        let hue = (c.hue + t * 0.02 + CGFloat(n - 1) * 0.012).truncatingRemainder(dividingBy: 1)
        let color = NSColor(calibratedHue: hue < 0 ? hue + 1 : hue,
                            saturation: 0.55, brightness: 1.0, alpha: 0.95)
        if c.style == 1 {
            drawRNA(ctx, c.history[n - 1], color: color, seed: c.style * 5 + 3)
        } else {
            drawDNA(ctx, c.history[n - 1], color: color, seed: c.style * 5 + 1)
        }
    }

    // DNA duplex: two backbones weaving around the guide curve in antiphase,
    // base-pair rungs where the groove opens, complementary colors meeting at
    // the middle of each rung.
    private func drawDNA(_ ctx: CGContext, _ snapshot: [CGPoint], color: NSColor,
                         seed: Int) {
        let spacing: CGFloat = 5
        let samples = resample(snapshot, spacing: spacing)
        guard samples.count > 12 else { return }
        let period: CGFloat = 66
        let amp: CGFloat = 13

        func phase(_ i: Int) -> CGFloat {
            CGFloat(i) * spacing / period * 2 * .pi
        }
        func strandPoint(_ i: Int, _ sign: CGFloat) -> CGPoint {
            let s = samples[i]
            let off = amp * sin(phase(i)) * sign
            return CGPoint(x: s.p.x - s.t.dy * off, y: s.p.y + s.t.dx * off)
        }

        // Base-pair rungs, drawn behind the backbones
        ctx.setLineWidth(2.2)
        for i in stride(from: 2, to: samples.count - 2, by: 3) {
            guard abs(sin(phase(i))) > 0.4 else { continue }
            let a = strandPoint(i, 1), b = strandPoint(i, -1)
            let mid = CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2)
            let bi = (i / 3 * 7 + seed) % 4
            let pair = [(0, 1), (1, 0), (2, 3), (3, 2)][bi]
            ctx.setStrokeColor(baseColors[pair.0].cgColor)
            ctx.move(to: a); ctx.addLine(to: mid); ctx.strokePath()
            ctx.setStrokeColor(baseColors[pair.1].cgColor)
            ctx.move(to: mid); ctx.addLine(to: b); ctx.strokePath()
        }

        // Backbones, brightness-modulated so each strand reads front/back
        guard let rgb = color.usingColorSpace(.deviceRGB) else { return }
        let cr = rgb.redComponent, cg = rgb.greenComponent, cb = rgb.blueComponent
        for sign: CGFloat in [1, -1] {
            var i = 0
            while i < samples.count - 1 {
                let end = min(i + 4, samples.count - 1)
                let depth = cos(phase((i + end) / 2)) * sign  // +1 = front strand
                let bright = 0.55 + 0.45 * (depth + 1) / 2
                ctx.setStrokeColor(CGColor(red: cr * bright, green: cg * bright,
                                           blue: cb * bright, alpha: 1))
                ctx.setLineWidth(2.4)
                ctx.move(to: strandPoint(i, sign))
                for j in (i + 1)...end { ctx.addLine(to: strandPoint(j, sign)) }
                ctx.strokePath()
                i = end
            }
        }

        // Antiparallel end labels
        let white = NSColor(calibratedWhite: 0.9, alpha: 1)
        drawLabel("5\u{2032}", at: offsetEnd(samples, first: true, along: 10, side: amp),
                  color: white, size: 9)
        drawLabel("3\u{2032}", at: offsetEnd(samples, first: false, along: 10, side: amp),
                  color: white, size: 9)
    }

    private func offsetEnd(_ samples: [(p: CGPoint, t: CGVector)], first: Bool,
                           along: CGFloat, side: CGFloat) -> CGPoint {
        let s = first ? samples[0] : samples[samples.count - 1]
        let dir: CGFloat = first ? -1 : 1
        return CGPoint(x: s.p.x + s.t.dx * along * dir,
                       y: s.p.y + s.t.dy * along * dir)
    }

    // RNA: single backbone with lettered bases waving off the sugar-phosphate
    // strand; U (purple) in place of T.
    private func drawRNA(_ ctx: CGContext, _ snapshot: [CGPoint], color: NSColor,
                         seed: Int) {
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(2.6)
        addSmoothPath(ctx, snapshot)
        ctx.strokePath()
        let samples = resample(snapshot, spacing: 17)
        guard samples.count > 4 else { return }
        let letters = ["A", "U", "G", "C"]
        for (i, s) in samples.enumerated() where i > 0 && i < samples.count - 1 {
            let side: CGFloat = sin(CGFloat(i) * 0.85) > 0 ? 1 : -1
            let perp = CGVector(dx: -s.t.dy * side, dy: s.t.dx * side)
            let bi = (i * 11 + seed) % 4
            let bc = bi == 1 ? uColor : baseColors[bi]
            ctx.setStrokeColor(bc.cgColor)
            ctx.setLineWidth(2.2)
            ctx.move(to: s.p)
            ctx.addLine(to: CGPoint(x: s.p.x + perp.dx * 9, y: s.p.y + perp.dy * 9))
            ctx.strokePath()
            drawLabel(letters[bi],
                      at: CGPoint(x: s.p.x + perp.dx * 15, y: s.p.y + perp.dy * 15),
                      color: bc, size: 8)
        }
        let white = NSColor(calibratedWhite: 0.9, alpha: 1)
        drawLabel("5\u{2032}", at: offsetEnd(samples, first: true, along: 10, side: 0),
                  color: white, size: 9)
        drawLabel("3\u{2032}", at: offsetEnd(samples, first: false, along: 10, side: 0),
                  color: white, size: 9)
    }

    // MARK: - Benzene bouncer

    private func drawBenzene(_ ctx: CGContext) {
        for (i, p) in benzeneTrail.enumerated() {
            let a = 0.05 + 0.08 * CGFloat(i) / CGFloat(max(benzeneTrail.count, 1))
            drawBenzeneRing(ctx, at: p, radius: 30, alpha: a, lineScale: 0.7)
        }
        drawBenzeneRing(ctx, at: benzene.p, radius: 30, alpha: 1.0, lineScale: 1.0)
    }

    private func drawBenzeneRing(_ ctx: CGContext, at p: CGPoint, radius: CGFloat,
                                 alpha: CGFloat, lineScale: CGFloat) {
        let color = NSColor(calibratedHue: benzeneHue, saturation: 0.75,
                            brightness: 1.0, alpha: alpha)
        ctx.saveGState()
        ctx.translateBy(x: p.x, y: p.y)
        ctx.rotate(by: benzeneRot)
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(3 * lineScale)
        let hex = CGMutablePath()
        for i in 0..<6 {
            let a = CGFloat(i) * .pi / 3 + .pi / 6
            let v = CGPoint(x: radius * cos(a), y: radius * sin(a))
            if i == 0 { hex.move(to: v) } else { hex.addLine(to: v) }
        }
        hex.closeSubpath()
        ctx.addPath(hex)
        ctx.strokePath()
        ctx.setLineWidth(2 * lineScale)
        ctx.strokeEllipse(in: CGRect(x: -radius * 0.58, y: -radius * 0.58,
                                     width: radius * 1.16, height: radius * 1.16))
        ctx.setFillColor(color.cgColor)
        for i in 0..<6 {
            let a = CGFloat(i) * .pi / 3 + .pi / 6
            let v = CGPoint(x: radius * cos(a), y: radius * sin(a))
            let r: CGFloat = 3 * lineScale
            ctx.fillEllipse(in: CGRect(x: v.x - r, y: v.y - r, width: 2 * r, height: 2 * r))
        }
        ctx.restoreGState()
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { MystifyPolymersView(frame: $0, isPreview: false)! }
#endif
