import ScreenSaver
import simd

// Schlenk Pipes — the Windows "3D Pipes" saver rebuilt as an air-free
// plumbing nightmare: glass tubing grows across the screen joint by joint,
// sprouting ground-glass collars, stopcocks, manifold take-offs, and the
// occasional condenser coil; runs retire into Schlenk flasks, oil bubblers,
// and cold traps, until the hood is full and everything gets flushed.

private typealias V3 = SIMD3<Double>

private func rnd(_ r: ClosedRange<Double>) -> Double { Double.random(in: r) }

private struct Camera {
    var eye = V3(0, 0, 10)
    var fwd = V3(0, 0, -1)
    var right = V3(1, 0, 0)
    var up = V3(0, 1, 0)
    var fl: Double = 800
    var cx: Double = 0, cy: Double = 0

    init(target: V3, azimuth: Double, elevation: Double, radius: Double,
         viewW: Double, viewH: Double) {
        eye = target + V3(radius * cos(elevation) * cos(azimuth),
                          radius * sin(elevation),
                          radius * cos(elevation) * sin(azimuth))
        fwd = simd_normalize(target - eye)
        right = simd_normalize(simd_cross(fwd, V3(0, 1, 0)))
        up = simd_cross(right, fwd)
        fl = viewH * 1.25
        cx = viewW / 2
        cy = viewH / 2
    }

    func project(_ p: V3) -> (pt: CGPoint, z: Double) {
        let d = p - eye
        let z = simd_dot(d, fwd)
        let zc = max(z, 0.6)
        let x = simd_dot(d, right)
        let y = simd_dot(d, up)
        return (CGPoint(x: cx + fl * x / zc, y: cy + fl * y / zc), z)
    }
}

private struct DrawItem {
    // 0 tube, 1 ball joint, 2 ground-glass collar, 3 condenser,
    // 4 clamped ball, 5 cold trap, 6 stopcock, 7 Schlenk flask, 8 bubbler,
    // 9 manifold take-off with flask
    var kind: Int
    var depth: Double
    var p1 = CGPoint.zero
    var p2 = CGPoint.zero
    var w: Double = 0
    var color: Int = 0
    var coil: [CGPoint] = []
}

private struct Pipe {
    var head: SIMD3<Int>
    var dir: SIMD3<Int>
    var color: Int
    var progress: Double = 0
    var alive = true
}

@objc(GlasswarePipesView)
public final class GlasswarePipesView: ScreenSaverView {

    private let nx = 13, ny = 9, nz = 13
    private var occupied: Set<Int> = []
    private var items: [DrawItem] = []
    private var needSort = false
    private var pipes: [Pipe] = []
    private var camera = Camera(target: .zero, azimuth: 0, elevation: 0.3,
                                radius: 14, viewW: 1280, viewH: 720)
    private var deadStarts = 0
    private var fade: Double = -1  // >=0 means flushing
    private var tick = 0

    private let tints: [(Double, Double, Double)] = [
        (0.88, 0.93, 0.97),  // borosilicate clear
        (0.85, 0.62, 0.25),  // amber
        (0.45, 0.75, 0.95),  // cool blue
        (0.55, 0.86, 0.62),  // green
        (0.9, 0.55, 0.75),   // rhodamine pink
    ]
    private let keckColors: [CGColor] = [
        CGColor(red: 0.15, green: 0.45, blue: 0.95, alpha: 1),  // keck blue
        CGColor(red: 0.2, green: 0.8, blue: 0.35, alpha: 1),    // keck green
        CGColor(red: 1.0, green: 0.8, blue: 0.15, alpha: 1),    // keck yellow
        CGColor(red: 0.95, green: 0.45, blue: 0.15, alpha: 1),  // keck orange
    ]
    private let dirs: [SIMD3<Int>] = [
        SIMD3(1, 0, 0), SIMD3(-1, 0, 0),
        SIMD3(0, 1, 0), SIMD3(0, -1, 0),
        SIMD3(0, 0, 1), SIMD3(0, 0, -1),
    ]

    public override init?(frame: NSRect, isPreview: Bool) {
        super.init(frame: frame, isPreview: isPreview)
        animationTimeInterval = 1.0 / 30.0
        resetScene()
    }

    public required init?(coder: NSCoder) {
        super.init(coder: coder)
        animationTimeInterval = 1.0 / 30.0
        resetScene()
    }

    private func cellIndex(_ c: SIMD3<Int>) -> Int { (c.x * ny + c.y) * nz + c.z }
    private func inBounds(_ c: SIMD3<Int>) -> Bool {
        c.x >= 0 && c.x < nx && c.y >= 0 && c.y < ny && c.z >= 0 && c.z < nz
    }
    private func world(_ c: SIMD3<Int>) -> V3 { V3(Double(c.x), Double(c.y), Double(c.z)) }

    private var speedMul = 1.0
    private var fancyOn = true

    private func resetScene() {
        speedMul = max(0.3, min(3, AfterDork.value("GlasswarePipes", "speed", 1.0)))
        // Settings key kept as "alembics" for compatibility; it now gates
        // all the fancy glassware.
        fancyOn = AfterDork.flag("GlasswarePipes", "alembics", true)
        occupied.removeAll()
        items.removeAll()
        pipes.removeAll()
        deadStarts = 0
        fade = -1
        let target = V3(Double(nx - 1) / 2, Double(ny - 1) / 2, Double(nz - 1) / 2)
        camera = Camera(target: target,
                        azimuth: rnd(0...(2 * .pi)),
                        elevation: rnd(0.18...0.5),
                        radius: Double(max(nx, nz)) * 1.35,
                        viewW: Double(max(bounds.width, 640)),
                        viewH: Double(max(bounds.height, 400)))
        for _ in 0..<3 { spawnPipe() }
    }

