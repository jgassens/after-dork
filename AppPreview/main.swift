import AppKit
import ScreenSaver

// After Hood Preview — a windowed app hosting all four savers live.
// Keys 1–4 switch savers, Q or Esc quits.

let savers: [(name: String, make: (NSRect) -> ScreenSaverView)] = [
    ("Flying Flasks", { FlyingFlasksView(frame: $0, isPreview: false)! }),
    ("Glassware Pipes", { GlasswarePipesView(frame: $0, isPreview: false)! }),
    ("Lattice Maze", { LatticeMazeView(frame: $0, isPreview: false)! }),
    ("Mystify Polymers", { MystifyPolymersView(frame: $0, isPreview: false)! }),
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
    win.title = "After Hood — \(entry.name)   (1–4 to switch, Q to quit)"
    view.startAnimation()
}

show(1)  // start on Glassware Pipes
win.center()
win.makeKeyAndOrderFront(nil)
app.activate(ignoringOtherApps: true)

let timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { _ in
    currentView?.animateOneFrame()
}
RunLoop.main.add(timer, forMode: .common)

NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
    switch event.charactersIgnoringModifiers {
    case "1", "2", "3", "4":
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
