import AppKit
import ScreenSaver

// Preview harness, two modes:
//   preview <width> <height> <totalFrames> <outPrefix>   — offscreen PNG snapshots
//   preview --window [<width> <height>]                  — live animated window

let args = CommandLine.arguments
let positional = args.dropFirst().filter { !$0.hasPrefix("--") }
func argD(_ i: Int, _ d: Double) -> Double {
    i < positional.count ? (Double(positional[positional.startIndex + i]) ?? d) : d
}
func argI(_ i: Int, _ d: Int) -> Int {
    i < positional.count ? (Int(positional[positional.startIndex + i]) ?? d) : d
}
let W = argD(0, 1280)
let H = argD(1, 720)

if args.contains("--window") {
    let app = NSApplication.shared
    app.setActivationPolicy(.regular)
    let rect = NSRect(x: 0, y: 0, width: W, height: H)
    let win = NSWindow(contentRect: rect,
                       styleMask: [.titled, .closable, .miniaturizable],
                       backing: .buffered, defer: false)
    win.title = "After Dork — live preview"
    let view = harnessMake(rect)
    win.contentView = view
    win.center()
    win.makeKeyAndOrderFront(nil)
    app.activate(ignoringOtherApps: true)
    view.startAnimation()
    let timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { _ in
        view.animateOneFrame()
    }
    RunLoop.main.add(timer, forMode: .common)
    NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification,
                                           object: win, queue: nil) { _ in
        app.terminate(nil)
    }
    app.run()
} else {
    let total = argI(2, 240)
    let outPrefix = positional.count > 3 ? positional[positional.startIndex + 3] : "frame"

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
}
