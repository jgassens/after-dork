import AppKit
import ScreenSaver
import ObjectiveC

final class Counter { var frames = 0; var timerCallbacks = 0 }
// Observe the framework's private timer selector only in this test process.
// Detached framework timers may fire while skipping animateOneFrame entirely.
typealias TimerStep = @convention(c) (AnyObject, Selector, AnyObject) -> Void
let timerSelector = NSSelectorFromString("_oneStep:")
let timerMethod = class_getInstanceMethod(ScreenSaverView.self, timerSelector)!
let originalTimerStep = unsafeBitCast(method_getImplementation(timerMethod), to: TimerStep.self)
final class WeakView { weak var value: ScreenSaverView?; init(_ value: ScreenSaverView) { self.value = value } }
final class OriginalProbe: ScreenSaverView {
    let counter = Counter()
    override func animateOneFrame() { counter.frames += 1 }
    @objc(_oneStep:) func recordTimer(_ timer: Timer) {
        counter.timerCallbacks += 1
        originalTimerStep(self, timerSelector, timer)
    }
}
final class FixedProbe: LifecycleScreenSaverView {
    let counter = Counter()
    override func animateOneFrame() { counter.frames += 1 }
    @objc(_oneStep:) func recordTimer(_ timer: Timer) {
        counter.timerCallbacks += 1
        originalTimerStep(self, timerSelector, timer)
    }
}
func require(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
func pump(_ duration: TimeInterval) {
    let end = Date(timeIntervalSinceNow: duration)
    while Date() < end { _ = RunLoop.current.run(mode: .default, before: end) }
}
let app = NSApplication.shared
app.setActivationPolicy(.prohibited)
let rect = NSRect(x: 0, y: 0, width: 320, height: 240)
let window = NSWindow(contentRect: rect, styleMask: .borderless, backing: .buffered, defer: false)
window.isReleasedWhenClosed = false
let parent = NSView(frame: rect)
window.contentView = parent

func repeatedRemoval(label: String, make: () -> (ScreenSaverView, Counter)) -> [String: Any] {
    var references: [WeakView] = []
    var counters: [Counter] = []
    for _ in 0..<12 {
        autoreleasepool {
            let (view, counter) = make()
            view.animationTimeInterval = 1.0 / 30.0
            parent.addSubview(view)
            view.startAnimation()
            require(view.isAnimating, "\(label) starts while attached")
            references.append(WeakView(view))
            counters.append(counter)
            view.removeFromSuperview()
        }
    }
    let before = counters.reduce(0) { $0 + $1.frames }
    let timerBefore = counters.reduce(0) { $0 + $1.timerCallbacks }
    pump(0.35)
    let after = counters.reduce(0) { $0 + $1.frames }
    let timerAfter = counters.reduce(0) { $0 + $1.timerCallbacks }
    let retained = references.filter { $0.value != nil }.count
    let animating = references.filter { $0.value?.isAnimating == true }.count
    for reference in references { reference.value?.stopAnimation() }
    pump(0.05)
    return ["label": label, "cycles": references.count, "retained_after_removal": retained,
            "animating_after_removal": animating, "callbacks_after_removal": after - before,
            "timer_callbacks_after_removal": timerAfter - timerBefore,
            "retained_after_explicit_cleanup": references.filter { $0.value != nil }.count]
}
let original = repeatedRemoval(label: "original") {
    let view = OriginalProbe(frame: rect, isPreview: false)!
    return (view, view.counter)
}
let fixed = repeatedRemoval(label: "fixed") {
    let view = FixedProbe(frame: rect, isPreview: false)!
    return (view, view.counter)
}
require(original["retained_after_removal"] as? Int == 12, "original reproduces timer retention")
require((original["timer_callbacks_after_removal"] as? Int ?? 0) > 0, "original continues detached timer callbacks")
require(fixed["animating_after_removal"] as? Int == 0, "fixed has no animating detached views")
require(fixed["callbacks_after_removal"] as? Int == 0, "fixed has no detached callbacks")
require(fixed["timer_callbacks_after_removal"] as? Int == 0, "fixed has no detached timer callbacks")
require(fixed["retained_after_removal"] as? Int == 0, "fixed probes are released after removal")

for preview in [false, true] {
    let view = FixedProbe(frame: rect, isPreview: preview)!
    view.animationTimeInterval = 1.0 / 30.0
    parent.addSubview(view)
    require(!view.isAnimating, "attachment alone does not start animation")
    view.startAnimation()
    pump(0.12)
    require(view.counter.timerCallbacks > 0, "attached view receives timer callbacks")
    view.removeFromSuperview()
    require(!view.isAnimating, "detachment stops the timer immediately")
    let detachedFrames = view.counter.frames
    let detachedTimerCallbacks = view.counter.timerCallbacks
    pump(0.12)
    require(view.counter.frames == detachedFrames, "no callbacks during detachment")
    require(view.counter.timerCallbacks == detachedTimerCallbacks, "no timer callbacks during detachment")
    parent.addSubview(view)
    require(view.isAnimating, "reattachment resumes a suspended running view")
    pump(0.12)
    require(view.counter.timerCallbacks > detachedTimerCallbacks, "reattached timer actually fires")
    view.stopAnimation()
    view.removeFromSuperview()
    parent.addSubview(view)
    require(!view.isAnimating, "explicit stop before detach survives reattachment")
    view.startAnimation()
    view.removeFromSuperview()
    view.stopAnimation()
    parent.addSubview(view)
    require(!view.isAnimating, "explicit stop while detached cancels pending resumption")
    let manualBefore = view.counter.frames
    view.animateOneFrame()
    require(view.counter.frames == manualBefore + 1, "manual stepping remains available")
    view.startAnimation()
    view.viewWillMove(toWindow: nil)
    view.viewWillMove(toWindow: nil)
    require(!view.isAnimating, "duplicate detachment notifications remain stopped")
    view.removeFromSuperview()
    parent.addSubview(view)
    require(view.isAnimating, "duplicate detachment notifications preserve resume intent")
    view.stopAnimation()
    view.removeFromSuperview()
}

// A host can start before attaching; retain the existing ScreenSaverView behavior.
autoreleasepool {
    let view = FixedProbe(frame: rect, isPreview: false)!
    view.animationTimeInterval = 1.0 / 30.0
    view.startAnimation()
    pump(0.12)
    require(view.counter.timerCallbacks > 0, "initial offscreen start remains supported")
    parent.addSubview(view)
    require(view.isAnimating, "pre-attachment start remains active after attachment")
    view.removeFromSuperview()
    require(!view.isAnimating, "pre-attachment start also suspends on later detach")
    view.stopAnimation()
}

// Exercise the real production class, including its arrays and AppKit observers.
var actualReferences: [WeakView] = []
for preview in [false, true] {
    for _ in 0..<6 {
        autoreleasepool {
            let view = StoddartReefView(frame: rect, isPreview: preview)!
            parent.addSubview(view)
            view.startAnimation()
            require(view.isAnimating, "production StoddartReef starts")
            view.animateOneFrame()
            actualReferences.append(WeakView(view))
            view.removeFromSuperview()
            require(!view.isAnimating, "production StoddartReef stops on removal")
        }
    }
}
pump(0.05)
let actualRetained = actualReferences.filter { $0.value != nil }.count
require(actualRetained == 0, "production StoddartReef views release in the harness")
window.close()
let result: [String: Any] = ["status": "passed", "original": original, "fixed": fixed,
    "production_stoddart_cycles": actualReferences.count, "production_stoddart_retained": actualRetained,
    "base_runtime_name": NSStringFromClass(LifecycleScreenSaverView.self),
    "semantics": ["preview and full-size", "suspend and resume", "explicit stop before detach",
                  "explicit stop while detached", "manual stepping", "start before first attachment",
                  "duplicate detachment notifications"]]
let data = try JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys])
FileHandle.standardOutput.write(data)
print()
