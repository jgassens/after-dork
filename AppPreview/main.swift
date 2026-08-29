import AppKit
import ScreenSaver

// After Dork Preview — a windowed app hosting all the savers live.
// Keys 1–7 switch savers, Q or Esc quits.

let savers: [(name: String, make: (NSRect) -> ScreenSaverView)] = [
    ("Flying Flasks", { FlyingFlasksView(frame: $0, isPreview: false)! }),
    ("Glassware Pipes", { GlasswarePipesView(frame: $0, isPreview: false)! }),
    ("Lattice Maze", { LatticeMazeView(frame: $0, isPreview: false)! }),
    ("Mystify Origami", { MystifyPolymersView(frame: $0, isPreview: false)! }),
    ("Stoddart Reef", { StoddartReefView(frame: $0, isPreview: false)! }),
    ("Orbital Box", { OrbitalBoxView(frame: $0, isPreview: false)! }),
    ("SMILES Rain", { SmilesRainView(frame: $0, isPreview: false)! }),
]

let app = NSApplication.shared
app.setActivationPolicy(.regular)

let rect = NSRect(x: 0, y: 0, width: 1280, height: 760)
let win = NSWindow(contentRect: rect,
                   styleMask: [.titled, .closable, .miniaturizable],
                   backing: .buffered, defer: false)
win.isReleasedWhenClosed = false

var currentView: ScreenSaverView?

func show(_ i: Int) {
    let entry = savers[i]
    let view = entry.make(rect)
    currentView = view
    win.contentView = view
    win.title = "After Dork — \(entry.name)   (1–7 to switch, Q to quit)"
    view.startAnimation()
}

// Start on the saver given as a 1-7 argument, otherwise pick at random.
let requested = CommandLine.arguments.dropFirst().compactMap { Int($0) }.first
show(requested.map { max(1, min(savers.count, $0)) - 1 }
    ?? Int.random(in: 0..<savers.count))
win.center()
win.makeKeyAndOrderFront(nil)
app.activate(ignoringOtherApps: true)

let timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { _ in
    currentView?.animateOneFrame()
}
RunLoop.main.add(timer, forMode: .common)

NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
    switch event.charactersIgnoringModifiers {
    case "1", "2", "3", "4", "5", "6", "7":
        show(Int(event.charactersIgnoringModifiers!)! - 1)
        return nil
    case "q", "\u{1B}":
        app.terminate(nil)
        return nil
    default:
        return event
    }
}

NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification,
                                       object: win, queue: nil) { _ in
    app.terminate(nil)
}

app.run()
