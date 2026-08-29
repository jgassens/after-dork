import ScreenSaver

// Crystal Lattice Maze — the Windows 95 "3D Maze" saver, except you are a
// guest molecule diffusing through a metal-organic framework. Chunky software
// raycaster, procedural lattice wall textures, checkerboard floor, and instead
// of the smiley that flips you upside down, you bump into solvent molecules.

private func packRGB(_ r: Int, _ g: Int, _ b: Int) -> UInt32 {
    (0xFF << 24) | (UInt32(max(0, min(255, r))) << 16)
        | (UInt32(max(0, min(255, g))) << 8) | UInt32(max(0, min(255, b)))
}

private struct Molecule {
    var cx: Int, cy: Int
    var type: Int
    var phase: Double
}

@objc(LatticeMazeView)
public final class LatticeMazeView: ScreenSaverView {

    // Maze
    private let mw = 25, mh = 25
    private var map: [[Int]] = []

    // Camera
    private var posX = 1.5, posY = 1.5
    private var angle = 0.0
    private var path: [(Int, Int)] = []
    private var inverted = false
    private var flipT = -1.0  // >=0 while animating

    // Molecules (the smiley stand-ins)
    private var molecules: [Molecule] = []

    // Framebuffer
    private var iw = 420, ih = 236
    private var buffer: [UInt32] = []
    private var zbuf: [Double] = []

