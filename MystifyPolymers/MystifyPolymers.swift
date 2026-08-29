import ScreenSaver

// Mystify Origami — the Windows "Mystify" saver done properly this time:
// rigid straight-edged polygons bouncing around the screen with deep echo
// trails, except the polygons are DNA nanostructure tiles — every edge is a
// straight double helix drawn as space-filling bead chains (navy strands,
// orange sticky ends, gray base-pair rungs) meeting at green single-stranded
// connector loops, after the classic Mao/Seeman tile figures. A benzene ring
// still bounces around DVD-logo style.

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

private struct Poly {
    var points: [Bouncer]
    var history: [[CGPoint]] = []
    var hue: CGFloat
    var recordCounter = 0
}

@objc(MystifyPolymersView)
public final class MystifyPolymersView: ScreenSaverView {

    private var polys: [Poly] = []
    private var benzene = Bouncer(p: .zero, v: .zero)
    private var benzeneTrail: [CGPoint] = []
    private var benzeneHue: CGFloat = 0.55
    private var benzeneRot: CGFloat = 0
    private var t: CGFloat = 0
    private let historyLen = 18

    private let navy = CGColor(red: 0.18, green: 0.24, blue: 0.62, alpha: 1)
    private let navyDim = CGColor(red: 0.11, green: 0.15, blue: 0.42, alpha: 1)
    private let orange = CGColor(red: 0.95, green: 0.58, blue: 0.14, alpha: 1)
    private let orangeDim = CGColor(red: 0.68, green: 0.40, blue: 0.10, alpha: 1)
    private let connectorGreen = CGColor(red: 0.55, green: 0.78, blue: 0.28, alpha: 1)
    private let rungGray = CGColor(red: 0.82, green: 0.84, blue: 0.88, alpha: 0.9)

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
        let speed: CGFloat = isPreview ? 2.6 : 5.0
        // Two rigid tiles: a triangle (3-point star tile) and a quad (cross tile).
        polys = [3, 4].enumerated().map { (i, count) in
            let pts = (0..<count).map { _ -> Bouncer in
                var b = Bouncer(p: CGPoint(x: rnd(10...w), y: rnd(10...h)), v: .zero)
                let a = rnd(0...(2 * .pi))
                let s = rnd(0.6...1.0) * speed
                b.v = CGVector(dx: cos(a) * s, dy: sin(a) * s)
                return b
            }
            return Poly(points: pts, hue: CGFloat(i) * 0.45 + rnd(0...0.1))
        }
        benzene = Bouncer(p: CGPoint(x: w / 2, y: h / 2),
                          v: CGVector(dx: speed * 0.7, dy: speed * 0.55))
    }

    public override func animateOneFrame() {
        t += 1.0 / 30.0
        let r = bounds.insetBy(dx: 8, dy: 8)
        for c in polys.indices {
            for i in polys[c].points.indices { polys[c].points[i].step(in: r) }
            polys[c].recordCounter += 1
            if polys[c].recordCounter >= 3 {
                polys[c].recordCounter = 0
                polys[c].history.append(polys[c].points.map { $0.p })
                if polys[c].history.count > historyLen { polys[c].history.removeFirst() }
            }
        }
        let ring = bounds.insetBy(dx: 42, dy: 42)
        let before = benzene.v
        benzene.step(in: ring)
        if before.dx != benzene.v.dx || before.dy != benzene.v.dy {
            benzeneHue = rnd(0...1)
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
        for p in polys { drawPoly(ctx, p) }
        drawBenzene(ctx)
    }

    // MARK: - Tiles

    private func drawPoly(_ ctx: CGContext, _ poly: Poly) {
        let n = poly.history.count
        guard n > 0 else { return }
        // The famous echo: many straight-edged wireframe ghosts in cycling hue.
        for (i, snapshot) in poly.history.enumerated() where i < n - 1 {
            let age = CGFloat(i + 1) / CGFloat(n)
            let hue = (poly.hue + t * 0.03 + CGFloat(i) * 0.018)
                .truncatingRemainder(dividingBy: 1)
            let color = NSColor(calibratedHue: hue < 0 ? hue + 1 : hue,
                                saturation: 0.9,
                                brightness: 0.5 + 0.5 * age,
                                alpha: 0.10 + 0.55 * age)
            ctx.setStrokeColor(color.cgColor)
            ctx.setLineWidth(1.4)
            closedPath(ctx, snapshot)
            ctx.strokePath()
        }
        // The front tile: every edge is a straight DNA duplex.
        let pts = poly.history[n - 1]
        for i in 0..<pts.count {
            drawEdgeDuplex(ctx, pts[i], pts[(i + 1) % pts.count])
        }
        // Green single-stranded connector loops at the junctions.
        ctx.setFillColor(connectorGreen)
        for v in pts {
            for k in 0..<6 {
                let a = CGFloat(k) * .pi / 3 + t * 0.8
                let bp = CGPoint(x: v.x + 7.5 * cos(a), y: v.y + 7.5 * sin(a))
                ctx.fillEllipse(in: CGRect(x: bp.x - 2.6, y: bp.y - 2.6,
                                           width: 5.2, height: 5.2))
            }
        }
    }

    private func closedPath(_ ctx: CGContext, _ pts: [CGPoint]) {
        guard pts.count > 1 else { return }
        ctx.move(to: pts[0])
        for p in pts.dropFirst() { ctx.addLine(to: p) }
        ctx.closePath()
    }

    /// A straight double helix in space-filling bead style: two antiphase bead
    /// strands about the edge line, gray rungs where the groove opens, orange
    /// sticky ends near the junctions, twist animated slowly.
    private func drawEdgeDuplex(_ ctx: CGContext, _ a: CGPoint, _ b: CGPoint) {
        let dx = b.x - a.x, dy = b.y - a.y
        let len = max(hypot(dx, dy), 0.001)
        guard len > 30 else { return }
        let ux = dx / len, uy = dy / len
        let px = -uy, py = ux
        let period: CGFloat = 38
        let amp: CGFloat = 5.5
        let twist = t * 1.6

        func strandPoint(_ d: CGFloat, _ sign: CGFloat) -> CGPoint {
            let off = amp * sin(d / period * 2 * .pi + twist) * sign
            return CGPoint(x: a.x + ux * d + px * off, y: a.y + uy * d + py * off)
        }
        // Rungs under the beads
        ctx.setStrokeColor(rungGray)
        ctx.setLineWidth(2)
        var d: CGFloat = 12
        while d < len - 12 {
            let phase = d / period * 2 * .pi + twist
            if abs(sin(phase)) > 0.45 {
                ctx.move(to: strandPoint(d, 1))
                ctx.addLine(to: strandPoint(d, -1))
                ctx.strokePath()
            }
            d += 7.5
        }
        // Bead strands, front/back shaded, sticky ends in orange
        d = 3
        while d < len - 3 {
            let phase = d / period * 2 * .pi + twist
            let ends = d < 17 || d > len - 17
            for sign: CGFloat in [1, -1] {
                let front = cos(phase) * sign > 0
                let color = ends ? (front ? orange : orangeDim)
                                 : (front ? navy : navyDim)
                ctx.setFillColor(color)
                let q = strandPoint(d, sign)
                let r: CGFloat = front ? 2.6 : 2.2
                ctx.fillEllipse(in: CGRect(x: q.x - r, y: q.y - r,
                                           width: 2 * r, height: 2 * r))
            }
            d += 3.4
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
