import ScreenSaver

/// ScreenSaver's timer retains its view even after the host detaches the view.
/// Suspend that timer while detached, preserving the host's explicit stop calls.
public class LifecycleScreenSaverView: ScreenSaverView {
    private var resumeAnimationOnAttachment = false

    public override func stopAnimation() {
        resumeAnimationOnAttachment = false
        super.stopAnimation()
    }

    public override func viewWillMove(toWindow newWindow: NSWindow?) {
        if newWindow == nil, window != nil, isAnimating {
            resumeAnimationOnAttachment = true
            // An internal suspension must not clear the saved resume intent.
            super.stopAnimation()
        }
        super.viewWillMove(toWindow: newWindow)
    }

    public override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window != nil, resumeAnimationOnAttachment {
            resumeAnimationOnAttachment = false
            super.startAnimation()
        }
    }
}