    private func spawnPipe() {
        for _ in 0..<40 {
            let c = SIMD3(Int.random(in: 0..<nx), Int.random(in: 0..<ny), Int.random(in: 0..<nz))
            if !occupied.contains(cellIndex(c)) {
                occupied.insert(cellIndex(c))
                pipes.append(Pipe(head: c, dir: dirs.randomElement()!,
                                  color: Int.random(in: 0..<tints.count)))
                return
            }
        }
        deadStarts = 99  // grid effectively full
    }

    private func chooseDir(from c: SIMD3<Int>, current: SIMD3<Int>) -> SIMD3<Int>? {
        let straight = c &+ current
        if inBounds(straight), !occupied.contains(cellIndex(straight)), rnd(0...1) < 0.55 {
            return current
        }
        let options = dirs.filter { d in
            let n = c &+ d
            return inBounds(n) && !occupied.contains(cellIndex(n))
        }
        return options.randomElement()
    }

    public override func animateOneFrame() {
        tick += 1
        if fade >= 0 {
            fade += 1.0 / 45.0
            if fade >= 1 { resetScene() }
            needsDisplay = true
            return
        }
        for i in pipes.indices where pipes[i].alive {
            pipes[i].progress += 0.2 * speedMul
            if pipes[i].progress >= 1 {
                pipes[i].progress = 0
                finishSegment(&pipes[i])
            }
        }
        pipes.removeAll { !$0.alive }
        while pipes.count < 3 && deadStarts < 12 { spawnPipe() }
        let full = Double(occupied.count) / Double(nx * ny * nz)
        if full > 0.62 || deadStarts >= 12 { fade = 0 }
        if needSort {
            items.sort { $0.depth > $1.depth }
            needSort = false
        }
        needsDisplay = true
    }

    private func finishSegment(_ p: inout Pipe) {
        let from = p.head
        let to = from &+ p.dir
        occupied.insert(cellIndex(to))
        addTube(from: world(from), to: world(to), color: p.color)
        p.head = to
        guard let nd = chooseDir(from: to, current: p.dir) else {
            // Stuck: retire the pipe into a piece of end glassware.
            addTerminator(at: world(to), color: p.color)
            p.alive = false
            deadStarts += 1
            return
        }
        if fancyOn && rnd(0...1) < 0.03 {
            // Deliberate retirement: the run ends in fancy glassware.
            addTerminator(at: world(to), color: p.color, fancyOnly: true)
            p.alive = false
            return
        }
        if nd != p.dir {
            // Elbow ball joint, some held by a pinch clamp.
            addBall(at: world(to), color: p.color, r: 0.34, clamped: rnd(0...1) < 0.3)
        } else {
            // Hardware on a straight run: ground-glass joint, stopcock, or —
            // on horizontal runs — a manifold take-off with a flask plumbed in.
            let roll = rnd(0...1)
            if roll < 0.10 {
                addCollar(at: world(to), dir: nd)
            } else if fancyOn, roll < 0.18 {
                addStopcock(at: world(to), dir: nd)
            } else if fancyOn && nd.y == 0 && roll < 0.26 {
                addManifold(at: world(to), dir: nd)
            }
        }
        p.dir = nd
    }

    /// End-of-run glassware: Schlenk flask, oil bubbler, cold trap on a
    /// vacuum pump, or a plain ball-joint cap.
    private func addTerminator(at p: V3, color: Int, fancyOnly: Bool = false) {
        guard fancyOn else {
            addBall(at: p, color: color, r: 0.30, clamped: false)
            return
        }
        let roll = rnd(0...1)
        if roll < 0.30 {
            addGlass(kind: 7, at: p, color: color)
        } else if roll < 0.55 {
            addGlass(kind: 8, at: p, color: color)
        } else if roll < 0.80 || fancyOnly {
            addGlass(kind: 5, at: p, color: color)
        } else {
            addBall(at: p, color: color, r: 0.30, clamped: false)
        }
    }

    // MARK: - Display list

    private func addTube(from a: V3, to b: V3, color: Int) {
        let (p1, z1) = camera.project(a)
        let (p2, z2) = camera.project(b)
        let zm = (z1 + z2) / 2
        var item = DrawItem(kind: 0, depth: zm, p1: p1, p2: p2,
                            w: 0.30 * camera.fl / max(zm, 0.6), color: color)
        if abs(a.y - b.y) < 0.01 && rnd(0...1) < 0.07 {
            // Horizontal run becomes a condenser: precompute the coil.
            item.kind = 3
            let axis = b - a
            var perp1 = simd_cross(axis, V3(0, 1, 0))
            if simd_length(perp1) < 0.01 { perp1 = V3(1, 0, 0) }
            perp1 = simd_normalize(perp1)
            let perp2 = simd_normalize(simd_cross(axis, perp1))
            var coil: [CGPoint] = []
            let turns = 5.0
            for s in stride(from: 0.0, through: 1.0, by: 1.0 / 40.0) {
                let angle = s * turns * 2 * .pi
                let p = a + axis * s + perp1 * (0.26 * cos(angle)) + perp2 * (0.26 * sin(angle))
                coil.append(camera.project(p).pt)
            }
            item.coil = coil
        }
        items.append(item)
        needSort = true
    }

    private func addBall(at p: V3, color: Int, r: Double, clamped: Bool) {
        let (pt, z) = camera.project(p)
        items.append(DrawItem(kind: clamped ? 4 : 1, depth: z - 0.01, p1: pt,
                              w: r * 2 * camera.fl / max(z, 0.6), color: color))
        needSort = true
    }

    private func addManifold(at p: V3, dir: SIMD3<Int>) {
        let d = V3(Double(dir.x), Double(dir.y), Double(dir.z)) * 0.5
        let (p1, z1) = camera.project(p - d)
        let (p2, z2) = camera.project(p + d)
        let zm = (z1 + z2) / 2 - 0.015
        items.append(DrawItem(kind: 9, depth: zm, p1: p1, p2: p2,
                              w: 0.30 * camera.fl / max(zm, 0.6),
                              color: Int.random(in: 0..<tints.count)))
        needSort = true
    }

