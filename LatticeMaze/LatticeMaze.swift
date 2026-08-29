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
    private let ts = 64
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
        wallTexA = makeLatticeTexture(nodeR: 200, nodeG: 110, nodeB: 60,
                                      strutR: 60, strutG: 160, strutB: 150)
        wallTexB = makeLatticeTexture(nodeR: 170, nodeG: 175, nodeB: 190,
                                      strutR: 70, strutG: 110, strutB: 190)
    }

    private func makeLatticeTexture(nodeR: Int, nodeG: Int, nodeB: Int,
                                    strutR: Int, strutG: Int, strutB: Int) -> [UInt32] {
        var tex = [UInt32](repeating: 0, count: ts * ts)
        let nodes = [(0, 0), (ts, 0), (0, ts), (ts, ts), (ts / 2, ts / 2)]
        for y in 0..<ts {
            for x in 0..<ts {
                // Ordered-dither dark background
                let noise = ((x &* 7 &+ y &* 13) & 7) - 3
                var r = 14 + noise, g = 17 + noise, b = 28 + noise

                // Diagonal struts and border struts
                let d1 = abs(x - y), d2 = abs(x + y - ts)
                let onDiag = d1 < 3 || d2 < 3
                let onEdge = x < 3 || x >= ts - 3 || y < 3 || y >= ts - 3
                if onDiag || onEdge {
                    let ridge = onDiag ? min(d1, d2) : min(x, min(y, min(ts - 1 - x, ts - 1 - y)))
                    let bright = 1.0 - Double(ridge) * 0.22
                    r = Int(Double(strutR) * bright)
                    g = Int(Double(strutG) * bright)
                    b = Int(Double(strutB) * bright)
                }
                // Metal nodes at corners and center
                for (nx, ny) in nodes {
                    let dx = Double(x - nx), dy = Double(y - ny)
                    let dist = (dx * dx + dy * dy).squareRoot()
                    if dist < 11 {
                        let shade = 1.0 - dist / 15.0
                        r = Int(Double(nodeR) * shade)
                        g = Int(Double(nodeG) * shade)
                        b = Int(Double(nodeB) * shade)
                        // Specular glint offset up-left
                        let hx = dx + 3.5, hy = dy + 3.5
                        if hx * hx + hy * hy < 6 { r = min(r + 90, 255); g = min(g + 90, 255); b = min(b + 80, 255) }
                        break
                    }
                }
                tex[y * ts + x] = packRGB(r, g, b)
            }
        }
        return tex
    }

    // MARK: - Sprites

    private func makeSpriteContext() -> CGContext {
        let ctx = CGContext(data: nil, width: ts, height: ts,
                            bitsPerComponent: 8, bytesPerRow: ts * 4,
                            space: CGColorSpaceCreateDeviceRGB(),
                            bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue
                                | CGBitmapInfo.byteOrder32Little.rawValue)!
        return ctx
    }

    private func readSprite(_ ctx: CGContext) -> [UInt32] {
        let ptr = ctx.data!.bindMemory(to: UInt32.self, capacity: ts * ts)
        return Array(UnsafeBufferPointer(start: ptr, count: ts * ts))
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
                var texX = Int(wallX * Double(ts))
                if (side == 0 && rayX > 0) || (side == 1 && rayY < 0) { texX = ts - texX - 1 }
                if texX < 0 { texX = 0 }
                if texX >= ts { texX = ts - 1 }
                let tex = ((mapX + mapY) & 1) == 0 ? wallTexA : wallTexB
                let texStep = Double(ts) / Double(lineH)
                var texPos = Double(drawStart - halfH + lineH / 2) * texStep
                var shade = max(0.22, min(1.0, 1.25 - perpDist * 0.11))
                if side == 1 { shade *= 0.72 }
                tex.withUnsafeBufferPointer { tp in
                    for y in drawStart...max(drawStart, drawEnd) {
                        var texY = Int(texPos)
                        if texY < 0 { texY = 0 }
                        if texY >= ts { texY = ts - 1 }
                        texPos += texStep
                        let c = tp[texY * ts + texX]
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
