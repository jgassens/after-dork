import ScreenSaver

// Stoddart Reef — After Dark's "Fish!" aquarium, restocked with mechanically
// interlocked molecules: shuttling bistable rotaxanes, a [2]catenane,
// Borromean rings, a crown-ether jellyfish (K+ included), a ferrocene crab
// scuttling on the sand, PEG seaweed, and a MOF crystal for a castle.

private func rnd(_ r: ClosedRange<CGFloat>) -> CGFloat { CGFloat.random(in: r) }

private enum SwimmerKind: CaseIterable {
    case rotaxane, catenane, borromean, jellyfish, trefoil, daisyChain, cucurbituril
}

private struct Swimmer {
    var kind: SwimmerKind
    var x: CGFloat, y: CGFloat
    var dir: CGFloat          // +1 swims right, -1 swims left
    var speed: CGFloat
    var phase: CGFloat
    var phaseRate: CGFloat
    var scale: CGFloat
}

private struct Bubble {
    var x: CGFloat, y: CGFloat
    var r: CGFloat
    var speed: CGFloat
    var wobble: CGFloat
}

private struct Weed {
    var x: CGFloat
    var segments: Int
    var phase: CGFloat
}

@objc(StoddartReefView)
public final class StoddartReefView: ScreenSaverView {

    private var swimmers: [Swimmer] = []
    private var bubbles: [Bubble] = []
    private var weeds: [Weed] = []
    private var crabX: CGFloat = 200
    private var crabDir: CGFloat = 1
    private var crabPhase: CGFloat = 0
    private var t: CGFloat = 0
    private let sandH: CGFloat = 46

