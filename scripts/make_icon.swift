import AppKit

// Renders the After Dork icon: a beveled 1996 CRT monitor with a winged
// Erlenmeyer flying across a starfield on the tube. Outputs an .iconset.
// Usage: swift scripts/make_icon.swift <out.iconset>

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "AfterDork.iconset"
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

func draw(_ S: CGFloat, into ctx: CGContext) {
    let u = S / 128.0  // design unit: art authored on a 128 grid

    func rr(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat, _ r: CGFloat) -> CGPath {
        CGPath(roundedRect: CGRect(x: x * u, y: y * u, width: w * u, height: h * u),
               cornerWidth: r * u, cornerHeight: r * u, transform: nil)
    }

    // CRT body
    let body = rr(6, 8, 116, 112, 10)
    ctx.addPath(body)
    ctx.setFillColor(CGColor(red: 0.78, green: 0.78, blue: 0.76, alpha: 1))
    ctx.fillPath()
    // Bevel: light top-left, dark bottom-right
    ctx.saveGState()
    ctx.addPath(body)
    ctx.clip()
    ctx.setStrokeColor(CGColor(red: 0.97, green: 0.97, blue: 0.95, alpha: 1))
    ctx.setLineWidth(5 * u)
    ctx.move(to: CGPoint(x: 4 * u, y: 12 * u))
    ctx.addLine(to: CGPoint(x: 4 * u, y: 122 * u))
    ctx.addLine(to: CGPoint(x: 124 * u, y: 122 * u))
    ctx.strokePath()
    ctx.setStrokeColor(CGColor(red: 0.42, green: 0.42, blue: 0.40, alpha: 1))
    ctx.move(to: CGPoint(x: 5 * u, y: 9 * u))
    ctx.addLine(to: CGPoint(x: 123 * u, y: 9 * u))
    ctx.addLine(to: CGPoint(x: 123 * u, y: 120 * u))
    ctx.strokePath()
    ctx.restoreGState()

    // Screen (sunken)
    let screen = CGRect(x: 16 * u, y: 34 * u, width: 96 * u, height: 76 * u)
    ctx.setFillColor(CGColor(red: 0.29, green: 0.29, blue: 0.28, alpha: 1))
    ctx.fill(screen.insetBy(dx: -2.5 * u, dy: -2.5 * u))
    ctx.setFillColor(CGColor(red: 0.02, green: 0.02, blue: 0.10, alpha: 1))
    ctx.fill(screen)
    // Phosphor glow at the bottom of the tube
    if let grad = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(),
                             colors: [CGColor(red: 0.05, green: 0.16, blue: 0.10, alpha: 1),
                                      CGColor(red: 0.02, green: 0.02, blue: 0.10, alpha: 0)] as CFArray,
                             locations: [0, 1]) {
        ctx.saveGState()
        ctx.clip(to: screen)
        ctx.drawLinearGradient(grad,
                               start: CGPoint(x: 0, y: screen.minY),
                               end: CGPoint(x: 0, y: screen.midY), options: [])
        ctx.restoreGState()
    }
    // Stars
    ctx.saveGState()
    ctx.clip(to: screen)
    var seed: UInt64 = 0xC0FFEE
    for _ in 0..<26 {
        seed = seed &* 6364136223846793005 &+ 1442695040888963407
        let sx = screen.minX + CGFloat(seed % 1000) / 1000 * screen.width
        let sy = screen.minY + CGFloat((seed >> 12) % 1000) / 1000 * screen.height
        let b = 0.4 + CGFloat((seed >> 24) % 100) / 160
        ctx.setFillColor(CGColor(red: 0.9, green: 0.92, blue: 1, alpha: b))
        ctx.fill(CGRect(x: sx, y: sy, width: 1.6 * u, height: 1.6 * u))
    }

    // Winged Erlenmeyer, centered on the tube
    ctx.translateBy(x: screen.midX, y: screen.midY - 2 * u)
    ctx.scaleBy(x: 0.9 * u, y: 0.9 * u)
    // Wings
    let wing = CGMutablePath()
    wing.move(to: .zero)
    wing.addCurve(to: CGPoint(x: 30, y: 34),
                  control1: CGPoint(x: 8, y: 16), control2: CGPoint(x: 24, y: 30))
    wing.addQuadCurve(to: CGPoint(x: 30, y: 18), control: CGPoint(x: 38, y: 26))
    wing.addQuadCurve(to: CGPoint(x: 24, y: 7), control: CGPoint(x: 33, y: 11))
    wing.addQuadCurve(to: CGPoint(x: 14, y: -1), control: CGPoint(x: 24, y: 0))
    wing.addQuadCurve(to: .zero, control: CGPoint(x: 6, y: -3))
    wing.closeSubpath()
    for side: CGFloat in [1, -1] {
        ctx.saveGState()
        ctx.scaleBy(x: side, y: 1)
        ctx.translateBy(x: 11, y: 6)
        ctx.rotate(by: 0.55)
        ctx.addPath(wing)
        ctx.setFillColor(CGColor(red: 0.97, green: 0.97, blue: 1, alpha: 0.97))
        ctx.fillPath()
        ctx.addPath(wing)
        ctx.setStrokeColor(CGColor(red: 0.55, green: 0.58, blue: 0.7, alpha: 0.9))
        ctx.setLineWidth(1.4)
        ctx.strokePath()
        ctx.restoreGState()
    }
    // Flask body
    let flask = CGMutablePath()
    flask.move(to: CGPoint(x: -20, y: -28))
    flask.addLine(to: CGPoint(x: 20, y: -28))
    flask.addLine(to: CGPoint(x: 7, y: 6))
    flask.addLine(to: CGPoint(x: 7, y: 26))
    flask.addLine(to: CGPoint(x: 9, y: 28))
    flask.addLine(to: CGPoint(x: 9, y: 31))
    flask.addLine(to: CGPoint(x: -9, y: 31))
    flask.addLine(to: CGPoint(x: -9, y: 28))
    flask.addLine(to: CGPoint(x: -7, y: 26))
    flask.addLine(to: CGPoint(x: -7, y: 6))
    flask.closeSubpath()
    ctx.addPath(flask)
    ctx.setFillColor(CGColor(red: 0.85, green: 0.9, blue: 1.0, alpha: 0.16))
    ctx.fillPath()
    ctx.saveGState()
    ctx.addPath(flask)
    ctx.clip()
    ctx.setFillColor(CGColor(red: 0.22, green: 0.85, blue: 0.35, alpha: 0.95))
    ctx.fill(CGRect(x: -22, y: -30, width: 44, height: 22))
    ctx.setFillColor(CGColor(red: 0.5, green: 1.0, blue: 0.6, alpha: 0.9))
    ctx.fillEllipse(in: CGRect(x: -16, y: -10.5, width: 32, height: 5))
    ctx.restoreGState()
    ctx.addPath(flask)
    ctx.setStrokeColor(CGColor(red: 0.92, green: 0.95, blue: 1.0, alpha: 0.95))
    ctx.setLineWidth(2.4)
    ctx.setLineJoin(.round)
    ctx.strokePath()
    ctx.resetClip()
    ctx.restoreGState()

    // Control strip: vents + power LED
    ctx.setFillColor(CGColor(red: 0.55, green: 0.55, blue: 0.53, alpha: 1))
    for i in 0..<3 {
        ctx.fill(CGRect(x: (18 + CGFloat(i) * 13) * u, y: 19 * u,
                        width: 9 * u, height: 3 * u))
    }
    ctx.setFillColor(CGColor(red: 0.2, green: 0.85, blue: 0.3, alpha: 1))
    ctx.fillEllipse(in: CGRect(x: 102 * u, y: 17.5 * u, width: 6.5 * u, height: 6.5 * u))
}

let variants: [(String, Int)] = [
    ("icon_16x16", 16), ("icon_16x16@2x", 32),
    ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256),
    ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024),
]

for (name, px) in variants {
    let ctx = CGContext(data: nil, width: px, height: px,
                        bitsPerComponent: 8, bytesPerRow: px * 4,
                        space: CGColorSpaceCreateDeviceRGB(),
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    draw(CGFloat(px), into: ctx)
    let img = ctx.makeImage()!
    let rep = NSBitmapImageRep(cgImage: img)
    if let png = rep.representation(using: .png, properties: [:]) {
        try? png.write(to: URL(fileURLWithPath: "\(outDir)/\(name).png"))
    }
}
print("iconset written to \(outDir)")
