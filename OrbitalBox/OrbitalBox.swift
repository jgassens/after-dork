import ScreenSaver
import simd

// Orbital Box — the Windows "3D Flower Box" morphing cube, except the shape
// morphs through atomic orbital geometries: 1s, 2p, 3dz2, 3dx2-y2, 4fz3,
// rendered as the classic chunky low-poly surface in the six saturated
// face colors, spinning and bouncing around the screen.

private typealias V3 = SIMD3<Double>

@objc(OrbitalBoxView)
public final class OrbitalBoxView: ScreenSaverView {

    // Real spherical-harmonic magnitudes, normalized to a max of 1.
    private let shapes: [(name: String, f: (Double, Double) -> Double)] = [
        ("1s", { _, _ in 1 }),
        ("2p", { th, _ in abs(cos(th)) }),
        ("3dz\u{B2}", { th, _ in abs(1.5 * cos(th) * cos(th) - 0.5) }),
        ("3dx\u{B2}\u{2013}y\u{B2}", { th, ph in
            let s = sin(th)
            return s * s * abs(cos(2 * ph))
        }),
        ("4fz\u{B3}", { th, _ in
            let c = cos(th)
            return abs(2.5 * c * c * c - 1.5 * c)
        }),
    ]

    private let nTheta = 18, nPhi = 26
    private let holdFrames = 80, morphFrames = 66

    private var tick = 0
    private var shapeIdx = 0
    private var rotX = 0.4, rotY = 0.0
    private var cx: CGFloat = 400, cy: CGFloat = 300
    private var vx: CGFloat = 1.4, vy: CGFloat = 1.0

    // The Flower Box palette: one saturated color per face direction.
    private let palette: [CGColor] = [
        CGColor(red: 0.92, green: 0.15, blue: 0.15, alpha: 1),  // +x red
        CGColor(red: 0.10, green: 0.85, blue: 0.90, alpha: 1),  // -x cyan
        CGColor(red: 0.95, green: 0.85, blue: 0.10, alpha: 1),  // +y yellow
        CGColor(red: 0.20, green: 0.30, blue: 0.95, alpha: 1),  // -y blue
        CGColor(red: 0.15, green: 0.85, blue: 0.25, alpha: 1),  // +z green
        CGColor(red: 0.90, green: 0.20, blue: 0.85, alpha: 1),  // -z magenta
    ]

    public override init?(frame: NSRect, isPreview: Bool) {
        super.init(frame: frame, isPreview: isPreview)
        animationTimeInterval = 1.0 / 30.0
        cx = max(bounds.midX, 200)
        cy = max(bounds.midY, 150)
    }

    public required init?(coder: NSCoder) {
        super.init(coder: coder)
        animationTimeInterval = 1.0 / 30.0
    }

    public override func animateOneFrame() {
        tick += 1
        rotX += 0.011
        rotY += 0.019
        let w = max(bounds.width, 640), h = max(bounds.height, 400)
        let R = min(w, h) * 0.29
        cx += vx; cy += vy
        if cx < R { cx = R; vx = abs(vx) }
        if cx > w - R { cx = w - R; vx = -abs(vx) }
        if cy < R { cy = R; vy = abs(vy) }
        if cy > h - R { cy = h - R; vy = -abs(vy) }
        if tick >= holdFrames + morphFrames {
            tick = 0
            shapeIdx = (shapeIdx + 1) % shapes.count
        }
        needsDisplay = true
    }