    private func addGlass(kind: Int, at p: V3, color: Int) {
        let (pt, z) = camera.project(p)
        items.append(DrawItem(kind: kind, depth: z - 0.02, p1: pt,
                              w: 0.5 * camera.fl / max(z, 0.6), color: color))
        needSort = true
    }

    private func addStopcock(at p: V3, dir: SIMD3<Int>) {
        let d = V3(Double(dir.x), Double(dir.y), Double(dir.z)) * 0.3
        let (p1, z1) = camera.project(p - d)
        let (p2, z2) = camera.project(p + d)
        let zm = (z1 + z2) / 2 - 0.015
        items.append(DrawItem(kind: 6, depth: zm, p1: p1, p2: p2,
                              w: 0.30 * camera.fl / max(zm, 0.6), color: 0))
        needSort = true
    }

    private func addCollar(at p: V3, dir: SIMD3<Int>) {
        let d = V3(Double(dir.x), Double(dir.y), Double(dir.z)) * 0.22
        let (p1, z1) = camera.project(p - d)
        let (p2, z2) = camera.project(p + d)
        let zm = (z1 + z2) / 2 - 0.01
        // color doubles as the Keck clip color for collars
        items.append(DrawItem(kind: 2, depth: zm, p1: p1, p2: p2,
                              w: 0.42 * camera.fl / max(zm, 0.6),
                              color: Int.random(in: 0..<keckColors.count)))
        needSort = true
    }

    // MARK: - Drawing

