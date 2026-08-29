import ScreenSaver

// Mystify Polymers — the Windows "Mystify" bouncing-bezier saver reimagined as
// wiggling polymer chains changing conformation, plus a bouncing benzene ring
// that changes color whenever it hits a wall (DVD-logo style).

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
    var hueB: CGFloat  // comonomer bead color
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
    private let historyLen = 15

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
        let speed: CGFloat = isPreview ? 2.5 : 5.0
        chains = (0..<3).map { i in
            let pts = (0..<6).map { _ -> Bouncer in
                var b = Bouncer(p: CGPoint(x: rnd(10...w), y: rnd(10...h)), v: .zero)
                let a = rnd(0...(2 * .pi))
                let s = rnd(0.55...1.0) * speed
                b.v = CGVector(dx: cos(a) * s, dy: sin(a) * s)
                return b
            }
            return Chain(points: pts,
                         hue: CGFloat(i) / 3 + rnd(0...0.1),
                         hueB: CGFloat(i) / 3 + 0.5 + rnd(0...0.1))
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

    // Catmull-Rom smoothing through the control points.
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

    // Points sampled along the same Catmull-Rom curve, for monomer beads.
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

    private func drawChain(_ ctx: CGContext, _ c: Chain) {
        let n = c.history.count
        for (i, snapshot) in c.history.enumerated() {
            let age = CGFloat(i + 1) / CGFloat(max(n, 1))
            let hue = (c.hue + t * 0.02 + CGFloat(i) * 0.012).truncatingRemainder(dividingBy: 1)
            let isNewest = i == n - 1
            let color = NSColor(calibratedHue: hue < 0 ? hue + 1 : hue,
                                saturation: 0.85,
                                brightness: 0.6 + 0.4 * age,
                                alpha: isNewest ? 0.95 : 0.12 + 0.38 * age)
            ctx.setStrokeColor(color.cgColor)
            ctx.setLineWidth(isNewest ? 2.6 : 1.4)
            addSmoothPath(ctx, snapshot)
            ctx.strokePath()
            if isNewest {
                // Monomer beads along the backbone — alternating comonomers.
                let beads = sampleCurve(snapshot, per: 4)
                let hueA = hue
                let hueB = (c.hueB + t * 0.02).truncatingRemainder(dividingBy: 1)
                for (j, b) in beads.enumerated() where j % 2 == 0 {
                    let bh = (j % 4 == 0) ? hueA : hueB
                    let bc = NSColor(calibratedHue: bh < 0 ? bh + 1 : bh,
                                     saturation: 0.7, brightness: 1.0, alpha: 0.95)
                    ctx.setFillColor(bc.cgColor)
                    let r: CGFloat = (j % 4 == 0) ? 3.2 : 2.2
                    ctx.fillEllipse(in: CGRect(x: b.x - r, y: b.y - r, width: 2 * r, height: 2 * r))
                }
            }
        }
    }

    private func drawBenzene(_ ctx: CGContext) {
        // Faded trail
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
        // Vertex carbons
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
