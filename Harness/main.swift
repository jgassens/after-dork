import AppKit
import ScreenSaver

// Offscreen preview harness: steps a saver's animation and writes PNG
// snapshots so the visuals can be inspected without installing anything.
// Usage: preview <width> <height> <totalFrames> <outPrefix>

let args = CommandLine.arguments
func argD(_ i: Int, _ d: Double) -> Double { args.count > i ? (Double(args[i]) ?? d) : d }
func argI(_ i: Int, _ d: Int) -> Int { args.count > i ? (Int(args[i]) ?? d) : d }
let W = argD(1, 1280)
let H = argD(2, 720)
let total = argI(3, 240)
let outPrefix = args.count > 4 ? args[4] : "frame"

let view = harnessMake(NSRect(x: 0, y: 0, width: W, height: H))
view.startAnimation()

func snapshot(_ path: String) {
    guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil,
                                     pixelsWide: Int(W), pixelsHigh: Int(H),
                                     bitsPerSample: 8, samplesPerPixel: 4,
                                     hasAlpha: true, isPlanar: false,
                                     colorSpaceName: .deviceRGB,
                                     bytesPerRow: 0, bitsPerPixel: 0),
          let gctx = NSGraphicsContext(bitmapImageRep: rep) else {
        FileHandle.standardError.write("snapshot: no context\n".data(using: .utf8)!)
        exit(1)
    }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = gctx
    view.draw(view.bounds)
    gctx.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()
    if let png = rep.representation(using: .png, properties: [:]) {
        try? png.write(to: URL(fileURLWithPath: path))
    }
}

let capture: Set<Int> = [total / 4, total / 2, total - 1]
for i in 0..<total {
    view.animateOneFrame()
    if capture.contains(i) {
        snapshot("\(outPrefix)-\(String(format: "%04d", i)).png")
    }
}
print("previewed \(total) frames -> \(outPrefix)-*.png")
