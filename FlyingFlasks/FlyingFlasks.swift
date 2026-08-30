import ScreenSaver

// Flying Flasks — an After Dark "Flying Toasters" homage for chemists.
// Winged Erlenmeyer flasks flap across a starfield, accompanied by drifting
// NMR tubes (the toast) and round-bottom flasks on bat wings, stir bars
// still going — the reaction waits for no one.

private func rnd(_ r: ClosedRange<CGFloat>) -> CGFloat { CGFloat.random(in: r) }

private struct Star {
    var x: CGFloat, y: CGFloat
    var size: CGFloat
    var base: CGFloat
    var phase: CGFloat
    var rate: CGFloat
}

private enum FlyerKind { case erlenmeyer, roundBottom, nmrTube }

private struct Drop {
    var x: CGFloat, y: CGFloat
    var vx: CGFloat, vy: CGFloat
    var hue: CGFloat
    var size: CGFloat
    var life: Int
}

private struct Flyer {
    var kind: FlyerKind
    var x: CGFloat, y: CGFloat
    var speed: CGFloat
    var scale: CGFloat
    var flapPhase: CGFloat
    var flapRate: CGFloat
    var hue: CGFloat
    var tilt: CGFloat
    var capColor: Int
}

@objc(FlyingFlasksView)
public final class FlyingFlasksView: ScreenSaverView {