    public override func draw(_ rect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 1))
        ctx.fill(bounds)

        // Morph blend between the current shape and the next
        let next = (shapeIdx + 1) % shapes.count
        var u = 0.0
        if tick > holdFrames {
            let raw = Double(tick - holdFrames) / Double(morphFrames)
            u = raw * raw * (3 - 2 * raw)  // smoothstep
        }
        let fA = shapes[shapeIdx].f, fB = shapes[next].f

        // Build the mesh in model space, remembering model-space directions
        // so the face colors are painted onto the shape and rotate with it.
        let cxr = cos(rotX), sxr = sin(rotX)
        let cyr = cos(rotY), syr = sin(rotY)
        func rotate(_ v: V3) -> V3 {
            let y1 = v.y * cxr - v.z * sxr
            let z1 = v.y * sxr + v.z * cxr
            let x2 = v.x * cyr + z1 * syr
            let z2 = -v.x * syr + z1 * cyr
            return V3(x2, y1, z2)
        }

        var verts = [[V3]]()
        var dirs = [[V3]]()
        for i in 0...nTheta {
            let th = Double(i) / Double(nTheta) * .pi
            var row = [V3]()
            var dRow = [V3]()
            for j in 0..<nPhi {
                let ph = Double(j) / Double(nPhi) * 2 * .pi
                let mag = (1 - u) * fA(th, ph) + u * fB(th, ph)
                let r = 0.18 + 0.82 * mag
                let dir = V3(sin(th) * cos(ph), cos(th), sin(th) * sin(ph))
                row.append(rotate(dir * r))
                dRow.append(dir)
            }
            verts.append(row)
            dirs.append(dRow)
        }

        let R = Double(min(max(bounds.width, 640), max(bounds.height, 400))) * 0.29
        let D = 3.3
        let light = simd_normalize(V3(0.35, 0.55, 0.75))
        let half = simd_normalize(light + V3(0, 0, 1))

        struct Quad {
            var pts: [CGPoint]
            var depth: Double
            var color: Int
            var shade: Double
            var spec: Double
        }
        var quads: [Quad] = []
        quads.reserveCapacity(nTheta * nPhi)

        for i in 0..<nTheta {
            for j in 0..<nPhi {
                let j2 = (j + 1) % nPhi
                let v = [verts[i][j], verts[i][j2], verts[i + 1][j2], verts[i + 1][j]]
                let center = (v[0] + v[1] + v[2] + v[3]) * 0.25
                var n = simd_cross(v[2] - v[0], v[3] - v[1])
                let nl = simd_length(n)
                if nl < 1e-9 { continue }
                n /= nl
                if simd_dot(n, center) < 0 { n = -n }
                let mdir = (dirs[i][j] + dirs[i][j2] + dirs[i + 1][j2] + dirs[i + 1][j]) * 0.25
                let ax = abs(mdir.x), ay = abs(mdir.y), az = abs(mdir.z)
                let color: Int
                if ax >= ay && ax >= az { color = mdir.x >= 0 ? 0 : 1 }
                else if ay >= ax && ay >= az { color = mdir.y >= 0 ? 2 : 3 }
                else { color = mdir.z >= 0 ? 4 : 5 }
                let shade = 0.32 + 0.68 * max(0, simd_dot(n, light))
                let spec = pow(max(0, simd_dot(n, half)), 20)
                let pts = v.map { p -> CGPoint in
                    let denom = D - p.z
                    return CGPoint(x: cx + CGFloat(p.x * R * 2.5 / denom),
                                   y: cy + CGFloat(p.y * R * 2.5 / denom))
                }
                quads.append(Quad(pts: pts, depth: center.z, color: color,
                                  shade: shade, spec: spec))
            }
        }
        quads.sort { $0.depth < $1.depth }

        for q in quads {
            let base = palette[q.color]
            let comps = base.components ?? [1, 1, 1, 1]
            let r = min(comps[0] * q.shade + q.spec * 0.9, 1)
            let g = min(comps[1] * q.shade + q.spec * 0.9, 1)
            let b = min(comps[2] * q.shade + q.spec * 0.9, 1)
            ctx.setFillColor(CGColor(red: r, green: g, blue: b, alpha: 1))
            ctx.move(to: q.pts[0])
            for p in q.pts.dropFirst() { ctx.addLine(to: p) }
            ctx.closePath()
            ctx.fillPath()
        }

        // Corner caption naming the current orbital
        let name = u < 0.5 ? shapes[shapeIdx].name : shapes[next].name
        let alpha = tick <= holdFrames ? 0.6 : 0.6 * abs(1 - 2 * u)
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedSystemFont(ofSize: 17, weight: .semibold),
            .foregroundColor: NSColor(calibratedWhite: 0.8, alpha: alpha),
        ]
        NSAttributedString(string: name, attributes: attrs)
            .draw(at: CGPoint(x: 26, y: 22))
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { OrbitalBoxView(frame: $0, isPreview: false)! }
#endif
