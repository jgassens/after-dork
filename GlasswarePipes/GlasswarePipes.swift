import ScreenSaver
import simd

// Glassware Pipes — the Windows "3D Pipes" saver rebuilt as a lab plumbing
// nightmare: glass tubing grows across the screen joint by joint, sprouting
// ground-glass collars, ball joints, and the occasional condenser coil, until
// the hood is full and everything gets flushed for a fresh setup.

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
    var kind: Int  // 0 tube, 1 ball joint, 2 ground-glass collar, 3 condenser
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

    private func resetScene() {
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
            pipes[i].progress += 0.2
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
            // Stuck: cap it with a ball joint and retire the pipe.
            addBall(at: world(to), color: p.color, r: 0.30)
            p.alive = false
            deadStarts += 1
            return
        }
        if nd != p.dir {
            addBall(at: world(to), color: p.color, r: 0.34)  // elbow ball joint
        } else if rnd(0...1) < 0.10 {
            addCollar(at: world(to), dir: nd)  // ground-glass joint on a straight run
        }
        p.dir = nd
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

    private func addBall(at p: V3, color: Int, r: Double) {
        let (pt, z) = camera.project(p)
        items.append(DrawItem(kind: 1, depth: z - 0.01, p1: pt,
                              w: r * 2 * camera.fl / max(z, 0.6), color: color))
        needSort = true
    }

    private func addCollar(at p: V3, dir: SIMD3<Int>) {
        let d = V3(Double(dir.x), Double(dir.y), Double(dir.z)) * 0.22
        let (p1, z1) = camera.project(p - d)
        let (p2, z2) = camera.project(p + d)
        let zm = (z1 + z2) / 2 - 0.01
        items.append(DrawItem(kind: 2, depth: zm, p1: p1, p2: p2,
                              w: 0.42 * camera.fl / max(zm, 0.6)))
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
        case 1:
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
        case 2:
            // Frosted ground-glass collar
            ctx.setStrokeColor(CGColor(red: 0.92, green: 0.94, blue: 0.96, alpha: 0.85))
            ctx.setLineWidth(item.w)
            ctx.move(to: item.p1); ctx.addLine(to: item.p2); ctx.strokePath()
            ctx.setStrokeColor(CGColor(red: 0.6, green: 0.64, blue: 0.7, alpha: 0.8))
            ctx.setLineWidth(item.w * 0.15)
            ctx.move(to: item.p1); ctx.addLine(to: item.p2); ctx.strokePath()
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
        default:
            break
        }
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { GlasswarePipesView(frame: $0, isPreview: false)! }
#endif