    private var stars: [Star] = []
    private var flyers: [Flyer] = []
    private var drops: [Drop] = []
    private var dripsEnabled = true
    private var t: CGFloat = 0
    // Classic toaster heading: down and to the left.
    private let flightDir = CGVector(dx: -0.868, dy: -0.496)
    private let capColors: [NSColor] = [
        NSColor(calibratedRed: 0.85, green: 0.20, blue: 0.22, alpha: 1),
        NSColor(calibratedRed: 0.20, green: 0.45, blue: 0.90, alpha: 1),
        NSColor(calibratedRed: 0.25, green: 0.72, blue: 0.35, alpha: 1),
        NSColor(calibratedRed: 0.92, green: 0.78, blue: 0.20, alpha: 1),
    ]

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
        stars = (0..<110).map { _ in
            Star(x: rnd(0...w), y: rnd(0...h),
                 size: rnd(1.0...2.6),
                 base: rnd(0.25...0.95),
                 phase: rnd(0...6.28),
                 rate: rnd(0.6...2.4))
        }
        let flock = Int(AfterDork.value("FlyingFlasks", "flock", 15))
        dripsEnabled = AfterDork.flag("FlyingFlasks", "drips", true)
        let count = isPreview ? min(8, flock) : max(4, min(40, flock))
        flyers = (0..<count).map { _ in makeFlyer(initial: true) }
        flyers.sort { $0.scale < $1.scale }
    }

    private func makeFlyer(initial: Bool) -> Flyer {
        let w = max(bounds.width, 640), h = max(bounds.height, 400)
        let roll = CGFloat.random(in: 0...1)
        let kind: FlyerKind = roll < 0.48 ? .erlenmeyer : (roll < 0.82 ? .nmrTube : .roundBottom)
        var f = Flyer(kind: kind,
                      x: 0, y: 0,
                      speed: rnd(2.0...4.2),
                      scale: rnd(0.55...1.25),
                      flapPhase: rnd(0...6.28),
                      flapRate: rnd(10...15),
                      hue: rnd(0...1),
                      tilt: kind == .nmrTube ? rnd(-0.35...0.05) : 0,
                      capColor: Int.random(in: 0..<4))
        if kind == .nmrTube { f.speed *= 0.62 }        // toast drifts slower
        if kind == .roundBottom { f.flapRate *= 0.6 }  // lazier bat flaps
        if initial {
            f.x = rnd(-100...w + 100)
            f.y = rnd(-50...h + 50)
        } else {
            respawn(&f)
        }
        return f
    }

    private func respawn(_ f: inout Flyer) {
        let w = max(bounds.width, 640), h = max(bounds.height, 400)
        let m = 110 * f.scale
        // Spawn along the top edge (extended to the right) or the right edge.
        if CGFloat.random(in: 0...1) < 0.62 {
            f.x = rnd(0...(w + 500))
            f.y = h + m
        } else {
            f.x = w + m
            f.y = rnd(0...(h + 200))
        }
    }

    public override func animateOneFrame() {
        t += 1.0 / 30.0
        for i in flyers.indices {
            let v = flyers[i].speed * (0.5 + flyers[i].scale)
            flyers[i].x += flightDir.dx * v
            flyers[i].y += flightDir.dy * v
            flyers[i].flapPhase += flyers[i].flapRate / 30.0
            let m = 130 * flyers[i].scale
            if flyers[i].x < -m || flyers[i].y < -m {
                var f = makeFlyer(initial: false)
                f.scale = flyers[i].scale  // keep depth slot so draw order stays valid
                flyers[i] = f
            }
            // Open glassware sloshes: a drop escapes the mouth now and then.
            // NMR tubes are capped and drip nothing, obviously.
            let f = flyers[i]
            if dripsEnabled, f.kind != .nmrTube, drops.count < 60, CGFloat.random(in: 0...1) < 0.02 {
                let v2 = f.speed * (0.5 + f.scale)
                drops.append(Drop(x: f.x + rnd(-4...4) * f.scale,
                                  y: f.y + 31 * f.scale,
                                  vx: flightDir.dx * v2 * 0.5 + rnd(-0.4...0.4),
                                  vy: rnd(0.5...1.6),
                                  hue: f.hue,
                                  size: rnd(2.0...3.2) * f.scale,
                                  life: 110))
            }
        }
        for i in drops.indices {
            drops[i].x += drops[i].vx
            drops[i].y += drops[i].vy
            drops[i].vy -= 0.16  // gravity
            drops[i].life -= 1
        }
        drops.removeAll { $0.life <= 0 || $0.y < -20 }
        needsDisplay = true
    }

    public override func draw(_ rect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        // Deep-space background
        ctx.setFillColor(CGColor(red: 0.012, green: 0.012, blue: 0.055, alpha: 1))
        ctx.fill(bounds)
        drawStars(ctx)
        drawDrops(ctx)
        for f in flyers { drawFlyer(ctx, f) }
    }

    private func drawDrops(_ ctx: CGContext) {
        for d in drops {
            let fade = min(CGFloat(d.life) / 30.0, 1)
            let color = NSColor(calibratedHue: d.hue, saturation: 0.8,
                                brightness: 0.9, alpha: 0.9 * fade)
            ctx.setFillColor(color.cgColor)
            // Teardrop: elongates as it picks up speed falling
            let stretch = min(1.0 + abs(d.vy) * 0.12, 1.8)
            ctx.fillEllipse(in: CGRect(x: d.x - d.size / 2,
                                       y: d.y - d.size * stretch / 2,
                                       width: d.size, height: d.size * stretch))
            ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.5 * fade))
            let hr = d.size * 0.18
            ctx.fillEllipse(in: CGRect(x: d.x - d.size * 0.15 - hr,
                                       y: d.y + d.size * 0.2 - hr,
                                       width: 2 * hr, height: 2 * hr))
        }
    }

    private func drawStars(_ ctx: CGContext) {
        for s in stars {
            let b = s.base * (0.55 + 0.45 * sin(t * s.rate + s.phase))
            ctx.setFillColor(CGColor(red: 0.9, green: 0.92, blue: 1.0, alpha: b))
            ctx.fillEllipse(in: CGRect(x: s.x, y: s.y, width: s.size, height: s.size))
        }
    }

    private func drawFlyer(_ ctx: CGContext, _ f: Flyer) {
        ctx.saveGState()
        ctx.translateBy(x: f.x, y: f.y)
        ctx.scaleBy(x: f.scale, y: f.scale)
        if f.tilt != 0 { ctx.rotate(by: f.tilt) }
        switch f.kind {
        case .erlenmeyer: drawErlenmeyer(ctx, f)
        case .roundBottom: drawRoundBottom(ctx, f)
        case .nmrTube: drawNMRTube(ctx, f)
        }
        ctx.restoreGState()
    }

    // MARK: - Wings

    private func featherWingPath() -> CGPath {
        // Wing pointing up and outward (+x is outboard); shoulder at origin.
        let p = CGMutablePath()
        p.move(to: .zero)
        p.addCurve(to: CGPoint(x: 30, y: 34),
                   control1: CGPoint(x: 8, y: 16),
                   control2: CGPoint(x: 24, y: 30))
        // Scalloped trailing edge back toward the shoulder
        p.addQuadCurve(to: CGPoint(x: 30, y: 18), control: CGPoint(x: 38, y: 26))
        p.addQuadCurve(to: CGPoint(x: 24, y: 7), control: CGPoint(x: 33, y: 11))
        p.addQuadCurve(to: CGPoint(x: 14, y: -1), control: CGPoint(x: 24, y: 0))
        p.addQuadCurve(to: .zero, control: CGPoint(x: 6, y: -3))
        p.closeSubpath()
        return p
    }

    private func batWingPath() -> CGPath {
        let p = CGMutablePath()
        p.move(to: .zero)
        p.addQuadCurve(to: CGPoint(x: 34, y: 30), control: CGPoint(x: 14, y: 26))
        // Scalloped membrane
        p.addQuadCurve(to: CGPoint(x: 26, y: 10), control: CGPoint(x: 34, y: 16))
        p.addQuadCurve(to: CGPoint(x: 15, y: 8), control: CGPoint(x: 20, y: 3))
        p.addQuadCurve(to: .zero, control: CGPoint(x: 6, y: 0))
        p.closeSubpath()
        return p
    }

    /// Draws both wings behind the body. `shoulder` is the +x attachment point.
    private func drawWings(_ ctx: CGContext, flap: CGFloat, shoulder: CGPoint, bat: Bool) {
        let path = bat ? batWingPath() : featherWingPath()
        let fill: CGColor = bat
            ? CGColor(red: 0.36, green: 0.18, blue: 0.48, alpha: 0.96)
            : CGColor(red: 0.97, green: 0.97, blue: 1.0, alpha: 0.97)
        let stroke: CGColor = bat
            ? CGColor(red: 0.75, green: 0.6, blue: 0.9, alpha: 0.9)
            : CGColor(red: 0.55, green: 0.58, blue: 0.7, alpha: 0.9)
        for side: CGFloat in [1, -1] {
            ctx.saveGState()
            ctx.scaleBy(x: side, y: 1)
            ctx.translateBy(x: shoulder.x, y: shoulder.y)
            ctx.rotate(by: flap)
            ctx.addPath(path)
            ctx.setFillColor(fill)
            ctx.fillPath()
            ctx.addPath(path)
            ctx.setStrokeColor(stroke)
            ctx.setLineWidth(1.4)
            ctx.strokePath()
            ctx.restoreGState()
        }
    }

    // MARK: - Bodies

    private func erlenmeyerBody() -> CGPath {
        let p = CGMutablePath()
        p.move(to: CGPoint(x: -20, y: -28))
        p.addLine(to: CGPoint(x: 20, y: -28))
        p.addLine(to: CGPoint(x: 7, y: 6))
        p.addLine(to: CGPoint(x: 7, y: 26))
        p.addLine(to: CGPoint(x: 9, y: 28))
        p.addLine(to: CGPoint(x: 9, y: 31))
        p.addLine(to: CGPoint(x: -9, y: 31))
        p.addLine(to: CGPoint(x: -9, y: 28))
        p.addLine(to: CGPoint(x: -7, y: 26))
        p.addLine(to: CGPoint(x: -7, y: 6))
        p.closeSubpath()
        return p
    }

    /// The After Dark toasters didn't glide — they snapped through a 4-pose
    /// mechanical flap cycle (up, mid, down, mid) with no smoothing.
    private func toasterFlap(_ phase: CGFloat) -> CGFloat {
        let poses: [CGFloat] = [0.95, 0.45, -0.22, 0.45]
        return poses[Int(phase / (.pi / 2)) % 4]
    }

    private func drawErlenmeyer(_ ctx: CGContext, _ f: Flyer) {
        let flap = toasterFlap(f.flapPhase)
        drawWings(ctx, flap: flap, shoulder: CGPoint(x: 11, y: 6), bat: false)

        let body = erlenmeyerBody()
        // Glass fill
        ctx.addPath(body)
        ctx.setFillColor(CGColor(red: 0.85, green: 0.9, blue: 1.0, alpha: 0.13))
        ctx.fillPath()
        // Liquid
        ctx.saveGState()
        ctx.addPath(body)
        ctx.clip()
        let liquid = NSColor(calibratedHue: f.hue, saturation: 0.75, brightness: 0.85, alpha: 0.92)
        ctx.setFillColor(liquid.cgColor)
        ctx.fill(CGRect(x: -22, y: -30, width: 44, height: 22))
        ctx.setFillColor(NSColor(calibratedHue: f.hue, saturation: 0.6, brightness: 1.0, alpha: 0.9).cgColor)
        ctx.fillEllipse(in: CGRect(x: -16, y: -10.5, width: 32, height: 5))
        ctx.restoreGState()
        // Outline + highlight
        ctx.addPath(body)
        ctx.setStrokeColor(CGColor(red: 0.92, green: 0.95, blue: 1.0, alpha: 0.9))
        ctx.setLineWidth(2)
        ctx.setLineJoin(.round)
        ctx.strokePath()
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.55))
        ctx.setLineWidth(1.6)
        ctx.move(to: CGPoint(x: -13, y: -22))
        ctx.addLine(to: CGPoint(x: -5, y: 0))
        ctx.strokePath()
    }

    private func drawRoundBottom(_ ctx: CGContext, _ f: Flyer) {
        let flap = toasterFlap(f.flapPhase)
        drawWings(ctx, flap: flap, shoulder: CGPoint(x: 13, y: 2), bat: true)

        let body = CGMutablePath()
        body.addEllipse(in: CGRect(x: -18, y: -24, width: 36, height: 36))
        body.addRect(CGRect(x: -5, y: 8, width: 10, height: 20))
        body.addRect(CGRect(x: -7, y: 28, width: 14, height: 3.5))

        ctx.addPath(body)
        ctx.setFillColor(CGColor(red: 0.85, green: 0.9, blue: 1.0, alpha: 0.13))
        ctx.fillPath()
        // Liquid inside the bulb
        ctx.saveGState()
        ctx.addEllipse(in: CGRect(x: -18, y: -24, width: 36, height: 36))
        ctx.clip()
        let liquid = NSColor(calibratedHue: f.hue, saturation: 0.75, brightness: 0.8, alpha: 0.92)
        ctx.setFillColor(liquid.cgColor)
        ctx.fill(CGRect(x: -18, y: -24, width: 36, height: 16))
        // Stir bar going, mid-flight: its apparent length breathes as it
        // whirls, seen side-on.
        let spin = cos(t * 9 + f.flapPhase)
        let half = 11 * max(abs(spin), 0.2)
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.95))
        ctx.setLineWidth(4)
        ctx.setLineCap(.round)
        ctx.move(to: CGPoint(x: -half, y: -17))
        ctx.addLine(to: CGPoint(x: half, y: -17))
        ctx.strokePath()
        // Vortex dimple in the surface while the bar spins
        ctx.setFillColor(CGColor(red: 0.012, green: 0.012, blue: 0.055, alpha: 0.35))
        ctx.fillEllipse(in: CGRect(x: -6, y: -10, width: 12, height: 4))
        ctx.restoreGState()
        // Outlines
        ctx.setStrokeColor(CGColor(red: 0.92, green: 0.95, blue: 1.0, alpha: 0.9))
        ctx.setLineWidth(2)
        ctx.strokeEllipse(in: CGRect(x: -18, y: -24, width: 36, height: 36))
        ctx.stroke(CGRect(x: -5, y: 8, width: 10, height: 20))
        ctx.stroke(CGRect(x: -7, y: 28, width: 14, height: 3.5))
        // Glass glint
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.55))
        ctx.setLineWidth(1.6)
        ctx.addArc(center: CGPoint(x: 0, y: -6), radius: 13,
                   startAngle: 2.4, endAngle: 3.4, clockwise: false)
        ctx.strokePath()
    }

    private func drawNMRTube(_ ctx: CGContext, _ f: Flyer) {
        let tube = CGPath(roundedRect: CGRect(x: -4, y: -30, width: 8, height: 58),
                          cornerWidth: 3, cornerHeight: 3, transform: nil)
        ctx.addPath(tube)
        ctx.setFillColor(CGColor(red: 0.85, green: 0.9, blue: 1.0, alpha: 0.14))
        ctx.fillPath()
        // Sample solution
        ctx.saveGState()
        ctx.addPath(tube)
        ctx.clip()
        ctx.setFillColor(NSColor(calibratedHue: f.hue, saturation: 0.8, brightness: 0.85, alpha: 0.95).cgColor)
        ctx.fill(CGRect(x: -4, y: -30, width: 8, height: 17))
        ctx.restoreGState()
        // Outline
        ctx.addPath(tube)
        ctx.setStrokeColor(CGColor(red: 0.92, green: 0.95, blue: 1.0, alpha: 0.85))
        ctx.setLineWidth(1.6)
        ctx.strokePath()
        // Colored cap
        let cap = capColors[f.capColor].cgColor
        ctx.setFillColor(cap)
        ctx.fill(CGRect(x: -5.5, y: 26, width: 11, height: 7))
        ctx.setStrokeColor(CGColor(red: 0, green: 0, blue: 0, alpha: 0.35))
        ctx.setLineWidth(1)
        ctx.stroke(CGRect(x: -5.5, y: 26, width: 11, height: 7))
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { FlyingFlasksView(frame: $0, isPreview: false)! }
#endif