    // Textures
    private let ts = 64    // sprite textures
    private let wts = 128  // wall textures (bigger so ligand structures stay legible)
    private var wallTexA: [UInt32] = []
    private var wallTexB: [UInt32] = []
    private var spriteFrames: [[[UInt32]]] = []  // [type][frame][pixels], ARGB
    private let spriteFrameCount = 16

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
        iw = isPreview ? 220 : 440
        let aspect = bounds.width > 0 ? bounds.height / bounds.width : 0.5625
        ih = max(120, Int(Double(iw) * Double(aspect)))
        buffer = [UInt32](repeating: 0, count: iw * ih)
        zbuf = [Double](repeating: 1e9, count: iw)
        generateMaze()
        buildWallTextures()
        buildSprites()
        posX = 1.5; posY = 1.5
        angle = 0
        molecules = []
        for _ in 0..<8 { placeMolecule() }
    }

    // MARK: - Maze generation (recursive backtracker)

    private func generateMaze() {
        map = Array(repeating: Array(repeating: 1, count: mw), count: mh)
        var stack: [(Int, Int)] = [(1, 1)]
        map[1][1] = 0
        while let (cx, cy) = stack.last {
            let dirs = [(2, 0), (-2, 0), (0, 2), (0, -2)].shuffled()
            var carved = false
            for (dx, dy) in dirs {
                let nx = cx + dx, ny = cy + dy
                if nx > 0 && nx < mw - 1 && ny > 0 && ny < mh - 1 && map[ny][nx] == 1 {
                    map[ny][nx] = 0
                    map[cy + dy / 2][cx + dx / 2] = 0
                    stack.append((nx, ny))
                    carved = true
                    break
                }
            }
            if !carved { stack.removeLast() }
        }
        // Knock a few extra openings through so the crawl can loop.
        var extra = 0
        while extra < 14 {
            let x = Int.random(in: 1..<(mw - 1)), y = Int.random(in: 1..<(mh - 1))
            if map[y][x] == 1 && ((map[y][x - 1] == 0 && map[y][x + 1] == 0)
                || (map[y - 1][x] == 0 && map[y + 1][x] == 0)) {
                map[y][x] = 0
                extra += 1
            }
        }
    }

    private func openCells() -> [(Int, Int)] {
        var out: [(Int, Int)] = []
        for y in 0..<mh { for x in 0..<mw where map[y][x] == 0 { out.append((x, y)) } }
        return out
    }

    private func placeMolecule() {
        let cells = openCells().filter { c in
            abs(c.0 - Int(posX)) + abs(c.1 - Int(posY)) > 5
                && !molecules.contains { $0.cx == c.0 && $0.cy == c.1 }
        }
        if let c = cells.randomElement() {
            molecules.append(Molecule(cx: c.0, cy: c.1,
                                      type: Int.random(in: 0..<3),
                                      phase: Double.random(in: 0...6.28)))
        }
    }

    // MARK: - Textures

    private func buildWallTextures() {
        wallTexA = makeLatticeTexture(style: 0)  // ZIF-8: Zn + 2-methylimidazolate
        wallTexB = makeLatticeTexture(style: 1)  // UiO-66: Zr + terephthalate
    }

    /// Draws one wall tile as a framework fragment with an atomically correct
    /// bridging ligand connecting the diagonal metal nodes.
    /// Style 0 = ZIF-8 (2-methylimidazolate, N-Zn on both nitrogens),
    /// style 1 = UiO-66 (terephthalate, para carboxylates chelating Zr).
    private func makeLatticeTexture(style: Int) -> [UInt32] {
        let ctx = makeContext(size: wts)
        let S = CGFloat(wts)
        let c = CGPoint(x: S / 2, y: S / 2)
        let bg = CGColor(red: 0.045, green: 0.055, blue: 0.10, alpha: 1)
        let bond = CGColor(red: 0.88, green: 0.90, blue: 0.94, alpha: 1)
        let nBlue = NSColor(calibratedRed: 0.45, green: 0.63, blue: 1.0, alpha: 1)
        let oRed = NSColor(calibratedRed: 1.0, green: 0.38, blue: 0.32, alpha: 1)
        let steel: (CGFloat, CGFloat, CGFloat) = (0.72, 0.74, 0.80)

        ctx.setFillColor(bg)
        ctx.fill(CGRect(x: 0, y: 0, width: S, height: S))
        for _ in 0..<320 {
            ctx.setFillColor(CGColor(red: 0.085, green: 0.095, blue: 0.16, alpha: 1))
            ctx.fill(CGRect(x: rndT(0...S), y: rndT(0...S), width: 1.6, height: 1.6))
        }

        let nsCtx = NSGraphicsContext(cgContext: ctx, flipped: false)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = nsCtx

        func pt(_ center: CGPoint, _ r: CGFloat, _ deg: CGFloat) -> CGPoint {
            let a = deg * .pi / 180
            return CGPoint(x: center.x + r * cos(a), y: center.y + r * sin(a))
        }
        func line(_ a: CGPoint, _ b: CGPoint, _ w: CGFloat, _ color: CGColor) {
            ctx.setStrokeColor(color)
            ctx.setLineWidth(w)
            ctx.setLineCap(.round)
            ctx.move(to: a); ctx.addLine(to: b); ctx.strokePath()
        }
        // Second stroke of a double bond, offset toward the ring center.
        func innerBond(_ a: CGPoint, _ b: CGPoint, _ center: CGPoint, _ w: CGFloat) {
            let mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2
            let ox = (center.x - mx), oy = (center.y - my)
            let ol = max(sqrt(ox * ox + oy * oy), 0.001)
            let off: CGFloat = 4.5
            let dx = ox / ol * off, dy = oy / ol * off
            let a2 = CGPoint(x: a.x * 0.82 + b.x * 0.18 + dx, y: a.y * 0.82 + b.y * 0.18 + dy)
            let b2 = CGPoint(x: b.x * 0.82 + a.x * 0.18 + dx, y: b.y * 0.82 + a.y * 0.18 + dy)
            line(a2, b2, w, bond)
        }
        func atomLabel(_ s: String, _ p: CGPoint, _ color: NSColor, _ size: CGFloat) {
            let attrs: [NSAttributedString.Key: Any] = [
                .font: NSFont.boldSystemFont(ofSize: size),
                .foregroundColor: color,
            ]
            let str = NSAttributedString(string: s, attributes: attrs)
            let sz = str.size()
            ctx.setFillColor(bg)
            ctx.fillEllipse(in: CGRect(x: p.x - sz.width / 2 - 2, y: p.y - sz.height / 2 - 1,
                                       width: sz.width + 4, height: sz.height + 2))
            str.draw(at: CGPoint(x: p.x - sz.width / 2, y: p.y - sz.height / 2))
        }
        func metalBall(_ p: CGPoint, _ r: CGFloat) {
            ctx.setFillColor(CGColor(red: steel.0 * 0.4, green: steel.1 * 0.4,
                                     blue: steel.2 * 0.4, alpha: 1))
            ctx.fillEllipse(in: CGRect(x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r))
            ctx.setFillColor(CGColor(red: steel.0, green: steel.1, blue: steel.2, alpha: 1))
            let r2 = r * 0.72
            ctx.fillEllipse(in: CGRect(x: p.x - r2 - r * 0.1, y: p.y - r2 + r * 0.1,
                                       width: 2 * r2, height: 2 * r2))
            ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.7))
            let hr = r * 0.2
            ctx.fillEllipse(in: CGRect(x: p.x - r * 0.35 - hr, y: p.y + r * 0.35 - hr,
                                       width: 2 * hr, height: 2 * hr))
        }

        let corners = [CGPoint(x: 0, y: 0), CGPoint(x: S, y: 0),
                       CGPoint(x: S, y: S), CGPoint(x: 0, y: S)]
        // Faint framework edge along the tile border.
        let frame = CGColor(red: 0.22, green: 0.28, blue: 0.42, alpha: 1)
        for i in 0..<4 { line(corners[i], corners[(i + 1) % 4], 2.5, frame) }

        func toward(_ corner: CGPoint, from p: CGPoint, stopAt: CGFloat) -> CGPoint {
            let dx = p.x - corner.x, dy = p.y - corner.y
            let l = max(sqrt(dx * dx + dy * dy), 0.001)
            return CGPoint(x: corner.x + dx / l * stopAt, y: corner.y + dy / l * stopAt)
        }

        if style == 0 {
            // ZIF-8: 2-methylimidazolate bridging two Zn through N1 and N3.
            // Ring: N1-C2(-CH3)-N3-C4-C5; doubles drawn C2=N3, C4=C5.
            let r: CGFloat = 22
            let n1 = pt(c, r, 243), c2 = pt(c, r, 315), n3 = pt(c, r, 27)
            let c4 = pt(c, r, 99), c5 = pt(c, r, 171)
            line(n1, c2, 2.6, bond)
            line(c2, n3, 2.6, bond)
            line(n3, c4, 2.6, bond)
            line(c4, c5, 2.6, bond)
            line(c5, n1, 2.6, bond)
            innerBond(c2, n3, c, 2.0)
            innerBond(c4, c5, c, 2.0)
            // 2-methyl group
            let me = pt(c, r + 13, 315)
            line(c2, me, 2.4, bond)
            atomLabel("CH\u{2083}", pt(c, r + 24, 315), NSColor.white, 10)
            // N -> Zn coordination to the diagonal corner nodes
            line(n1, toward(corners[0], from: n1, stopAt: 18), 2.2, bond)
            line(n3, toward(corners[2], from: n3, stopAt: 18), 2.2, bond)
            atomLabel("N", n1, nBlue, 12)
            atomLabel("N", n3, nBlue, 12)
        } else {
            // UiO-66: terephthalate (BDC), para carboxylates chelating the
            // diagonal metal nodes; Kekulé alternating double bonds.
            let r: CGFloat = 20
            var v: [CGPoint] = []
            for k in 0..<6 { v.append(pt(c, r, 45 + CGFloat(k) * 60)) }
            for k in 0..<6 { line(v[k], v[(k + 1) % 6], 2.6, bond) }
            for k in [0, 2, 4] { innerBond(v[k], v[(k + 1) % 6], c, 2.0) }
            for (vi, corner) in [(3, corners[0]), (0, corners[2])] {
                let vp = v[vi]
                let dirx = (vp.x - c.x) / r, diry = (vp.y - c.y) / r
                let cc = CGPoint(x: vp.x + dirx * 13, y: vp.y + diry * 13)
                line(vp, cc, 2.6, bond)
                // Two oxygens fan out from the carboxylate carbon
                let baseA = atan2(diry, dirx)
                for (spread, isDouble) in [(CGFloat(0.62), true), (CGFloat(-0.62), false)] {
                    let o = CGPoint(x: cc.x + 12 * cos(baseA + spread),
                                    y: cc.y + 12 * sin(baseA + spread))
                    line(cc, o, 2.4, bond)
                    if isDouble {
                        let px = -(o.y - cc.y), py = o.x - cc.x
                        let pl = max(sqrt(px * px + py * py), 0.001)
                        line(CGPoint(x: cc.x + px / pl * 3.4, y: cc.y + py / pl * 3.4),
                             CGPoint(x: o.x + px / pl * 3.4, y: o.y + py / pl * 3.4), 1.8, bond)
                    }
                    // O -> Zr coordination
                    line(o, toward(corner, from: o, stopAt: 18), 1.6,
                         CGColor(red: 0.55, green: 0.58, blue: 0.66, alpha: 1))
                    atomLabel("O", o, oRed, 11)
                }
            }
        }
        for p in corners { metalBall(p, 15) }
        NSGraphicsContext.restoreGraphicsState()
        return readPixels(ctx, size: wts)
    }

    private func rndT(_ r: ClosedRange<CGFloat>) -> CGFloat { CGFloat.random(in: r) }

    // MARK: - Sprites

    private func makeSpriteContext() -> CGContext {
        makeContext(size: ts)
    }

    private func makeContext(size: Int) -> CGContext {
        CGContext(data: nil, width: size, height: size,
                  bitsPerComponent: 8, bytesPerRow: size * 4,
                  space: CGColorSpaceCreateDeviceRGB(),
                  bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue
                      | CGBitmapInfo.byteOrder32Little.rawValue)!
    }

    private func readSprite(_ ctx: CGContext) -> [UInt32] {
        readPixels(ctx, size: ts)
    }

    private func readPixels(_ ctx: CGContext, size: Int) -> [UInt32] {
        let ptr = ctx.data!.bindMemory(to: UInt32.self, capacity: size * size)
        return Array(UnsafeBufferPointer(start: ptr, count: size * size))
    }

    private func buildSprites() {
        spriteFrames = (0..<3).map { type in
            (0..<spriteFrameCount).map { f in
                let ctx = makeSpriteContext()
                ctx.translateBy(x: CGFloat(ts) / 2, y: CGFloat(ts) / 2)
                ctx.rotate(by: CGFloat(f) / CGFloat(spriteFrameCount) * 2 * .pi)
                switch type {
                case 0: drawBenzeneSprite(ctx)
                case 1: drawWaterSprite(ctx)
                default: drawBuckySprite(ctx)
                }
                return readSprite(ctx)
            }
        }
    }

    private func drawBenzeneSprite(_ ctx: CGContext) {
        ctx.setStrokeColor(CGColor(red: 0.95, green: 0.95, blue: 1, alpha: 1))
        ctx.setLineWidth(3)
        let path = CGMutablePath()
        for i in 0..<6 {
            let a = CGFloat(i) * .pi / 3
            let p = CGPoint(x: 21 * cos(a), y: 21 * sin(a))
            if i == 0 { path.move(to: p) } else { path.addLine(to: p) }
        }
        path.closeSubpath()
        ctx.addPath(path)
        ctx.strokePath()
        ctx.setLineWidth(2)
        ctx.strokeEllipse(in: CGRect(x: -12, y: -12, width: 24, height: 24))
        ctx.setFillColor(CGColor(red: 0.4, green: 0.85, blue: 0.5, alpha: 1))
        for i in 0..<6 {
            let a = CGFloat(i) * .pi / 3
            ctx.fillEllipse(in: CGRect(x: 21 * cos(a) - 4, y: 21 * sin(a) - 4, width: 8, height: 8))
        }
    }

    private func drawWaterSprite(_ ctx: CGContext) {
        // Two H, one O, 104.5-degree bend.
        let hAngle = 104.5 * .pi / 180 / 2
        let h1 = CGPoint(x: -18 * sin(hAngle), y: -18 * cos(hAngle))
        let h2 = CGPoint(x: 18 * sin(hAngle), y: -18 * cos(hAngle))
        ctx.setStrokeColor(CGColor(red: 0.7, green: 0.7, blue: 0.75, alpha: 1))
        ctx.setLineWidth(4)
        ctx.move(to: .zero); ctx.addLine(to: h1)
        ctx.move(to: .zero); ctx.addLine(to: h2)
        ctx.strokePath()
        ctx.setFillColor(CGColor(red: 0.9, green: 0.2, blue: 0.15, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: -11, y: -11, width: 22, height: 22))
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: h1.x - 7, y: h1.y - 7, width: 14, height: 14))
        ctx.fillEllipse(in: CGRect(x: h2.x - 7, y: h2.y - 7, width: 14, height: 14))
        ctx.setFillColor(CGColor(red: 1, green: 0.6, blue: 0.55, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: -7, y: 0, width: 7, height: 7))
    }

    private func drawBuckySprite(_ ctx: CGContext) {
        ctx.setFillColor(CGColor(red: 0.35, green: 0.32, blue: 0.3, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: -22, y: -22, width: 44, height: 44))
        ctx.setStrokeColor(CGColor(red: 0.8, green: 0.75, blue: 0.7, alpha: 1))
        ctx.setLineWidth(1.5)
        // Hexagon/pentagon cage suggestion
        for ring in [10.0, 19.0] {
            let n = ring < 15 ? 5 : 6
            let path = CGMutablePath()
            for i in 0..<n {
                let a = CGFloat(i) / CGFloat(n) * 2 * .pi + (ring < 15 ? 0.3 : 0)
                let p = CGPoint(x: ring * cos(a), y: ring * sin(a))
                if i == 0 { path.move(to: p) } else { path.addLine(to: p) }
            }
            path.closeSubpath()
            ctx.addPath(path)
            ctx.strokePath()
        }
        for i in 0..<6 {
            let a = CGFloat(i) / 6 * 2 * .pi
            ctx.setStrokeColor(CGColor(red: 0.7, green: 0.65, blue: 0.6, alpha: 1))
            ctx.move(to: CGPoint(x: 10 * cos(a + 0.3), y: 10 * sin(a + 0.3)))
            ctx.addLine(to: CGPoint(x: 19 * cos(a), y: 19 * sin(a)))
            ctx.strokePath()
        }
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.35))
        ctx.fillEllipse(in: CGRect(x: -14, y: 4, width: 10, height: 10))
    }

    // MARK: - Navigation

    private func recomputePath() {
        let startC = (Int(posX), Int(posY))
        var dist = Array(repeating: Array(repeating: -1, count: mw), count: mh)
        var prev = Array(repeating: Array(repeating: (-1, -1), count: mw), count: mh)
        var queue = [startC]
        dist[startC.1][startC.0] = 0
        var qi = 0
        while qi < queue.count {
            let (cx, cy) = queue[qi]; qi += 1
            for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
                let nx = cx + dx, ny = cy + dy
                if nx >= 0 && nx < mw && ny >= 0 && ny < mh && map[ny][nx] == 0
                    && dist[ny][nx] < 0 {
                    dist[ny][nx] = dist[cy][cx] + 1
                    prev[ny][nx] = (cx, cy)
                    queue.append((nx, ny))
                }
            }
        }
        let far = queue.filter { dist[$0.1][$0.0] > 6 }
        guard var target = (far.isEmpty ? queue : far).randomElement() else { return }
        var newPath: [(Int, Int)] = []
        while target != startC && target.0 >= 0 {
            newPath.append(target)
            target = prev[target.1][target.0]
        }
        path = newPath.reversed()
    }

    public override func animateOneFrame() {
        if flipT >= 0 {
            flipT += 1.0 / 36.0
            if flipT >= 1 {
                flipT = -1
                inverted.toggle()
            }
        }
        if path.isEmpty { recomputePath() }
        if let next = path.first {
            let tx = Double(next.0) + 0.5, ty = Double(next.1) + 0.5
            let dx = tx - posX, dy = ty - posY
            let distance = (dx * dx + dy * dy).squareRoot()
            let desired = atan2(dy, dx)
            var diff = desired - angle
            while diff > .pi { diff -= 2 * .pi }
            while diff < -.pi { diff += 2 * .pi }
            if abs(diff) > 0.05 {
                angle += max(-0.085, min(0.085, diff))  // turn in place, classic style
            } else {
                angle = desired
                let step = min(0.052, distance)
                posX += cos(angle) * step
                posY += sin(angle) * step
            }
            if distance < 0.1 { path.removeFirst() }
        }
        // Solvent collision → barrel-flip and relocate the molecule.
        if flipT < 0 {
            for i in molecules.indices {
                if molecules[i].cx == Int(posX) && molecules[i].cy == Int(posY) {
                    flipT = 0
                    molecules.remove(at: i)
                    placeMolecule()
                    break
                }
            }
        }
        for i in molecules.indices { molecules[i].phase += 0.09 }
        render()
        needsDisplay = true
    }

    // MARK: - Renderer

    private func render() {
        let dirX = cos(angle), dirY = sin(angle)
        let planeScale = 0.66
        let planeX = -sin(angle) * planeScale
        let planeY = cos(angle) * planeScale
        let w = iw, h = ih
        let halfH = h / 2

        buffer.withUnsafeMutableBufferPointer { buf in
            // Ceiling and floor (horizontal casting for the checkerboard).
            for y in (halfH + 1)..<h {
                let p = Double(y - halfH)
                let rowDist = Double(halfH) / p
                let stepX = rowDist * (2 * planeX) / Double(w)
                let stepY = rowDist * (2 * planeY) / Double(w)
                var fx = posX + rowDist * (dirX - planeX)
                var fy = posY + rowDist * (dirY - planeY)
                let shade = max(0.25, min(1.0, 1.15 - rowDist * 0.12))
                // Ceiling row (mirrored): dark with distance haze
                let cy = h - 1 - y
                let cv = packRGB(Int(16 * shade + 6), Int(18 * shade + 6), Int(30 * shade + 8))
                let crBase = cy * w
                for x in 0..<w { buf[crBase + x] = cv }
                let frBase = y * w
                for x in 0..<w {
                    let cellX = Int((fx * 2).rounded(.down)), cellY = Int((fy * 2).rounded(.down))
                    let checker = (cellX + cellY) & 1
                    let base = checker == 0 ? 170.0 : 55.0
                    let v = Int(base * shade)
                    buf[frBase + x] = checker == 0
                        ? packRGB(v, v, Int(Double(v) * 1.05))
                        : packRGB(Int(Double(v) * 0.8), Int(Double(v) * 0.9), v)
                    fx += stepX
                    fy += stepY
                }
            }
            // Horizon rows
            let hv = packRGB(10, 12, 20)
            for x in 0..<w { buf[halfH * w + x] = hv }

            // Walls
            for x in 0..<w {
                let cameraX = 2 * Double(x) / Double(w) - 1
                let rayX = dirX + planeX * cameraX
                let rayY = dirY + planeY * cameraX
                var mapX = Int(posX), mapY = Int(posY)
                let deltaX = rayX == 0 ? 1e30 : abs(1 / rayX)
                let deltaY = rayY == 0 ? 1e30 : abs(1 / rayY)
                var stepX = 0, stepY = 0
                var sideX = 0.0, sideY = 0.0
                if rayX < 0 { stepX = -1; sideX = (posX - Double(mapX)) * deltaX }
                else { stepX = 1; sideX = (Double(mapX) + 1 - posX) * deltaX }
                if rayY < 0 { stepY = -1; sideY = (posY - Double(mapY)) * deltaY }
                else { stepY = 1; sideY = (Double(mapY) + 1 - posY) * deltaY }
                var side = 0
                var guard_ = 0
                while guard_ < 64 {
                    guard_ += 1
                    if sideX < sideY { sideX += deltaX; mapX += stepX; side = 0 }
                    else { sideY += deltaY; mapY += stepY; side = 1 }
                    if mapX < 0 || mapX >= mw || mapY < 0 || mapY >= mh { break }
                    if map[mapY][mapX] == 1 { break }
                }
                let perpDist = side == 0
                    ? max(sideX - deltaX, 0.02)
                    : max(sideY - deltaY, 0.02)
                zbuf[x] = perpDist
                let lineH = Int(Double(h) / perpDist)
                var drawStart = -lineH / 2 + halfH
                var drawEnd = lineH / 2 + halfH
                if drawStart < 0 { drawStart = 0 }
                if drawEnd >= h { drawEnd = h - 1 }
                var wallX = side == 0 ? posY + perpDist * rayY : posX + perpDist * rayX
                wallX -= wallX.rounded(.down)
                var texX = Int(wallX * Double(wts))
                if (side == 0 && rayX > 0) || (side == 1 && rayY > 0) { texX = wts - texX - 1 }
                if texX < 0 { texX = 0 }
                if texX >= wts { texX = wts - 1 }
                let tex = ((mapX + mapY) & 1) == 0 ? wallTexA : wallTexB
                let texStep = Double(wts) / Double(lineH)
                var texPos = Double(drawStart - halfH + lineH / 2) * texStep
                var shade = max(0.22, min(1.0, 1.25 - perpDist * 0.11))
                if side == 1 { shade *= 0.72 }
                tex.withUnsafeBufferPointer { tp in
                    for y in drawStart...max(drawStart, drawEnd) {
                        var texY = Int(texPos)
                        if texY < 0 { texY = 0 }
                        if texY >= wts { texY = wts - 1 }
                        texPos += texStep
                        let c = tp[texY * wts + texX]
                        let r = Double((c >> 16) & 0xFF) * shade
                        let g = Double((c >> 8) & 0xFF) * shade
                        let b = Double(c & 0xFF) * shade
                        buf[y * w + x] = packRGB(Int(r), Int(g), Int(b))
                    }
                }
            }

            // Sprites, far to near
            let order = molecules.sorted {
                let da = pow(Double($0.cx) + 0.5 - posX, 2) + pow(Double($0.cy) + 0.5 - posY, 2)
                let db = pow(Double($1.cx) + 0.5 - posX, 2) + pow(Double($1.cy) + 0.5 - posY, 2)
                return da > db
            }
            let invDet = 1.0 / (planeX * dirY - dirX * planeY)
            for m in order {
                let relX = Double(m.cx) + 0.5 - posX
                let relY = Double(m.cy) + 0.5 - posY
                let transX = invDet * (dirY * relX - dirX * relY)
                let transY = invDet * (-planeY * relX + planeX * relY)
                if transY <= 0.15 { continue }
                let screenX = Int(Double(w) / 2 * (1 + transX / transY))
                let size = abs(Int(Double(h) * 0.62 / transY))
                if size < 2 { continue }
                let frameIdx = ((Int(m.phase * 3) % spriteFrameCount)
                    + spriteFrameCount) % spriteFrameCount
                let sprite = spriteFrames[m.type][frameIdx]
                let bob = Int(Double(h) * 0.05 / transY * sin(m.phase * 0.7))
                let startY = max(0, halfH - size / 2 + bob)
                let endY = min(h - 1, halfH + size / 2 + bob)
                let startX = max(0, screenX - size / 2)
                let endX = min(w - 1, screenX + size / 2)
                if startX > endX || startY > endY { continue }
                sprite.withUnsafeBufferPointer { sp in
                    for sx in startX...endX {
                        if transY >= zbuf[sx] { continue }
                        let tx = (sx - (screenX - size / 2)) * ts / max(size, 1)
                        if tx < 0 || tx >= ts { continue }
                        for sy in startY...endY {
                            let ty = (sy - (halfH - size / 2 + bob)) * ts / max(size, 1)
                            if ty < 0 || ty >= ts { continue }
                            let c = sp[ty * ts + tx]
                            if (c >> 24) < 0x60 { continue }
                            buf[sy * w + sx] = c | (0xFF << 24)
                        }
                    }
                }
            }
        }
    }

    public override func draw(_ rect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 1))
        ctx.fill(bounds)
        let data = buffer.withUnsafeBufferPointer { Data(buffer: $0) }
        guard let provider = CGDataProvider(data: data as CFData),
              let image = CGImage(width: iw, height: ih, bitsPerComponent: 8,
                                  bitsPerPixel: 32, bytesPerRow: iw * 4,
                                  space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGBitmapInfo(rawValue:
                                    CGImageAlphaInfo.noneSkipFirst.rawValue
                                        | CGBitmapInfo.byteOrder32Little.rawValue),
                                  provider: provider, decode: nil,
                                  shouldInterpolate: false, intent: .defaultIntent)
        else { return }
        var scaleY: CGFloat = inverted ? -1 : 1
        if flipT >= 0 {
            let s = cos(.pi * flipT) * (inverted ? -1.0 : 1.0)
            scaleY = CGFloat(abs(s) < 0.03 ? (s < 0 ? -0.03 : 0.03) : s)
        }
        ctx.saveGState()
        ctx.interpolationQuality = .none
        ctx.translateBy(x: bounds.midX, y: bounds.midY)
        ctx.scaleBy(x: 1, y: scaleY)
        ctx.draw(image, in: CGRect(x: -bounds.width / 2, y: -bounds.height / 2,
                                   width: bounds.width, height: bounds.height))
        ctx.restoreGState()
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { LatticeMazeView(frame: $0, isPreview: false)! }
#endif