    private let boxBlue = CGColor(red: 0.25, green: 0.45, blue: 0.92, alpha: 1)
    private let crownRed = CGColor(red: 0.95, green: 0.45, blue: 0.35, alpha: 1)

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
        let w = max(bounds.width, 800), h = max(bounds.height, 500)
        let census: [SwimmerKind] = [.rotaxane, .catenane, .jellyfish, .borromean,
                                     .daisyChain, .cucurbituril, .trefoil, .rotaxane]
        var pop = max(3, min(20, Int(AfterDork.value("StoddartReef", "population", 12))))
        if isPreview { pop = min(pop, 6) }
        let kinds = (0..<pop).map { census[$0 % census.count] }
        swimmers = kinds.map { kind in
            Swimmer(kind: kind,
                    x: rnd(0...w),
                    y: rnd((h * 0.25)...(h * 0.85)),
                    dir: Bool.random() ? 1 : -1,
                    speed: kind == .jellyfish ? rnd(0.25...0.5) : rnd(0.8...1.9),
                    phase: rnd(0...6.28),
                    phaseRate: rnd(0.03...0.06),
                    scale: rnd(0.6...1.15))
        }
        let bubbleCount = AfterDork.flag("StoddartReef", "bubbles", true)
            ? (isPreview ? 6 : 14) : 0
        bubbles = (0..<bubbleCount).map { _ in
            Bubble(x: rnd(0...w), y: rnd(0...h), r: rnd(2...5),
                   speed: rnd(0.8...2.0), wobble: rnd(0...6.28))
        }
        let weedCount = isPreview ? 3 : 5
        weeds = (0..<weedCount).map { i in
            Weed(x: w * (0.08 + 0.85 * CGFloat(i) / CGFloat(max(weedCount - 1, 1)))
                    + rnd(-30...30),
                 segments: Int.random(in: 9...15),
                 phase: rnd(0...6.28))
        }
        crabX = rnd((w * 0.15)...(w * 0.5))
    }

    public override func animateOneFrame() {
        t += 1.0 / 30.0
        let w = max(bounds.width, 800), h = max(bounds.height, 500)
        for i in swimmers.indices {
            swimmers[i].phase += swimmers[i].phaseRate * (swimmers[i].kind == .jellyfish ? 2 : 1)
            let s = swimmers[i]
            swimmers[i].x += s.dir * s.speed * (0.5 + s.scale * 0.7)
            let m = 260 * s.scale
            if s.dir > 0 && s.x > w + m { respawn(&swimmers[i], fromLeft: true, w: w, h: h) }
            if s.dir < 0 && s.x < -m { respawn(&swimmers[i], fromLeft: false, w: w, h: h) }
        }
        for i in bubbles.indices {
            bubbles[i].y += bubbles[i].speed
            bubbles[i].wobble += 0.08
            if bubbles[i].y > h + 10 {
                bubbles[i].y = sandH - 5
                bubbles[i].x = rnd(0...w)
            }
        }
        // Crab scuttles, pausing now and then
        crabPhase += 0.14
        if sin(t * 0.31) > -0.25 {
            crabX += crabDir * 0.7
            if crabX < w * 0.06 { crabDir = 1 }
            if crabX > w * 0.62 { crabDir = -1 }
        }
        needsDisplay = true
    }

    private func respawn(_ s: inout Swimmer, fromLeft: Bool, w: CGFloat, h: CGFloat) {
        s.dir = fromLeft ? 1 : -1
        s.x = fromLeft ? -240 * s.scale : w + 240 * s.scale
        s.y = rnd((h * 0.25)...(h * 0.85))
        s.speed = s.kind == .jellyfish ? rnd(0.25...0.5) : rnd(0.8...1.9)
        s.scale = rnd(0.6...1.15)
    }

    // MARK: - Drawing

    public override func draw(_ rect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        drawWater(ctx)
        drawSand(ctx)
        drawCastle(ctx)
        for weed in weeds { drawWeed(ctx, weed) }
        drawCrab(ctx)
        for s in swimmers.sorted(by: { $0.scale < $1.scale }) { drawSwimmer(ctx, s) }
        drawBubbles(ctx)
    }

    private func drawWater(_ ctx: CGContext) {
        let colors = [CGColor(red: 0.03, green: 0.20, blue: 0.32, alpha: 1),
                      CGColor(red: 0.005, green: 0.05, blue: 0.11, alpha: 1)]
        if let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(),
                                 colors: colors as CFArray, locations: [1, 0]) {
            ctx.drawLinearGradient(grad,
                                   start: CGPoint(x: 0, y: 0),
                                   end: CGPoint(x: 0, y: bounds.height),
                                   options: [])
        }
    }

    private func drawSand(_ ctx: CGContext) {
        ctx.setFillColor(CGColor(red: 0.36, green: 0.33, blue: 0.24, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: bounds.width, height: sandH))
        // Speckles, deterministic so they don't shimmer
        ctx.setFillColor(CGColor(red: 0.45, green: 0.42, blue: 0.30, alpha: 1))
        var seed: UInt64 = 0x5EED
        for _ in 0..<Int(bounds.width / 6) {
            seed = seed &* 6364136223846793005 &+ 1442695040888963407
            let sx = CGFloat(seed % 10000) / 10000 * bounds.width
            let sy = CGFloat((seed >> 16) % 1000) / 1000 * (sandH - 6)
            ctx.fill(CGRect(x: sx, y: sy, width: 2.5, height: 2.5))
        }
    }

    /// The aquarium castle, except it's a MOF crystal: a cubic cage on the sand.
    private func drawCastle(_ ctx: CGContext) {
        let baseX = bounds.width * 0.74, baseY = sandH - 4
        let size: CGFloat = 74, off: CGFloat = 20
        let tan = CGColor(red: 0.55, green: 0.52, blue: 0.40, alpha: 1)
        let dimTan = CGColor(red: 0.38, green: 0.36, blue: 0.29, alpha: 1)
        func frameSquare(_ x: CGFloat, _ y: CGFloat, _ color: CGColor, _ w: CGFloat) {
            ctx.setStrokeColor(color)
            ctx.setLineWidth(w)
            ctx.stroke(CGRect(x: x, y: y, width: size, height: size))
        }
        frameSquare(baseX + off, baseY + off * 0.7, dimTan, 2.2)     // back face
        for (dx, dy) in [(0, 0), (size, 0), (0, size), (size, size)] {
            ctx.setStrokeColor(dimTan)
            ctx.setLineWidth(2.2)
            ctx.move(to: CGPoint(x: baseX + CGFloat(dx), y: baseY + CGFloat(dy)))
            ctx.addLine(to: CGPoint(x: baseX + CGFloat(dx) + off,
                                    y: baseY + CGFloat(dy) + off * 0.7))
            ctx.strokePath()
        }
        frameSquare(baseX, baseY, tan, 3)                             // front face
        // Node balls on the front corners
        ctx.setFillColor(tan)
        for (dx, dy) in [(0, 0), (size, 0), (0, size), (size, size)] {
            ctx.fillEllipse(in: CGRect(x: baseX + CGFloat(dx) - 5,
                                       y: baseY + CGFloat(dy) - 5, width: 10, height: 10))
        }
        // Castle door, as is traditional
        ctx.setFillColor(CGColor(red: 0.02, green: 0.05, blue: 0.09, alpha: 1))
        ctx.fill(CGRect(x: baseX + size / 2 - 9, y: baseY, width: 18, height: 22))
        ctx.fillEllipse(in: CGRect(x: baseX + size / 2 - 9, y: baseY + 13,
                                   width: 18, height: 18))
    }

    /// PEG seaweed: a skeletal glycol chain — vertices zig-zagging across the
    /// swaying spine like a drawn PEG, ether oxygens every third vertex
    /// (O-C-C repeat), a terminal OH at the tip.
    private func drawWeed(_ ctx: CGContext, _ weed: Weed) {
        var pts: [CGPoint] = []
        for k in 0...weed.segments {
            let sway = sin(t * 1.1 + weed.phase + CGFloat(k) * 0.45) * CGFloat(k) * 1.1
            let zig: CGFloat = k % 2 == 0 ? -4 : 4
            pts.append(CGPoint(x: weed.x + sway + zig, y: sandH - 6 + CGFloat(k) * 10))
        }
        ctx.setStrokeColor(CGColor(red: 0.22, green: 0.65, blue: 0.40, alpha: 0.95))
        ctx.setLineWidth(2.6)
        ctx.setLineJoin(.round)
        ctx.move(to: pts[0])
        for p in pts.dropFirst() { ctx.addLine(to: p) }
        ctx.strokePath()
        ctx.setFillColor(CGColor(red: 0.95, green: 0.4, blue: 0.32, alpha: 1))
        for (k, p) in pts.enumerated() where k % 3 == 2 {
            ctx.fillEllipse(in: CGRect(x: p.x - 2.5, y: p.y - 2.5, width: 5, height: 5))
        }
        if let tip = pts.last {
            drawTinyLabel(ctx, "OH", at: CGPoint(x: tip.x, y: tip.y + 8), size: 8,
                          color: NSColor(calibratedRed: 0.95, green: 0.4, blue: 0.32,
                                         alpha: 1), halo: false)
        }
    }

    /// Ferrocene crab: Cp-ring sandwich with an iron heart, scuttling sideways.
    private func drawCrab(_ ctx: CGContext) {
        ctx.saveGState()
        ctx.translateBy(x: crabX, y: sandH + 16)
        let cp = CGColor(red: 0.85, green: 0.62, blue: 0.25, alpha: 1)
        // Legs first, alternating like a proper crab
        ctx.setStrokeColor(cp)
        ctx.setLineWidth(2)
        for i in 0..<3 {
            let lift = sin(crabPhase + CGFloat(i) * 2.1) * 3
            for side: CGFloat in [-1, 1] {
                ctx.move(to: CGPoint(x: side * 10, y: -8))
                ctx.addLine(to: CGPoint(x: side * (20 + CGFloat(i) * 6),
                                        y: -14 + (i == 1 ? lift : -lift)))
                ctx.strokePath()
            }
        }
        // Cp rings above and below the iron
        for dy: CGFloat in [9, -9] {
            ctx.setFillColor(CGColor(red: 0.12, green: 0.1, blue: 0.05, alpha: 1))
            ctx.fillEllipse(in: CGRect(x: -17, y: dy - 4.5, width: 34, height: 9))
            ctx.setStrokeColor(cp)
            ctx.setLineWidth(2.2)
            ctx.strokeEllipse(in: CGRect(x: -17, y: dy - 4.5, width: 34, height: 9))
        }
        ctx.setFillColor(CGColor(red: 0.9, green: 0.42, blue: 0.15, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: -6.5, y: -6.5, width: 13, height: 13))
        drawTinyLabel(ctx, "Fe", at: .zero, size: 8,
                      color: NSColor(calibratedWhite: 1, alpha: 0.95), halo: false)
        ctx.restoreGState()
    }

    private func drawBubbles(_ ctx: CGContext) {
        for b in bubbles {
            let x = b.x + sin(b.wobble) * 4
            ctx.setStrokeColor(CGColor(red: 0.8, green: 0.92, blue: 1.0, alpha: 0.5))
            ctx.setLineWidth(1.2)
            ctx.strokeEllipse(in: CGRect(x: x - b.r, y: b.y - b.r,
                                         width: 2 * b.r, height: 2 * b.r))
            ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.35))
            ctx.fillEllipse(in: CGRect(x: x - b.r * 0.3, y: b.y + b.r * 0.2,
                                       width: b.r * 0.5, height: b.r * 0.5))
        }
    }

    private func drawTinyLabel(_ ctx: CGContext, _ s: String, at p: CGPoint,
                               size: CGFloat, color: NSColor, halo: Bool) {
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.boldSystemFont(ofSize: size),
            .foregroundColor: color,
        ]
        let str = NSAttributedString(string: s, attributes: attrs)
        let sz = str.size()
        if halo {
            ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 0.7))
            ctx.fillEllipse(in: CGRect(x: p.x - sz.width / 2 - 1.5,
                                       y: p.y - sz.height / 2 - 0.5,
                                       width: sz.width + 3, height: sz.height + 1))
        }
        str.draw(at: CGPoint(x: p.x - sz.width / 2, y: p.y - sz.height / 2))
    }

    // MARK: - Swimmers

    /// 18-crown-6 skeletal ring: 18 vertices (O-CH2-CH2 repeating), the six
    /// oxygens tucked into notches at 0.82R so each ethylene bridge bulges
    /// outward — the classic crown scallop. Leaves the path in the context
    /// (caller strokes it) and returns the oxygen positions.
    @discardableResult
    private func addCrownPath(_ ctx: CGContext, radius: CGFloat,
                              rotation: CGFloat) -> [CGPoint] {
        var oxygens: [CGPoint] = []
        ctx.beginPath()
        for i in 0..<18 {
            let a = rotation + CGFloat(i) * .pi / 9
            let r = i % 3 == 0 ? radius * 0.82 : radius
            let p = CGPoint(x: r * cos(a), y: r * sin(a))
            if i % 3 == 0 { oxygens.append(p) }
            if i == 0 { ctx.move(to: p) } else { ctx.addLine(to: p) }
        }
        ctx.closePath()
        return oxygens
    }

    private func drawSwimmer(_ ctx: CGContext, _ s: Swimmer) {
        ctx.saveGState()
        let bob = sin(s.phase * 1.3) * 7
        ctx.translateBy(x: s.x, y: s.y + bob)
        ctx.scaleBy(x: s.scale, y: s.scale)
        switch s.kind {
        case .rotaxane: drawRotaxane(ctx, s)
        case .catenane: drawCatenane(ctx, s)
        case .borromean: drawBorromean(ctx, s)
        case .jellyfish: drawJellyfish(ctx, s)
        case .trefoil: drawTrefoil(ctx, s)
        case .daisyChain: drawDaisyChain(ctx, s)
        case .cucurbituril: drawCucurbituril(ctx, s)
        }
        ctx.restoreGState()
    }

    /// Bistable [2]rotaxane: dumbbell axle, green and red stations, bulky
    /// stoppers, and the blue box shuttling between stations.
    private func drawRotaxane(_ ctx: CGContext, _ s: Swimmer) {
        ctx.saveGState()
        ctx.scaleBy(x: s.dir, y: 1)
        // Axle
        ctx.setStrokeColor(CGColor(red: 0.8, green: 0.84, blue: 0.9, alpha: 0.95))
        ctx.setLineWidth(3)
        ctx.setLineCap(.round)
        ctx.move(to: CGPoint(x: -74, y: 0))
        ctx.addLine(to: CGPoint(x: 74, y: 0))
        ctx.strokePath()
        // Stations: TTF green, DNP red
        ctx.setLineWidth(6.5)
        ctx.setStrokeColor(CGColor(red: 0.25, green: 0.78, blue: 0.4, alpha: 1))
        ctx.move(to: CGPoint(x: -42, y: 0)); ctx.addLine(to: CGPoint(x: -16, y: 0))
        ctx.strokePath()
        ctx.setStrokeColor(CGColor(red: 0.92, green: 0.45, blue: 0.25, alpha: 1))
        ctx.move(to: CGPoint(x: 16, y: 0)); ctx.addLine(to: CGPoint(x: 42, y: 0))
        ctx.strokePath()
        // Bulky stoppers
        let gray = CGColor(red: 0.62, green: 0.66, blue: 0.74, alpha: 1)
        for sx: CGFloat in [-1, 1] {
            ctx.setFillColor(gray)
            ctx.fillEllipse(in: CGRect(x: sx * 74 - 8, y: -8, width: 16, height: 16))
            for dy: CGFloat in [11, 0, -11] {
                ctx.fillEllipse(in: CGRect(x: sx * 74 + sx * 9 - 6, y: dy - 6,
                                           width: 12, height: 12))
            }
        }
        // The blue box, dwelling at one station then shuttling to the other
        let p = max(-1, min(1, 1.8 * sin(s.phase)))
        let rx = 29 * p
        let ring = CGRect(x: rx - 13, y: -24, width: 26, height: 48)
        ctx.setStrokeColor(boxBlue)
        ctx.setLineWidth(7.5)
        ctx.stroke(ring.insetBy(dx: 3.75, dy: 3.75))
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.4))
        ctx.setLineWidth(1.6)
        ctx.stroke(ring.insetBy(dx: 1.5, dy: 1.5))
        ctx.restoreGState()
    }

    /// [2]Catenane: the blue box interlocked with a crown ether, both slowly
    /// circumrotating; over-under fixed at the top crossing.
    private func drawCatenane(_ ctx: CGContext, _ s: Swimmer) {
        let rot = s.phase * 0.4
        func drawBox() {
            ctx.saveGState()
            ctx.translateBy(x: -15, y: 0)
            ctx.rotate(by: rot)
            ctx.setStrokeColor(boxBlue)
            ctx.setLineWidth(7)
            ctx.stroke(CGRect(x: -22, y: -22, width: 44, height: 44).insetBy(dx: 3.5, dy: 3.5))
            ctx.restoreGState()
        }
        func drawCrown() {
            ctx.saveGState()
            ctx.translateBy(x: 15, y: 0)
            ctx.setStrokeColor(crownRed)
            ctx.setLineWidth(6)
            ctx.setLineJoin(.round)
            let oxygens = addCrownPath(ctx, radius: 25, rotation: -rot)
            ctx.strokePath()
            ctx.setFillColor(CGColor(red: 1, green: 0.85, blue: 0.8, alpha: 1))
            for p in oxygens {
                ctx.fillEllipse(in: CGRect(x: p.x - 3, y: p.y - 3,
                                           width: 6, height: 6))
            }
            ctx.restoreGState()
        }
        drawBox()
        drawCrown()
        // Re-draw the box inside a clip at the upper crossing: interlocked.
        ctx.saveGState()
        ctx.addEllipse(in: CGRect(x: 0 - 12, y: 14, width: 24, height: 24))
        ctx.clip()
        drawBox()
        ctx.restoreGState()
    }

    /// Borromean rings: no two rings are linked, but the three are.
    private func drawBorromean(_ ctx: CGContext, _ s: Swimmer) {
        ctx.saveGState()
        ctx.rotate(by: s.phase * 0.25)
        let colors = [CGColor(red: 0.92, green: 0.42, blue: 0.35, alpha: 1),
                      CGColor(red: 0.35, green: 0.8, blue: 0.45, alpha: 1),
                      boxBlue]
        let d: CGFloat = 14, r: CGFloat = 24
        var centers: [CGPoint] = []
        for i in 0..<3 {
            let a = CGFloat(i) * 2 * .pi / 3 + .pi / 2
            centers.append(CGPoint(x: d * cos(a), y: d * sin(a)))
        }
        func ring(_ i: Int) {
            ctx.setStrokeColor(colors[i])
            ctx.setLineWidth(5.5)
            ctx.strokeEllipse(in: CGRect(x: centers[i].x - r, y: centers[i].y - r,
                                         width: 2 * r, height: 2 * r))
        }
        ring(0); ring(1); ring(2)
        // Fix one crossing per pair (0 over 2, 1 over 0, 2 over 1) so the
        // weave alternates and the topology reads Borromean.
        for (over, under) in [(0, 2), (1, 0), (2, 1)] {
            let ci = centers[over], cj = centers[under]
            let mid = CGPoint(x: (ci.x + cj.x) / 2, y: (ci.y + cj.y) / 2)
            let dx = cj.x - ci.x, dy = cj.y - ci.y
            let dist = max(hypot(dx, dy), 0.001)
            let h = (r * r - dist * dist / 4).squareRoot()
            let cross = CGPoint(x: mid.x - dy / dist * h, y: mid.y + dx / dist * h)
            ctx.saveGState()
            ctx.addEllipse(in: CGRect(x: cross.x - 9, y: cross.y - 9, width: 18, height: 18))
            ctx.clip()
            ring(over)
            ctx.restoreGState()
        }
        ctx.restoreGState()
    }

    /// 18-crown-6 jellyfish: pulsing macrocycle with a potassium passenger and
    /// glycol tentacles.
    private func drawJellyfish(_ ctx: CGContext, _ s: Swimmer) {
        let pulse = 1 + 0.13 * sin(s.phase * 2.2)
        let r = 20 * pulse
        // Tentacles
        ctx.setStrokeColor(CGColor(red: 0.95, green: 0.65, blue: 0.6, alpha: 0.75))
        ctx.setLineWidth(1.8)
        for i in 0..<4 {
            let bx = CGFloat(i - 2) * 9 + 4
            ctx.move(to: CGPoint(x: bx, y: -r * 0.7))
            var py = -r * 0.7
            var px = bx
            for k in 1...4 {
                py -= 9
                px = bx + sin(s.phase * 2 + CGFloat(k) + CGFloat(i)) * 4
                ctx.addLine(to: CGPoint(x: px, y: py))
            }
            ctx.strokePath()
        }
        // Macrocycle: the real 18-crown-6 scallop, an oxygen notch at twelve
        // o'clock, swaying gently instead of spinning.
        ctx.setStrokeColor(crownRed)
        ctx.setLineWidth(3.6)
        ctx.setLineJoin(.round)
        let sway: CGFloat = .pi / 2 + 0.18 * sin(s.phase * 0.7)
        let oxygens = addCrownPath(ctx, radius: r, rotation: sway)
        ctx.strokePath()
        ctx.setFillColor(CGColor(red: 1, green: 0.85, blue: 0.8, alpha: 1))
        for p in oxygens {
            ctx.fillEllipse(in: CGRect(x: p.x - 2.8, y: p.y - 2.8,
                                       width: 5.6, height: 5.6))
        }
        // The potassium passenger
        ctx.setFillColor(CGColor(red: 0.6, green: 0.4, blue: 0.85, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: -7, y: -7, width: 14, height: 14))
        drawTinyLabel(ctx, "K\u{207A}", at: .zero, size: 8,
                      color: NSColor(calibratedWhite: 1, alpha: 0.95), halo: false)
    }

    /// Molecular trefoil knot: the strand weaves over and under itself, drawn
    /// with depth-sorted segments so the crossings come out right, tumbling
    /// slowly as it drifts.
    private func drawTrefoil(_ ctx: CGContext, _ s: Swimmer) {
        ctx.saveGState()
        ctx.rotate(by: s.phase * 0.35)
        let R: CGFloat = 12.5
        let n = 72
        var pts: [(p: CGPoint, z: CGFloat)] = []
        for i in 0...n {
            let a = CGFloat(i) / CGFloat(n) * 2 * .pi
            pts.append((CGPoint(x: (sin(a) + 2 * sin(2 * a)) * R,
                                y: (cos(a) - 2 * cos(2 * a)) * R),
                        sin(3 * a)))
        }
        var order = Array(0..<n)
        order.sort { pts[$0].z + pts[$0 + 1].z < pts[$1].z + pts[$1 + 1].z }
        let shadow = CGColor(red: 0.01, green: 0.07, blue: 0.13, alpha: 1)
        for i in order {
            let depth = (pts[i].z + pts[i + 1].z) / 2
            let bright = 0.6 + 0.4 * (depth + 1) / 2
            ctx.setStrokeColor(shadow)
            ctx.setLineWidth(8.5)
            ctx.setLineCap(.round)
            ctx.move(to: pts[i].p); ctx.addLine(to: pts[i + 1].p); ctx.strokePath()
            ctx.setStrokeColor(CGColor(red: 0.95 * bright, green: 0.78 * bright,
                                       blue: 0.28 * bright, alpha: 1))
            ctx.setLineWidth(5)
            ctx.move(to: pts[i].p); ctx.addLine(to: pts[i + 1].p); ctx.strokePath()
        }
        ctx.restoreGState()
    }

    /// [c2]Daisy-chain eel: threaded ring-and-rod units undulating along.
    private func drawDaisyChain(_ ctx: CGContext, _ s: Swimmer) {
        ctx.saveGState()
        ctx.scaleBy(x: s.dir, y: 1)
        let units = 4
        let spacing: CGFloat = 36
        func unitCenter(_ u: Int) -> CGPoint {
            CGPoint(x: CGFloat(u) * spacing - spacing * CGFloat(units - 1) / 2,
                    y: sin(s.phase * 1.6 + CGFloat(u) * 0.95) * 9)
        }
        // Rods first, threading unit to unit, with end stoppers
        ctx.setStrokeColor(CGColor(red: 0.8, green: 0.84, blue: 0.9, alpha: 0.95))
        ctx.setLineWidth(3)
        ctx.setLineCap(.round)
        let head = unitCenter(0), tail = unitCenter(units - 1)
        ctx.move(to: CGPoint(x: head.x - 20, y: head.y))
        ctx.addLine(to: head)
        ctx.strokePath()
        for u in 0..<(units - 1) {
            ctx.move(to: unitCenter(u)); ctx.addLine(to: unitCenter(u + 1))
            ctx.strokePath()
        }
        ctx.move(to: tail)
        ctx.addLine(to: CGPoint(x: tail.x + 20, y: tail.y))
        ctx.strokePath()
        let gray = CGColor(red: 0.62, green: 0.66, blue: 0.74, alpha: 1)
        ctx.setFillColor(gray)
        ctx.fillEllipse(in: CGRect(x: head.x - 27, y: head.y - 7, width: 14, height: 14))
        ctx.fillEllipse(in: CGRect(x: tail.x + 13, y: tail.y - 7, width: 14, height: 14))
        // Rings over the rods: threaded
        for u in 0..<units {
            let c = unitCenter(u)
            ctx.setStrokeColor(u % 2 == 0 ? boxBlue : crownRed)
            ctx.setLineWidth(4.5)
            if u % 2 == 0 {
                ctx.stroke(CGRect(x: c.x - 9, y: c.y - 12, width: 18, height: 24)
                    .insetBy(dx: 2.25, dy: 2.25))
            } else {
                ctx.strokeEllipse(in: CGRect(x: c.x - 11, y: c.y - 13, width: 22, height: 26))
            }
        }
        ctx.restoreGState()
    }

    /// Cucurbituril: the pumpkin-shaped barrel, carbonyl-lined portals top and
    /// bottom, with a shy guest inside.
    private func drawCucurbituril(_ ctx: CGContext, _ s: Swimmer) {
        let teal = CGColor(red: 0.35, green: 0.62, blue: 0.62, alpha: 1)
        // Barrel body: bulging staves
        ctx.setFillColor(CGColor(red: 0.10, green: 0.24, blue: 0.27, alpha: 0.92))
        let body = CGMutablePath()
        body.move(to: CGPoint(x: -13, y: 16))
        body.addQuadCurve(to: CGPoint(x: -13, y: -16), control: CGPoint(x: -24, y: 0))
        body.addLine(to: CGPoint(x: 13, y: -16))
        body.addQuadCurve(to: CGPoint(x: 13, y: 16), control: CGPoint(x: 24, y: 0))
        body.closeSubpath()
        ctx.addPath(body)
        ctx.fillPath()
        // The guest, peeking out of the cavity
        ctx.setFillColor(CGColor(red: 0.6, green: 0.64, blue: 0.7, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: -6, y: 4 + sin(s.phase * 2) * 4, width: 12, height: 12))
        // Glycoluril staves
        ctx.setStrokeColor(teal)
        ctx.setLineWidth(2)
        for sx: CGFloat in [-14, -5, 5, 14] {
            ctx.move(to: CGPoint(x: sx * 0.8, y: 15))
            ctx.addQuadCurve(to: CGPoint(x: sx * 0.8, y: -15),
                             control: CGPoint(x: sx * 1.45, y: 0))
            ctx.strokePath()
        }
        ctx.addPath(body)
        ctx.setLineWidth(2.6)
        ctx.strokePath()
        // Carbonyl portals: red oxygens rimming both openings
        ctx.setFillColor(CGColor(red: 0.95, green: 0.4, blue: 0.32, alpha: 1))
        for dy: CGFloat in [16, -16] {
            for sx: CGFloat in [-11, -4.5, 2, 8.5] {
                ctx.fillEllipse(in: CGRect(x: sx, y: dy - 2.4, width: 4.8, height: 4.8))
            }
        }
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { StoddartReefView(frame: $0, isPreview: false)! }
#endif
