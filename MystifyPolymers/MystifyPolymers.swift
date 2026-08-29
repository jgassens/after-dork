import ScreenSaver

// Mystify Polymers — the Windows "Mystify" bouncing-bezier saver reimagined as
// real polymer backbones changing conformation: Kevlar (aromatic amides),
// nylon-6,6 (aliphatic amides), and polystyrene (pendant phenyls), each drawn
// as a structural formula along the wiggling curve, with fading ghosts of past
// conformations. Plus a benzene ring bouncing DVD-logo style.

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
    var style: Int  // 0 Kevlar, 1 nylon-6,6, 2 polystyrene
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

    private let oColor = NSColor(calibratedRed: 1.0, green: 0.36, blue: 0.3, alpha: 1)
    private let nColor = NSColor(calibratedRed: 0.42, green: 0.62, blue: 1.0, alpha: 1)
    private let hColor = NSColor(calibratedRed: 0.88, green: 0.9, blue: 0.95, alpha: 1)

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

    // MARK: - Atom labels

    private func drawLabel(_ s: String, at p: CGPoint, color: NSColor, size: CGFloat) {
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.boldSystemFont(ofSize: size),
            .foregroundColor: color,
        ]
        let str = NSAttributedString(string: s, attributes: attrs)
        let sz = str.size()
        // Black halo so the letter sits cleanly on the backbone line.
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
        // Current conformation as an actual polymer structure
        let hue = (c.hue + t * 0.02 + CGFloat(n - 1) * 0.012).truncatingRemainder(dividingBy: 1)
        let color = NSColor(calibratedHue: hue < 0 ? hue + 1 : hue,
                            saturation: 0.65, brightness: 1.0, alpha: 0.95)
        switch c.style {
        case 0: drawKevlar(ctx, c.history[n - 1], color: color)
        case 1: drawNylon(ctx, c.history[n - 1], color: color)
        default: drawPolystyrene(ctx, c.history[n - 1], color: color)
        }
    }

    private func hexagon(_ ctx: CGContext, center: CGPoint, radius: CGFloat,
                         rotation: CGFloat, color: NSColor, aromatic: Bool) {
        let path = CGMutablePath()
        for k in 0..<6 {
            let a = rotation + CGFloat(k) * .pi / 3
            let v = CGPoint(x: center.x + radius * cos(a), y: center.y + radius * sin(a))
            if k == 0 { path.move(to: v) } else { path.addLine(to: v) }
        }
        path.closeSubpath()
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 1))
        ctx.addPath(path)
        ctx.fillPath()
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(2)
        ctx.addPath(path)
        ctx.strokePath()
        if aromatic {
            ctx.setLineWidth(1.3)
            ctx.strokeEllipse(in: CGRect(x: center.x - radius * 0.55,
                                         y: center.y - radius * 0.55,
                                         width: radius * 1.1, height: radius * 1.1))
        }
    }

    /// C=O stub: two short parallel lines plus a red O.
    private func drawCarbonyl(_ ctx: CGContext, at p: CGPoint, dir: CGVector,
                              side: CGFloat, color: NSColor) {
        let perp = CGVector(dx: -dir.dy * side, dy: dir.dx * side)
        let end = CGPoint(x: p.x + perp.dx * 9, y: p.y + perp.dy * 9)
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(1.6)
        for off: CGFloat in [-1.5, 1.5] {
            ctx.move(to: CGPoint(x: p.x + dir.dx * off, y: p.y + dir.dy * off))
            ctx.addLine(to: CGPoint(x: end.x + dir.dx * off, y: end.y + dir.dy * off))
        }
        ctx.strokePath()
        drawLabel("O", at: CGPoint(x: p.x + perp.dx * 13, y: p.y + perp.dy * 13),
                  color: oColor, size: 10)
    }

    /// N with its H stub on the opposite side of the backbone.
    private func drawAmideN(_ ctx: CGContext, at p: CGPoint, dir: CGVector,
                            side: CGFloat, color: NSColor) {
        let perp = CGVector(dx: dir.dy * side, dy: -dir.dx * side)
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(1.6)
        ctx.move(to: p)
        ctx.addLine(to: CGPoint(x: p.x + perp.dx * 8, y: p.y + perp.dy * 8))
        ctx.strokePath()
        drawLabel("N", at: p, color: nColor, size: 10)
        drawLabel("H", at: CGPoint(x: p.x + perp.dx * 12, y: p.y + perp.dy * 12),
                  color: hColor, size: 8)
    }

    // Kevlar: [-NH-C6H4-NH-CO-C6H4-CO-]n — rings on the smooth curve joined by
    // amide linkages.
    private func drawKevlar(_ ctx: CGContext, _ snapshot: [CGPoint], color: NSColor) {
        let samples = resample(snapshot, spacing: 13)
        guard samples.count > 7 else { return }
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(1.8)
        ctx.move(to: samples[0].p)
        for s in samples.dropFirst() { ctx.addLine(to: s.p) }
        ctx.strokePath()
        // PPTA: the amide direction alternates between linkages, so each ring
        // carries the same group on both sides — ring(-NH x2), ring(-CO x2).
        var i = 0
        while i < samples.count {
            let cycle = i % 6
            let linkIdx = i / 6
            let side: CGFloat = linkIdx % 2 == 0 ? 1 : -1
            let s = samples[i]
            let angle = atan2(s.t.dy, s.t.dx)
            if cycle == 0 {
                hexagon(ctx, center: s.p, radius: 10, rotation: angle,
                        color: color, aromatic: true)
            } else if cycle == 3 {
                if linkIdx % 2 == 0 {
                    drawAmideN(ctx, at: s.p, dir: s.t, side: side, color: color)
                } else {
                    drawCarbonyl(ctx, at: s.p, dir: s.t, side: side, color: color)
                }
            } else if cycle == 4 {
                if linkIdx % 2 == 0 {
                    drawCarbonyl(ctx, at: s.p, dir: s.t, side: side, color: color)
                } else {
                    drawAmideN(ctx, at: s.p, dir: s.t, side: side, color: color)
                }
            }
            i += 1
        }
    }

    // Nylon-6,6: zigzag aliphatic backbone with periodic amide groups.
    private func drawNylon(_ ctx: CGContext, _ snapshot: [CGPoint], color: NSColor) {
        let samples = resample(snapshot, spacing: 11)
        guard samples.count > 4 else { return }
        var zig: [CGPoint] = []
        for (i, s) in samples.enumerated() {
            let side: CGFloat = i % 2 == 0 ? 4.5 : -4.5
            zig.append(CGPoint(x: s.p.x - s.t.dy * side, y: s.p.y + s.t.dx * side))
        }
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(1.8)
        ctx.move(to: zig[0])
        for p in zig.dropFirst() { ctx.addLine(to: p) }
        ctx.strokePath()
        // Nylon-6,6: -NH-(CH2)6-NH-CO-(CH2)4-CO- — amide junctions come in
        // mirrored pairs, with 6 carbons between the nitrogens and 4 between
        // the carbonyls. Period of 14 zigzag vertices: N,CO | 4x CH2 | CO,N | 6x CH2.
        for i in 2..<zig.count {
            let cycle = (i - 2) % 14
            let apex: CGFloat = i % 2 == 0 ? 1 : -1  // stubs point out of the apex
            switch cycle {
            case 0, 7:
                drawAmideN(ctx, at: zig[i], dir: samples[i].t, side: apex, color: color)
            case 1, 6:
                drawCarbonyl(ctx, at: zig[i], dir: samples[i].t, side: apex, color: color)
            default:
                break
            }
        }
    }

    // Polystyrene: zigzag backbone with pendant phenyl rings on alternate carbons.
    private func drawPolystyrene(_ ctx: CGContext, _ snapshot: [CGPoint], color: NSColor) {
        let samples = resample(snapshot, spacing: 11)
        guard samples.count > 4 else { return }
        var zig: [CGPoint] = []
        for (i, s) in samples.enumerated() {
            let side: CGFloat = i % 2 == 0 ? 4.5 : -4.5
            zig.append(CGPoint(x: s.p.x - s.t.dy * side, y: s.p.y + s.t.dx * side))
        }
        ctx.setStrokeColor(color.cgColor)
        ctx.setLineWidth(1.8)
        ctx.move(to: zig[0])
        for p in zig.dropFirst() { ctx.addLine(to: p) }
        ctx.strokePath()
        for i in stride(from: 2, to: zig.count - 1, by: 4) {
            let s = samples[i]
            let side: CGFloat = i % 2 == 0 ? 1 : -1
            let perp = CGVector(dx: -s.t.dy * side, dy: s.t.dx * side)
            let ringC = CGPoint(x: zig[i].x + perp.dx * 15, y: zig[i].y + perp.dy * 15)
            ctx.setStrokeColor(color.cgColor)
            ctx.setLineWidth(1.6)
            ctx.move(to: zig[i])
            ctx.addLine(to: CGPoint(x: ringC.x - perp.dx * 7, y: ringC.y - perp.dy * 7))
            ctx.strokePath()
            hexagon(ctx, center: ringC, radius: 7,
                    rotation: atan2(perp.dy, perp.dx), color: color, aromatic: true)
        }
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