    public override func draw(_ rect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        // Dark fume-hood interior
        let colors = [CGColor(red: 0.05, green: 0.06, blue: 0.09, alpha: 1),
                      CGColor(red: 0.01, green: 0.015, blue: 0.03, alpha: 1)]
        if let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(),
                                 colors: colors as CFArray, locations: [0, 1]) {
            ctx.drawRadialGradient(grad,
                                   startCenter: CGPoint(x: bounds.midX, y: bounds.midY),
                                   startRadius: 0,
                                   endCenter: CGPoint(x: bounds.midX, y: bounds.midY),
                                   endRadius: max(bounds.width, bounds.height) * 0.75,
                                   options: [.drawsAfterEndLocation])
        }
        ctx.setLineCap(.round)
        for item in items { drawItem(ctx, item) }
        for p in pipes where p.alive && p.progress > 0.02 {
            let a = world(p.head)
            let b = a + V3(Double(p.dir.x), Double(p.dir.y), Double(p.dir.z)) * p.progress
            let (p1, z1) = camera.project(a)
            let (p2, z2) = camera.project(b)
            let zm = (z1 + z2) / 2
            drawCapsule(ctx, p1, p2, w: 0.30 * camera.fl / max(zm, 0.6),
                        color: p.color, depth: zm)
        }
        if fade >= 0 {
            ctx.setFillColor(CGColor(red: 0.01, green: 0.015, blue: 0.03,
                                     alpha: min(fade * 1.2, 1)))
            ctx.fill(bounds)
        }
    }

    private func shade(_ color: Int, _ mul: Double, alpha: Double, depth: Double) -> CGColor {
        let t = tints[color]
        let dim = max(0.35, min(1.0, 1.35 - depth / 26.0))  // depth cueing
        return CGColor(red: t.0 * mul * dim, green: t.1 * mul * dim,
                       blue: t.2 * mul * dim, alpha: alpha)
    }

    private func drawCapsule(_ ctx: CGContext, _ p1: CGPoint, _ p2: CGPoint,
                             w: Double, color: Int, depth: Double) {
        // Dark rim, bright core, specular streak: cheap fake cylinder shading.
        ctx.setStrokeColor(shade(color, 0.42, alpha: 1, depth: depth))
        ctx.setLineWidth(w)
        ctx.move(to: p1); ctx.addLine(to: p2); ctx.strokePath()
        ctx.setStrokeColor(shade(color, 0.85, alpha: 1, depth: depth))
        ctx.setLineWidth(w * 0.66)
        ctx.move(to: p1); ctx.addLine(to: p2); ctx.strokePath()
        // Specular highlight offset toward the light (up-left).
        var dx = p2.x - p1.x, dy = p2.y - p1.y
        let len = max(sqrt(dx * dx + dy * dy), 0.001)
        dx /= len; dy /= len
        var ox = -dy, oy = dx
        if ox * (-0.5) + oy * 0.86 < 0 { ox = -ox; oy = -oy }
        let off = w * 0.2
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.5))
        ctx.setLineWidth(max(w * 0.16, 0.8))
        ctx.move(to: CGPoint(x: p1.x + ox * off, y: p1.y + oy * off))
        ctx.addLine(to: CGPoint(x: p2.x + ox * off, y: p2.y + oy * off))
        ctx.strokePath()
    }

    private func drawItem(_ ctx: CGContext, _ item: DrawItem) {
        switch item.kind {
        case 0:
            drawCapsule(ctx, item.p1, item.p2, w: item.w, color: item.color, depth: item.depth)
        case 1, 4:
            let r = item.w / 2
            let rect = CGRect(x: item.p1.x - r, y: item.p1.y - r, width: 2 * r, height: 2 * r)
            ctx.setFillColor(shade(item.color, 0.55, alpha: 1, depth: item.depth))
            ctx.fillEllipse(in: rect)
            ctx.setFillColor(shade(item.color, 0.95, alpha: 1, depth: item.depth))
            ctx.fillEllipse(in: rect.insetBy(dx: r * 0.25, dy: r * 0.25)
                                .offsetBy(dx: -r * 0.1, dy: r * 0.1))
            ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.6))
            let hr = r * 0.22
            ctx.fillEllipse(in: CGRect(x: item.p1.x - r * 0.38 - hr,
                                       y: item.p1.y + r * 0.38 - hr,
                                       width: 2 * hr, height: 2 * hr))
            if item.kind == 4, r > 5 { drawPinchClamp(ctx, at: item.p1, r: r) }
        case 2:
            // Frosted ground-glass collar
            ctx.setStrokeColor(CGColor(red: 0.92, green: 0.94, blue: 0.96, alpha: 0.85))
            ctx.setLineWidth(item.w)
            ctx.move(to: item.p1); ctx.addLine(to: item.p2); ctx.strokePath()
            ctx.setStrokeColor(CGColor(red: 0.6, green: 0.64, blue: 0.7, alpha: 0.8))
            ctx.setLineWidth(item.w * 0.15)
            ctx.move(to: item.p1); ctx.addLine(to: item.p2); ctx.strokePath()
            drawKeckClip(ctx, item)
        case 3:
            // Condenser: outer glass envelope, coil, inner tube.
            ctx.setStrokeColor(CGColor(red: 0.75, green: 0.85, blue: 0.95, alpha: 0.28))
            ctx.setLineWidth(item.w * 2.1)
            ctx.move(to: item.p1); ctx.addLine(to: item.p2); ctx.strokePath()
            if item.coil.count > 1 {
                ctx.setStrokeColor(shade(item.color, 0.9, alpha: 0.9, depth: item.depth))
                ctx.setLineWidth(max(item.w * 0.22, 1))
                ctx.move(to: item.coil[0])
                for p in item.coil.dropFirst() { ctx.addLine(to: p) }
                ctx.strokePath()
            }
            drawCapsule(ctx, item.p1, item.p2, w: item.w * 0.55,
                        color: item.color, depth: item.depth)
        case 5:
            drawColdTrap(ctx, at: item.p1, s: item.w, color: item.color,
                         depth: item.depth)
        case 6:
            drawStopcock(ctx, item)
        case 7:
            drawSchlenkFlask(ctx, at: item.p1, s: item.w, color: item.color,
                             depth: item.depth)
        case 8:
            drawBubbler(ctx, at: item.p1, s: item.w, color: item.color,
                        depth: item.depth)
        case 9:
            drawManifold(ctx, item)
        default:
            break
        }
    }

    /// Cold trap: the line drops into a glass cold finger sunk in a silver
    /// dewar of liquid nitrogen, side-armed over to a little vacuum pump.
    /// Vapor curls off the rim.
    private func drawColdTrap(_ ctx: CGContext, at p: CGPoint, s: Double,
                              color: Int, depth: Double) {
        ctx.setLineCap(.round)
        // Side arm out over the dewar, then down to the pump inlet
        let armY = p.y - s * 0.4
        // The whole pump assembly buzzes about a pixel — it's running.
        let vib = s * 0.025
        let pumpC = CGPoint(
            x: p.x + s * 1.55 + vib * sin(Double(tick) * 1.9 + Double(p.x)),
            y: p.y - s * 1.7 + vib * cos(Double(tick) * 2.3 + Double(p.x)))
        ctx.setStrokeColor(shade(color, 0.45, alpha: 1, depth: depth))
        ctx.setLineWidth(max(s * 0.22, 1.6))
        ctx.move(to: CGPoint(x: p.x, y: armY))
        ctx.addLine(to: CGPoint(x: pumpC.x, y: armY))
        ctx.addLine(to: CGPoint(x: pumpC.x, y: pumpC.y + s * 0.35))
        ctx.strokePath()
        ctx.setStrokeColor(shade(color, 0.9, alpha: 1, depth: depth))
        ctx.setLineWidth(max(s * 0.12, 1))
        ctx.move(to: CGPoint(x: p.x, y: armY))
        ctx.addLine(to: CGPoint(x: pumpC.x, y: armY))
        ctx.addLine(to: CGPoint(x: pumpC.x, y: pumpC.y + s * 0.35))
        ctx.strokePath()
        // Neck down from the line
        ctx.setLineWidth(max(s * 0.3, 2))
        ctx.move(to: p)
        ctx.addLine(to: CGPoint(x: p.x, y: p.y - s * 1.0))
        ctx.strokePath()
        // The cold finger proper: a fatter glass trap body that visibly
        // pokes out of the dewar mouth before sinking in.
        ctx.setStrokeColor(shade(color, 0.45, alpha: 1, depth: depth))
        ctx.setLineWidth(s * 0.58)
        ctx.move(to: CGPoint(x: p.x, y: p.y - s * 0.9))
        ctx.addLine(to: CGPoint(x: p.x, y: p.y - s * 1.8))
        ctx.strokePath()
        ctx.setStrokeColor(shade(color, 0.85, alpha: 1, depth: depth))
        ctx.setLineWidth(s * 0.4)
        ctx.move(to: CGPoint(x: p.x, y: p.y - s * 0.9))
        ctx.addLine(to: CGPoint(x: p.x, y: p.y - s * 1.8))
        ctx.strokePath()
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.5))
        ctx.setLineWidth(max(s * 0.09, 0.8))
        ctx.move(to: CGPoint(x: p.x - s * 0.14, y: p.y - s * 0.95))
        ctx.addLine(to: CGPoint(x: p.x - s * 0.14, y: p.y - s * 1.3))
        ctx.strokePath()
        // Silver dewar swallowing the bottom of the finger: a tall narrow
        // cup — noticeably longer than it is wide — with a flat open rim.
        let rimY = p.y - s * 1.35
        let botY = p.y - s * 2.5
        let halfW = s * 0.42
        let cup = CGMutablePath()
        cup.move(to: CGPoint(x: p.x - halfW, y: rimY))
        cup.addLine(to: CGPoint(x: p.x - halfW, y: botY + s * 0.3))
        cup.addQuadCurve(to: CGPoint(x: p.x, y: botY),
                         control: CGPoint(x: p.x - halfW, y: botY))
        cup.addQuadCurve(to: CGPoint(x: p.x + halfW, y: botY + s * 0.3),
                         control: CGPoint(x: p.x + halfW, y: botY))
        cup.addLine(to: CGPoint(x: p.x + halfW, y: rimY))
        cup.closeSubpath()
        ctx.addPath(cup)
        ctx.setFillColor(CGColor(red: 0.62, green: 0.66, blue: 0.73, alpha: 1))
        ctx.fillPath()
        ctx.addPath(cup)
        ctx.setStrokeColor(CGColor(red: 0.38, green: 0.40, blue: 0.46, alpha: 1))
        ctx.setLineWidth(max(s * 0.09, 1))
        ctx.strokePath()
        // Rim lip
        ctx.setStrokeColor(CGColor(red: 0.80, green: 0.83, blue: 0.88, alpha: 1))
        ctx.setLineWidth(max(s * 0.14, 1.2))
        ctx.move(to: CGPoint(x: p.x - halfW - s * 0.06, y: rimY))
        ctx.addLine(to: CGPoint(x: p.x + halfW + s * 0.06, y: rimY))
        ctx.strokePath()
        // Highlight streak on the wall
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.55))
        ctx.setLineWidth(max(s * 0.12, 1))
        ctx.move(to: CGPoint(x: p.x - s * 0.26, y: rimY - s * 0.2))
        ctx.addLine(to: CGPoint(x: p.x - s * 0.26, y: botY + s * 0.3))
        ctx.strokePath()
        // Steam pouring off the rim: wisps rising, spreading, thinning out
        for k in 0..<4 {
            let f = (Double(tick) * (0.007 + Double(k) * 0.0017)
                     + Double(k) * 0.27 + Double(p.x) * 0.002)
                .truncatingRemainder(dividingBy: 1)
            let side = k % 2 == 0 ? 1.0 : -1.0
            let vx = p.x + side * s * (0.28 + 0.38 * f
                                       + 0.06 * sin(f * 10 + Double(k) * 2))
            let vy = rimY + s * (0.1 + f * 1.0)
            let vw = s * (0.14 + 0.14 * f)
            ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1,
                                     alpha: 0.45 * (1 - f)))
            ctx.fillEllipse(in: CGRect(x: vx - vw, y: vy - vw * 0.4,
                                       width: vw * 2, height: vw * 0.8))
        }
        // The vacuum pump, seen from the side: pump head under the hose
        // barb, finned motor cylinder behind it, feet, oil sight glass.
        let headDark = CGColor(red: 0.16, green: 0.17, blue: 0.20, alpha: 1)
        let motorGray = CGColor(red: 0.26, green: 0.28, blue: 0.33, alpha: 1)
        let outline = CGColor(red: 0.45, green: 0.47, blue: 0.53, alpha: 1)
        let head = CGRect(x: pumpC.x - s * 0.26, y: pumpC.y - s * 0.32,
                          width: s * 0.52, height: s * 0.64)
        let motor = CGRect(x: pumpC.x + s * 0.24, y: pumpC.y - s * 0.24,
                           width: s * 0.75, height: s * 0.48)
        // Feet first, so the body sits on them
        ctx.setFillColor(headDark)
        for fx in [head.minX + s * 0.06, motor.maxX - s * 0.16] {
            ctx.fill(CGRect(x: fx, y: head.minY - s * 0.1,
                            width: s * 0.12, height: s * 0.12))
        }
        // Motor with cooling fins
        ctx.setFillColor(motorGray)
        ctx.addPath(CGPath(roundedRect: motor, cornerWidth: s * 0.08,
                           cornerHeight: s * 0.08, transform: nil))
        ctx.fillPath()
        ctx.setStrokeColor(headDark)
        ctx.setLineWidth(max(s * 0.045, 0.6))
        for i in 1...4 {
            let fx = motor.minX + motor.width * Double(i) / 5
            ctx.move(to: CGPoint(x: fx, y: motor.minY + s * 0.05))
            ctx.addLine(to: CGPoint(x: fx, y: motor.maxY - s * 0.05))
            ctx.strokePath()
        }
        // Pump head, hose barb on top, oil sight glass low on the side
        ctx.setFillColor(headDark)
        ctx.addPath(CGPath(roundedRect: head, cornerWidth: s * 0.06,
                           cornerHeight: s * 0.06, transform: nil))
        ctx.fillPath()
        ctx.setStrokeColor(outline)
        ctx.setLineWidth(max(s * 0.05, 0.7))
        ctx.addPath(CGPath(roundedRect: head, cornerWidth: s * 0.06,
                           cornerHeight: s * 0.06, transform: nil))
        ctx.strokePath()
        ctx.setStrokeColor(headDark)
        ctx.setLineWidth(max(s * 0.16, 1.2))
        ctx.move(to: CGPoint(x: pumpC.x, y: head.maxY))
        ctx.addLine(to: CGPoint(x: pumpC.x, y: head.maxY + s * 0.14))
        ctx.strokePath()
        ctx.setFillColor(CGColor(red: 0.85, green: 0.62, blue: 0.25, alpha: 1))
        let og = max(s * 0.06, 0.9)
        ctx.fillEllipse(in: CGRect(x: head.maxX - s * 0.16 - og,
                                   y: head.minY + s * 0.14 - og,
                                   width: 2 * og, height: 2 * og))
        // Power lamp on the motor end
        ctx.setFillColor(CGColor(red: 0.3, green: 0.95, blue: 0.4, alpha: 1))
        let lr = max(s * 0.04, 0.7)
        ctx.fillEllipse(in: CGRect(x: motor.maxX - s * 0.1 - lr,
                                   y: motor.maxY - s * 0.1 - lr,
                                   width: 2 * lr, height: 2 * lr))
    }

    /// Manifold take-off on a straight run: a stubby down-tube with its own
    /// stopcock and a round-bottom flask plumbed in underneath, stir bar
    /// going — the line moonlighting as a Schlenk manifold.
    private func drawManifold(_ ctx: CGContext, _ item: DrawItem) {
        guard item.w > 5 else { return }
        let dx = item.p2.x - item.p1.x, dy = item.p2.y - item.p1.y
        let len = max(hypot(dx, dy), 0.001)
        let u = CGVector(dx: dx / len, dy: dy / len)
        var v = CGVector(dx: -u.dy, dy: u.dx)
        if v.dy > 0 { v = CGVector(dx: -v.dx, dy: -v.dy) }  // flask hangs down
        let pc = CGPoint(x: (item.p1.x + item.p2.x) / 2,
                         y: (item.p1.y + item.p2.y) / 2)
        let w = item.w
        func at(_ a: Double, _ b: Double) -> CGPoint {
            CGPoint(x: pc.x + u.dx * a + v.dx * b, y: pc.y + u.dy * a + v.dy * b)
        }
        ctx.setLineCap(.round)
        // Take-off stub (clear glass)
        ctx.setStrokeColor(shade(0, 0.42, alpha: 1, depth: item.depth))
        ctx.setLineWidth(w * 0.6)
        ctx.move(to: at(0, w * 0.3)); ctx.addLine(to: at(0, w * 1.7))
        ctx.strokePath()
        ctx.setStrokeColor(shade(0, 0.85, alpha: 1, depth: item.depth))
        ctx.setLineWidth(w * 0.4)
        ctx.move(to: at(0, w * 0.3)); ctx.addLine(to: at(0, w * 1.7))
        ctx.strokePath()
        // Stopcock on the stub
        let red = CGColor(red: 0.82, green: 0.18, blue: 0.14, alpha: 1)
        ctx.setStrokeColor(red)
        ctx.setLineWidth(max(w * 0.2, 1.4))
        ctx.move(to: at(0, w * 0.95)); ctx.addLine(to: at(w * 0.75, w * 0.95))
        ctx.strokePath()
        ctx.setLineWidth(max(w * 0.26, 1.7))
        ctx.move(to: at(w * 0.75, w * 0.62)); ctx.addLine(to: at(w * 0.75, w * 1.28))
        ctx.strokePath()
        // Frosted joint where the flask hangs on
        ctx.setStrokeColor(CGColor(red: 0.92, green: 0.94, blue: 0.96, alpha: 0.85))
        ctx.setLineWidth(w * 0.7)
        ctx.move(to: at(0, w * 1.45)); ctx.addLine(to: at(0, w * 1.75))
        ctx.strokePath()
        // Round-bottom flask with contents and a spinning stir bar
        let fc = at(0, w * 2.55)
        let r = w * 0.95
        let rect = CGRect(x: fc.x - r, y: fc.y - r, width: 2 * r, height: 2 * r)
        ctx.setFillColor(shade(0, 0.75, alpha: 0.96, depth: item.depth))
        ctx.fillEllipse(in: rect)
        ctx.saveGState()
        ctx.addEllipse(in: rect)
        ctx.clip()
        ctx.setFillColor(shade(item.color, 1.0, alpha: 0.95, depth: item.depth))
        ctx.fill(CGRect(x: rect.minX, y: rect.minY,
                        width: rect.width, height: r * 0.95))
        let spin = cos(Double(tick) * 0.3 + Double(pc.x) * 0.07)
        let half = r * 0.45 * max(abs(spin), 0.18)
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.92))
        ctx.setLineWidth(max(r * 0.17, 1.2))
        ctx.move(to: CGPoint(x: fc.x - half, y: fc.y - r * 0.6))
        ctx.addLine(to: CGPoint(x: fc.x + half, y: fc.y - r * 0.6))
        ctx.strokePath()
        ctx.restoreGState()
        ctx.addEllipse(in: rect)
        ctx.setStrokeColor(shade(0, 0.4, alpha: 1, depth: item.depth))
        ctx.setLineWidth(max(r * 0.12, 1))
        ctx.strokePath()
        // Glint
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.5))
        ctx.setLineWidth(max(r * 0.09, 0.8))
        ctx.addArc(center: fc, radius: r * 0.72,
                   startAngle: 2.2, endAngle: 3.0, clockwise: false)
        ctx.strokePath()
    }

    /// Glass stopcock plugged through a straight run: frosted barrel across
    /// the tube, red plug stem sticking out with a T-grip — a little piece of
    /// Schlenk manifold.
    private func drawStopcock(_ ctx: CGContext, _ item: DrawItem) {
        guard item.w > 4 else { return }
        let dx = item.p2.x - item.p1.x, dy = item.p2.y - item.p1.y
        let len = max(hypot(dx, dy), 0.001)
        let u = CGVector(dx: dx / len, dy: dy / len)
        var v = CGVector(dx: -u.dy, dy: u.dx)
        if v.dy < 0 { v = CGVector(dx: -v.dx, dy: -v.dy) }  // handle points up
        let pc = CGPoint(x: (item.p1.x + item.p2.x) / 2,
                         y: (item.p1.y + item.p2.y) / 2)
        let w = item.w
        func at(_ a: Double, _ b: Double) -> CGPoint {
            CGPoint(x: pc.x + u.dx * a + v.dx * b, y: pc.y + u.dy * a + v.dy * b)
        }
        ctx.setLineCap(.round)
        // Frosted barrel crossing the tube
        ctx.setStrokeColor(CGColor(red: 0.92, green: 0.94, blue: 0.96, alpha: 0.9))
        ctx.setLineWidth(w * 0.55)
        ctx.move(to: at(0, -w * 0.75)); ctx.addLine(to: at(0, w * 0.75))
        ctx.strokePath()
        // Red plug stem and T-grip
        let red = CGColor(red: 0.82, green: 0.18, blue: 0.14, alpha: 1)
        ctx.setStrokeColor(red)
        ctx.setLineWidth(max(w * 0.22, 1.5))
        ctx.move(to: at(0, w * 0.7)); ctx.addLine(to: at(0, w * 1.35))
        ctx.strokePath()
        ctx.setLineWidth(max(w * 0.3, 2))
        ctx.move(to: at(-w * 0.5, w * 1.35)); ctx.addLine(to: at(w * 0.5, w * 1.35))
        ctx.strokePath()
        // Glint on the barrel
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.5))
        ctx.setLineWidth(max(w * 0.12, 0.8))
        ctx.move(to: at(-w * 0.1, -w * 0.4)); ctx.addLine(to: at(-w * 0.1, w * 0.4))
        ctx.strokePath()
    }

    /// Schlenk flask hanging off the end of a run: frosted ground joint,
    /// neck, round bulb with solvent and a spinning stir bar, and the
    /// signature sidearm stopcock.
    private func drawSchlenkFlask(_ ctx: CGContext, at p: CGPoint, s: Double,
                                  color: Int, depth: Double) {
        let bulbC = CGPoint(x: p.x, y: p.y - s * 1.35)
        let bulbR = s
        let neckW = max(s * 0.34, 2)
        ctx.setLineCap(.round)
        // Neck down from the pipe
        ctx.setStrokeColor(shade(color, 0.6, alpha: 0.95, depth: depth))
        ctx.setLineWidth(neckW)
        ctx.move(to: p)
        ctx.addLine(to: CGPoint(x: bulbC.x, y: bulbC.y + bulbR * 0.8))
        ctx.strokePath()
        // Sidearm angling off the neck, with its own little red stopcock
        let armBase = CGPoint(x: p.x, y: p.y - s * 0.45)
        let armEnd = CGPoint(x: p.x + s * 1.15, y: p.y - s * 0.1)
        ctx.setLineWidth(neckW * 0.7)
        ctx.move(to: armBase); ctx.addLine(to: armEnd); ctx.strokePath()
        let red = CGColor(red: 0.82, green: 0.18, blue: 0.14, alpha: 1)
        let armMid = CGPoint(x: (armBase.x + armEnd.x) / 2,
                             y: (armBase.y + armEnd.y) / 2)
        let plugTop = CGPoint(x: armMid.x + s * 0.18, y: armMid.y + s * 0.5)
        ctx.setStrokeColor(red)
        ctx.setLineWidth(max(s * 0.14, 1.2))
        ctx.move(to: armMid); ctx.addLine(to: plugTop); ctx.strokePath()
        ctx.setLineWidth(max(s * 0.18, 1.6))
        ctx.move(to: CGPoint(x: plugTop.x - s * 0.28, y: plugTop.y - s * 0.06))
        ctx.addLine(to: CGPoint(x: plugTop.x + s * 0.28, y: plugTop.y + s * 0.06))
        ctx.strokePath()
        // Bulb
        let rect = CGRect(x: bulbC.x - bulbR, y: bulbC.y - bulbR,
                          width: bulbR * 2, height: bulbR * 2)
        ctx.setFillColor(shade(color, 0.75, alpha: 0.96, depth: depth))
        ctx.fillEllipse(in: rect)
        // Solvent pooling, stir bar whirling (its apparent length breathes)
        ctx.saveGState()
        ctx.addEllipse(in: rect)
        ctx.clip()
        ctx.setFillColor(shade((color + 2) % tints.count, 1.0, alpha: 0.95,
                               depth: depth))
        ctx.fill(CGRect(x: rect.minX, y: rect.minY,
                        width: rect.width, height: bulbR * 0.9))
        let spin = cos(Double(tick) * 0.35 + Double(p.x) * 0.05)
        let half = s * 0.42 * max(abs(spin), 0.18)
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.92))
        ctx.setLineWidth(max(s * 0.16, 1.4))
        ctx.move(to: CGPoint(x: bulbC.x - half, y: bulbC.y - bulbR * 0.62))
        ctx.addLine(to: CGPoint(x: bulbC.x + half, y: bulbC.y - bulbR * 0.62))
        ctx.strokePath()
        ctx.restoreGState()
        // Outline
        ctx.addEllipse(in: rect)
        ctx.setStrokeColor(shade(color, 0.4, alpha: 1, depth: depth))
        ctx.setLineWidth(max(s * 0.12, 1.2))
        ctx.strokePath()
        // Frosted ground joint where flask meets pipe
        ctx.setStrokeColor(CGColor(red: 0.92, green: 0.94, blue: 0.96, alpha: 0.85))
        ctx.setLineWidth(neckW * 1.6)
        ctx.move(to: CGPoint(x: p.x, y: p.y - s * 0.02))
        ctx.addLine(to: CGPoint(x: p.x, y: p.y - s * 0.3))
        ctx.strokePath()
        // Glint on the bulb
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.55))
        ctx.setLineWidth(max(s * 0.09, 1))
        ctx.addArc(center: bulbC, radius: bulbR * 0.72,
                   startAngle: 2.2, endAngle: 3.0, clockwise: false)
        ctx.strokePath()
    }

    /// Mineral-oil bubbler: the run's dip tube plunges into a little vessel
    /// of amber oil and burps a steady stream of bubbles — the Schlenk
    /// line's exhaust.
    private func drawBubbler(_ ctx: CGContext, at p: CGPoint, s: Double,
                             color: Int, depth: Double) {
        let topY = p.y - s * 0.35
        let botY = p.y - s * 1.75
        let halfW = s * 0.52
        ctx.setLineCap(.round)
        // Glass vessel envelope
        ctx.setStrokeColor(CGColor(red: 0.75, green: 0.85, blue: 0.95, alpha: 0.30))
        ctx.setLineWidth(halfW * 2)
        ctx.move(to: CGPoint(x: p.x, y: topY))
        ctx.addLine(to: CGPoint(x: p.x, y: botY))
        ctx.strokePath()
        // Oil sitting in the bottom
        ctx.setStrokeColor(CGColor(red: 0.85, green: 0.62, blue: 0.25, alpha: 0.85))
        ctx.setLineWidth(halfW * 1.7)
        ctx.move(to: CGPoint(x: p.x, y: botY + s * 0.72))
        ctx.addLine(to: CGPoint(x: p.x, y: botY + s * 0.12))
        ctx.strokePath()
        // Dip tube from the line down into the oil
        ctx.setStrokeColor(shade(color, 0.85, alpha: 1, depth: depth))
        ctx.setLineWidth(max(s * 0.16, 1.2))
        ctx.move(to: p)
        ctx.addLine(to: CGPoint(x: p.x, y: botY + s * 0.22))
        ctx.strokePath()
        // Bubbles rising off the dip tube outlet
        if s > 5 {
            for k in 0..<3 {
                let f = (Double(tick) * (0.011 + Double(k) * 0.002)
                         + Double(k) * 0.37 + Double(p.x) * 0.004)
                    .truncatingRemainder(dividingBy: 1)
                let by = botY + s * 0.2 + f * s * 1.15
                let bx = p.x + s * (0.16 + 0.09 * sin(f * 14 + Double(k) * 2))
                let br = s * (0.055 + 0.045 * f)
                ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1,
                                         alpha: 0.75 * (1 - f * 0.4)))
                ctx.fillEllipse(in: CGRect(x: bx - br, y: by - br,
                                           width: br * 2, height: br * 2))
            }
        }
        // Rim glint
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.4))
        ctx.setLineWidth(max(s * 0.08, 1))
        ctx.move(to: CGPoint(x: p.x - halfW * 0.7, y: topY + halfW))
        ctx.addLine(to: CGPoint(x: p.x + halfW * 0.7, y: topY + halfW))
        ctx.strokePath()
    }

    /// Colored plastic Keck clip straddling a ground-glass joint: a spine
    /// along one side of the tube with two fork arms wrapping across it.
    private func drawKeckClip(_ ctx: CGContext, _ item: DrawItem) {
        let dx = item.p2.x - item.p1.x, dy = item.p2.y - item.p1.y
        let len = max(hypot(dx, dy), 0.001)
        guard item.w > 6 else { return }
        let u = CGVector(dx: dx / len, dy: dy / len)
        let v = CGVector(dx: -u.dy, dy: u.dx)
        let pc = CGPoint(x: (item.p1.x + item.p2.x) / 2, y: (item.p1.y + item.p2.y) / 2)
        let r = item.w * 0.62
        let d = len * 0.58
        let lw = max(item.w * 0.22, 1.5)
        let color = keckColors[item.color % keckColors.count]
        func pt(_ alongAxis: Double, _ acrossAxis: Double) -> CGPoint {
            CGPoint(x: pc.x + u.dx * alongAxis + v.dx * acrossAxis,
                    y: pc.y + u.dy * alongAxis + v.dy * acrossAxis)
        }
        ctx.setStrokeColor(color)
        ctx.setLineWidth(lw)
        ctx.setLineCap(.round)
        // Spine along one side
        ctx.move(to: pt(-d, r))
        ctx.addLine(to: pt(d, r))
        ctx.strokePath()
        // Fork arms wrapping across the joint
        for s in [-d, d] {
            ctx.move(to: pt(s, r))
            ctx.addLine(to: pt(s * 1.12, -r * 0.95))
            ctx.strokePath()
        }
        // Highlight on the spine
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.55))
        ctx.setLineWidth(lw * 0.35)
        ctx.move(to: pt(-d * 0.7, r * 1.12))
        ctx.addLine(to: pt(d * 0.7, r * 1.12))
        ctx.strokePath()
    }

    /// Metal pinch clamp gripping a ball joint: a horseshoe jaw hugging the
    /// lower half of the ball, with a screw stem and wing nut attached below.
    private func drawPinchClamp(_ ctx: CGContext, at p: CGPoint, r: Double) {
        let metal = CGColor(red: 0.60, green: 0.63, blue: 0.69, alpha: 1)
        let jr = r * 1.02
        let lw = max(r * 0.30, 2.0)
        ctx.setLineCap(.round)
        // Jaw wraps the bottom of the ball, opening upward for the tubing.
        ctx.setStrokeColor(metal)
        ctx.setLineWidth(lw)
        ctx.addArc(center: p, radius: jr,
                   startAngle: .pi * 1.13, endAngle: .pi * 1.87, clockwise: false)
        ctx.strokePath()
        // Gripping tabs at the jaw tips
        for a in [Double.pi * 1.13, Double.pi * 1.87] {
            let tip = CGPoint(x: p.x + jr * cos(a), y: p.y + jr * sin(a))
            ctx.move(to: tip)
            ctx.addLine(to: CGPoint(x: p.x + (jr + r * 0.34) * cos(a),
                                    y: p.y + (jr + r * 0.34) * sin(a)))
            ctx.strokePath()
        }
        // Screw stem straight off the jaw, then a wing nut.
        let jawBottom = p.y - jr
        ctx.setLineWidth(max(r * 0.18, 1.4))
        ctx.move(to: CGPoint(x: p.x, y: jawBottom))
        ctx.addLine(to: CGPoint(x: p.x, y: jawBottom - r * 0.55))
        ctx.strokePath()
        let nutY = jawBottom - r * 0.62
        ctx.setFillColor(metal)
        for sx in [-1.0, 1.0] {
            ctx.fillEllipse(in: CGRect(x: p.x + sx * r * 0.08 - (sx < 0 ? r * 0.42 : 0),
                                       y: nutY - r * 0.16,
                                       width: r * 0.42, height: r * 0.32))
        }
        // Specular hint on the jaw
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.5))
        ctx.setLineWidth(lw * 0.3)
        ctx.addArc(center: p, radius: jr,
                   startAngle: .pi * 1.35, endAngle: .pi * 1.65, clockwise: false)
        ctx.strokePath()
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { GlasswarePipesView(frame: $0, isPreview: false)! }
#endif
