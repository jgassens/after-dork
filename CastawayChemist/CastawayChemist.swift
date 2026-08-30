import CoreText
import ScreenSaver

// Castaway Chemist — an homage to Sierra's "Johnny Castaway", except the
// island is a chemistry lab and the castaway is a grad student who is never
// allowed to leave. They shuffle between the fume hood, the rotovap, and the
// NMR, and slapstick follows them: the hood detonates in colored smoke, the
// rotovap drops its flask into the bath, and the NMR either quenches or
// fires their sample into the ceiling, where the tubes accumulate as a
// permanent record of their sins.

private func rnd(_ r: ClosedRange<CGFloat>) -> CGFloat { CGFloat.random(in: r) }
private func chance(_ p: CGFloat) -> Bool { CGFloat.random(in: 0...1) < p }

private struct Particle {
    var x: CGFloat, y: CGFloat
    var vx: CGFloat, vy: CGFloat
    var r: CGFloat
    var growth: CGFloat
    var life: Int
    var maxLife: Int
    var red: CGFloat, green: CGFloat, blue: CGFloat
    var buoyant: Bool  // smoke rises and grows; droplets fall under gravity
}

private struct CeilingTube {
    var x: CGFloat
    var tilt: CGFloat
    var cap: Int
}

private enum Station: CaseIterable { case hood, rotovap, nmr, desk }
private enum CharState {
    case walking, working, reacting, fleeing, staring, idling, typing, sleeping
}

/// The helium refill saga, phase by phase. Two coin flips stand between the
/// chemist and success, and neither of them is friendly.
private enum RefillPhase {
    case none, exiting, entering, parking, connecting, toLadder, climbing,
         falling, down, rising, hooking, tankTip, tankFly, descending,
         toValve, opening, filling, nmrFly, landing, cleanup
}

@objc(CastawayChemistView)
public final class CastawayChemistView: ScreenSaverView {

    private var tick = 0
    private var chaos: CGFloat = 1
    private var quenchOn = true

    // The grad student
    private var charX: CGFloat = 300
    private var targetX: CGFloat = 300
    private var facing: CGFloat = 1
    private var state: CharState = .idling
    private var stateTimer = 90
    private var station: Station?
    private var walkPhase: CGFloat = 0

    // Prop state
    private var rotoAttached = true
    private var rotoDropT = -1
    private var bobPhase: CGFloat = 0
    private var boomT = -1
    private var boomHue: CGFloat = 0
    private var sootT = -1        // >= 0: wearing the cartoon soot face
    private var pendingFleeX: CGFloat?  // run here once the stun wears off
    private var typingT = 0       // frames at the laptop this sitting
    private var sleepAt = 500     // typing frames until sleep claims them
    private var sleepT = 0
    private var wakeAt = 700      // nap length, rolled once at lights-out
    private var zzz: [(x: CGFloat, y: CGFloat, age: Int)] = []

    // The refill saga
    private var refill = RefillPhase.none
    private var rfT = 0
    private var heliumT = 0  // frames since last fill; the magnet is thirsty
    private var cylX: CGFloat = 0
    private var cylTilt: CGFloat = 0
    private var cylVisible = false
    private var onDolly = false
    private var hoseToHand = false
    private var hoseToPort = false
    private var climbH: CGFloat = 0
    private var charYOff: CGFloat = 0
    private var fallTilt: CGFloat = 0
    private var flying = false
    private var flyIsNMR = false
    private var flyOff = CGVector.zero
    private var flyVel = CGVector.zero
    private var flyAngle: CGFloat = 0  // drawn tilt; decays smoothly on landing
    private var artAskew = false

    private var cylParkX: CGFloat { nmrCX - 92 * u }
    private var ladderX: CGFloat { nmrCX + 96 * u }
    private var tankHome: CGPoint { CGPoint(x: cylX, y: floorY + 130 * u) }
    private var nmrHome: CGPoint { CGPoint(x: nmrCX, y: floorY + 220 * u) }
    private var quenchT = -1
    private var tubeX: CGFloat = 0
    private var tubeY: CGFloat = -1  // < 0 means no tube in flight
    private var ceilingTubes: [CeilingTube] = []
    private var particles: [Particle] = []

    private let capColors: [(CGFloat, CGFloat, CGFloat)] = [
        (0.85, 0.20, 0.22), (0.20, 0.45, 0.90), (0.25, 0.72, 0.35),
        (0.92, 0.78, 0.20),
    ]

    // MARK: - Layout

    private var W: CGFloat { max(bounds.width, 640) }
    private var H: CGFloat { max(bounds.height, 400) }
    // Scene unit: divisor > 720 shrinks all furniture and the chemist
    // together, buying elbow room between stations.
    private var u: CGFloat { H / 860 }
    // Slim floor and ceiling bands: the lab reads long, like a corridor
    // you will spend seven years in.
    private var floorY: CGFloat { H * 0.09 }
    private var ceilY: CGFloat { H * 0.93 }
    private var hoodCX: CGFloat { W * 0.13 }
    private var benchY: CGFloat { floorY + 128 * u }
    private var bathCX: CGFloat { W * 0.475 }
    private var deskX: CGFloat { W * 0.67 }
    private var nmrCX: CGFloat { W * 0.90 }
    private var magnetTopY: CGFloat { floorY + 400 * u }

    private func stationX(_ s: Station) -> CGFloat {
        switch s {
        case .hood: return hoodCX + 30 * u  // squarely in front, back to us
        case .rotovap: return bathCX - 165 * u
        case .nmr: return nmrCX - 105 * u
        case .desk: return deskX - 58 * u
        }
    }

    private func stationFacing(_ s: Station) -> CGFloat { 1 }

    // MARK: - Lifecycle

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
        chaos = max(0.3, min(3, CGFloat(AfterDork.value("CastawayChemist", "chaos", 1.0))))
        quenchOn = AfterDork.flag("CastawayChemist", "quench", true)
        charX = W * 0.4
        targetX = charX
        state = .idling
        stateTimer = 60
    }

    // MARK: - Brain

    private func chooseNext() {
        // When the helium is overdue, duty overrides wanderlust: go check
        // the magnet, which is where the saga begins.
        if refill == .none, CGFloat(heliumT) > 4500 / chaos {
            station = .nmr
            targetX = stationX(.nmr)
            state = .walking
            return
        }
        if chance(0.65) {
            let options = Station.allCases.filter { $0 != station }
            let s = options.randomElement()!
            station = s
            targetX = stationX(s)
            state = .walking
        } else {
            station = nil
            state = .idling
            stateTimer = Int(rnd(80...240))
        }
    }

    private func startBoom() {
        boomT = 0
        boomHue = rnd(0...1)
        sootT = 0
        // Take it on the chin first: face the hood, stunned and sooty,
        // arms up — THEN run.
        facing = stationFacing(.hood)
        state = .reacting
        stateTimer = 70
        station = nil
        pendingFleeX = min(charX + W * 0.32, W - 80 * u)
    }

    private func startRotoDrop() {
        rotoAttached = false
        rotoDropT = 0
        if state == .working, station == .rotovap {
            state = .reacting
            stateTimer = 100
        }
    }

    private func launchTube() {
        tubeX = nmrCX
        tubeY = magnetTopY + 56 * u
        state = .staring
        stateTimer = 150
    }

    private func startQuench() {
        quenchT = 0
        if abs(charX - nmrCX) < W * 0.35 {
            state = .fleeing
            station = nil
            targetX = max(hoodCX + 200 * u, 80 * u)
            facing = -1
        }
    }

    public override func animateOneFrame() {
        tick += 1
        bobPhase += 0.12

        heliumT += 1
        if refill != .none {
            updateRefill()
        } else {
        switch state {
        case .walking, .fleeing:
            let speed = (state == .fleeing ? 8.5 : 4.2) * u
            walkPhase += state == .fleeing ? 0.55 : 0.34
            if abs(targetX - charX) <= speed {
                charX = targetX
                if state == .fleeing || station == nil {
                    state = .idling
                    stateTimer = Int(rnd(70...160))
                } else if station == .desk {
                    state = .typing
                    typingT = 0
                    sleepAt = Int(rnd(420...650))
                    facing = 1
                } else {
                    state = .working
                    stateTimer = Int(rnd(320...720))
                    facing = stationFacing(station!)
                }
            } else {
                facing = targetX > charX ? 1 : -1
                charX += facing * speed
            }
        case .working:
            stateTimer -= 1
            let p = 0.005 * chaos
            if station == .hood, boomT < 0, chance(p) { startBoom() }
            if station == .rotovap, rotoAttached, chance(p) { startRotoDrop() }
            // The helium clock: once the magnet is due, the current NMR
            // session turns into the refill saga after a beat of gauge
            // reading — and no other magnet shenanigans can preempt it.
            if station == .nmr, CGFloat(heliumT) > 4500 / chaos {
                if chance(0.03) { startRefill() }
            } else if station == .nmr, tubeY < 0, chance(p) {
                launchTube()
            }
            if stateTimer <= 0 { chooseNext() }
        case .typing:
            // Thesis words go in; consciousness drains out.
            typingT += 1
            if typingT >= sleepAt {
                state = .sleeping
                sleepT = 0
                // Drawn ONCE — re-rolling per frame collapses the range.
                wakeAt = Int(rnd(650...900))
            }
        case .sleeping:
            sleepT += 1
            if sleepT % 55 == 12 {
                zzz.append((x: charX + facing * 32 * u, y: floorY + 250 * u,
                            age: 0))
            }
            if sleepT > wakeAt {
                state = .idling
                stateTimer = 50
            }
        case .reacting, .staring, .idling:
            stateTimer -= 1
            if stateTimer <= 0 {
                if state == .reacting, let fleeTo = pendingFleeX {
                    pendingFleeX = nil
                    station = nil
                    state = .fleeing
                    targetX = fleeTo
                } else {
                    chooseNext()
                }
            }
        }

        // The magnet quenches on its own schedule, chemist or no chemist —
        // though deliberately never mid-saga: one catastrophe at a time.
        if quenchOn, quenchT < 0, chance(0.0005 * chaos) { startQuench() }
        }

        // Ambient timers tick whether or not a saga is running: the soot
        // wears off eventually (the shame does not), and Z's age out.
        if sootT >= 0 {
            sootT += 1
            if sootT > 400 { sootT = -1 }
        }
        for i in zzz.indices { zzz[i].age += 1 }
        zzz.removeAll { $0.age > 130 }

        // Fume hood detonation: flash, then colored smoke pours out.
        if boomT >= 0 {
            boomT += 1
            if boomT < 70, boomT % 2 == 0 {
                let c = NSColor(calibratedHue: boomHue, saturation: 0.7,
                                brightness: 0.85, alpha: 1)
                particles.append(Particle(
                    x: hoodCX + rnd(-50...50) * u, y: benchY + rnd(30...60) * u,
                    vx: rnd(-0.5...0.5) * u, vy: rnd(1.4...2.6) * u,
                    r: rnd(6...11) * u, growth: 0.22 * u,
                    life: 90, maxLife: 90,
                    red: c.redComponent, green: c.greenComponent,
                    blue: c.blueComponent, buoyant: true))
            }
            if boomT > 160 { boomT = -1 }
        }

        // Rotovap flask overboard: falls for 12 frames, splashes, bobs in
        // the bath a while, then gets quietly fished back on.
        if rotoDropT >= 0 {
            rotoDropT += 1
            if rotoDropT == 12 {
                for _ in 0..<9 {
                    particles.append(Particle(
                        x: bathCX + 34 * u + rnd(-30...30) * u, y: benchY + 68 * u,
                        vx: rnd(-1.6...1.6) * u, vy: rnd(2.0...4.2) * u,
                        r: rnd(2...3.5) * u, growth: 0,
                        life: 42, maxLife: 42,
                        red: 0.55, green: 0.75, blue: 0.95, buoyant: false))
                }
            }
            if rotoDropT > 280 {
                rotoAttached = true
                rotoDropT = -1
            }
        }

        // NMR tube en route to the ceiling.
        if tubeY >= 0 {
            tubeY += 21 * u
            if tubeY >= ceilY {
                ceilingTubes.append(CeilingTube(x: tubeX + rnd(-6...6) * u,
                                                tilt: rnd(-0.22...0.22),
                                                cap: Int.random(in: 0..<4)))
                if ceilingTubes.count > 15 { ceilingTubes.removeFirst() }
                tubeY = -1
            }
        }

        // Quench: the magnet vents its helium in great white plumes.
        if quenchT >= 0 {
            quenchT += 1
            if quenchT < 210, quenchT % 2 == 0 {
                particles.append(Particle(
                    x: nmrCX + rnd(-20...20) * u, y: magnetTopY + 52 * u,
                    vx: rnd(-1.1...1.1) * u, vy: rnd(2.2...3.8) * u,
                    r: rnd(9...16) * u, growth: 0.34 * u,
                    life: 110, maxLife: 110,
                    red: 0.96, green: 0.97, blue: 1.0, buoyant: true))
            }
            if quenchT > 300 { quenchT = -1 }
        }

        for i in particles.indices {
            particles[i].x += particles[i].vx
            particles[i].y += particles[i].vy
            if particles[i].buoyant {
                particles[i].r += particles[i].growth
                // Smoke pools along the ceiling and drifts sideways —
                // clamped, or the compounding 1.05x rips it off-screen.
                if particles[i].y > ceilY - particles[i].r * 0.4 {
                    particles[i].y = ceilY - particles[i].r * 0.4
                    particles[i].vy = 0
                    if abs(particles[i].vx) < 3.5 * u { particles[i].vx *= 1.05 }
                }
            } else {
                particles[i].vy -= 0.3 * u
            }
            particles[i].life -= 1
        }
        particles.removeAll {
            $0.life <= 0 || $0.y < 0 || $0.x < -80 * u || $0.x > W + 80 * u
        }

        needsDisplay = true
    }

    // MARK: - The refill saga

    private func startRefill() {
        refill = .exiting
        rfT = 0
        station = nil
        // Keep the same-frame `stateTimer <= 0 → chooseNext()` check from
        // clobbering the station we just cleared.
        stateTimer = 100
        zzz.removeAll()
    }

    private func rf(_ p: RefillPhase) { refill = p; rfT = 0 }

    private func updateRefill() {
        rfT += 1
        let walkSpeed = 3.6 * u
        func walk(to x: CGFloat) -> Bool {
            state = .walking
            walkPhase += 0.34
            if abs(x - charX) <= walkSpeed { charX = x; return true }
            facing = x > charX ? 1 : -1
            charX += facing * walkSpeed
            return false
        }
        switch refill {
        case .none:
            break
        case .exiting:
            // Off the left edge: somewhere out there is a gas cylinder depot
            if walk(to: -150 * u) {
                cylVisible = true
                onDolly = true
                cylTilt = -0.22
                charX = -220 * u
                cylX = charX + 62 * u
                rf(.entering)
            }
        case .entering:
            _ = walk(to: cylParkX - 62 * u)
            cylX = charX + 62 * u
            if cylX >= cylParkX { cylX = cylParkX; rf(.parking) }
        case .parking:
            state = .working
            facing = 1
            cylTilt = min(0, -0.22 + CGFloat(rfT) * 0.006)
            if rfT > 45 { cylTilt = 0; onDolly = false; rf(.connecting) }
        case .connecting:
            state = .working
            facing = 1
            if rfT > 70 { hoseToHand = true; rf(.toLadder) }
        case .toLadder:
            if walk(to: ladderX - 2 * u) { climbH = 0; rf(.climbing) }
        case .climbing:
            state = .working
            facing = -1
            climbH = min(1, climbH + 0.012)
            charYOff = climbH * 148 * u
            walkPhase += 0.2
            if climbH >= 1 {
                // First flip of the coin: gravity or glory. A fall tips the
                // body away from the ladder (screen-left), so face +1 —
                // mirrored drawing would rotate the torso off-screen right.
                if chance(0.5) { facing = 1; rf(.falling) } else { rf(.hooking) }
            }
        case .falling:
            state = .idling
            let f = CGFloat(rfT) / 22
            charYOff = max(0, 148 * u * (1 - f * f))
            charX -= 1.5 * u
            fallTilt = min(1.35, f * 1.35)
            if charYOff <= 0 { charYOff = 0; rf(.down) }
        case .down:
            state = .idling
            if rfT > 55 { rf(.rising) }
        case .rising:
            state = .idling
            fallTilt = max(0, fallTilt - 0.12)
            if fallTilt <= 0 { fallTilt = 0; rf(.toLadder) }
        case .hooking:
            state = .working
            facing = -1
            if rfT > 85 {
                hoseToHand = false
                hoseToPort = true
                // Second flip: does the tank respect the hose?
                if chance(0.5) { rf(.tankTip) } else { rf(.descending) }
            }
        case .tankTip:
            state = .reacting
            facing = 1
            cylTilt = min(1.45, CGFloat(rfT) * 0.09)
            if rfT > 22 {
                // Valve snaps off. Newton takes it from here — seeding the
                // flight from the TIPPED pose (center offset and heading
                // along the tank's axis) so there is no one-frame teleport.
                hoseToPort = false
                cylVisible = false
                flying = true
                flyIsNMR = false
                flyOff = CGVector(dx: -130 * u * sin(cylTilt),
                                  dy: 130 * u * (cos(cylTilt) - 1))
                flyVel = CGVector(dx: -16 * u * sin(cylTilt),
                                  dy: 16 * u * cos(cylTilt))
                flyAngle = cylTilt
                debrisBurst(at: CGPoint(x: tankHome.x + flyOff.dx,
                                        y: tankHome.y + flyOff.dy))
                climbH = 0
                rf(.tankFly)
            }
        case .tankFly, .nmrFly:
            charYOff = max(0, charYOff - 14 * u)
            if rfT < 60 {
                _ = walk(to: W * 0.07)
            } else {
                state = .reacting
                facing = 1
            }
            updateFlight()
            if rfT > (refill == .tankFly ? 330 : 400) { rf(.landing) }
        case .descending:
            state = .working
            facing = -1
            climbH = max(0, climbH - 0.015)
            charYOff = climbH * 148 * u
            if climbH <= 0 { rf(.toValve) }
        case .toValve:
            if walk(to: cylX + 46 * u) { facing = -1; rf(.opening) }
        case .opening:
            state = .working
            facing = -1
            if rfT > 60 { rf(.filling) }
        case .filling:
            if rfT < 110 {
                state = .staring
                facing = 1
            } else if rfT < 150 {
                _ = walk(to: nmrCX - 240 * u)
            } else {
                state = .staring
                facing = 1
            }
            // Vapor from the far port, ramping from burp to jet engine
            let interval = max(2, 9 - Int(rfT / 40))
            if rfT % interval == 0 {
                particles.append(Particle(
                    x: nmrCX - 34 * u + rnd(-6...6) * u,
                    y: magnetTopY + 30 * u,
                    vx: rnd(-1.5...(-0.4)) * u, vy: rnd(2.0...3.6) * u,
                    r: rnd(6...12) * u, growth: 0.3 * u,
                    life: 90, maxLife: 90,
                    red: 0.96, green: 0.97, blue: 1.0, buoyant: true))
            }
            if rfT > 340 {
                // The magnet has had enough of this
                flying = true
                flyIsNMR = true
                flyOff = .zero
                flyVel = CGVector(dx: -7 * u, dy: 12 * u)
                hoseToPort = false
                debrisBurst(at: nmrHome)
                rf(.nmrFly)
            }
        case .landing:
            state = .reacting
            facing = 1
            flyOff.dx *= 0.88
            flyOff.dy *= 0.88
            flyAngle *= 0.85  // settle upright instead of snapping there
            if abs(flyOff.dx) < 3 * u, abs(flyOff.dy) < 3 * u {
                flyOff = .zero
                flying = false
                if !flyIsNMR { cylVisible = true; cylTilt = 0 }
                rf(.cleanup)
            }
        case .cleanup:
            state = .idling
            if rfT > 50 {
                // The scene refreshes: art rehung, glass swept, tank gone,
                // no one speaks of it again.
                artAskew = false
                cylVisible = false
                onDolly = false
                hoseToHand = false
                hoseToPort = false
                fallTilt = 0
                charYOff = 0
                particles.removeAll()
                refill = .none
                heliumT = 0
                chooseNext()
            }
        }
    }

    private func updateFlight() {
        let home = flyIsNMR ? nmrHome : tankHome
        flyVel.dx += rnd(-1.3...1.3) * u
        flyVel.dy += rnd(-1.3...1.3) * u
        let sp = max(hypot(flyVel.dx, flyVel.dy), 0.001)
        let maxSp = 14 * u
        if sp > maxSp { flyVel.dx *= maxSp / sp; flyVel.dy *= maxSp / sp }
        flyOff.dx += flyVel.dx
        flyOff.dy += flyVel.dy
        flyAngle = atan2(flyVel.dy, flyVel.dx) - .pi / 2
        let p = CGPoint(x: home.x + flyOff.dx, y: home.y + flyOff.dy)
        var bounced = false
        if p.x < 80 * u { flyVel.dx = abs(flyVel.dx); bounced = true }
        if p.x > W - 80 * u { flyVel.dx = -abs(flyVel.dx); bounced = true }
        if p.y < floorY + 90 * u { flyVel.dy = abs(flyVel.dy); bounced = true }
        if p.y > H - 70 * u { flyVel.dy = -abs(flyVel.dy); bounced = true }
        if bounced {
            artAskew = true
            debrisBurst(at: p)
        }
        // Exhaust screaming out behind it
        particles.append(Particle(
            x: p.x - flyVel.dx * 1.6 + rnd(-4...4) * u,
            y: p.y - flyVel.dy * 1.6 + rnd(-4...4) * u,
            vx: -flyVel.dx * 0.15, vy: -flyVel.dy * 0.15,
            r: rnd(5...9) * u, growth: 0.3 * u,
            life: 40, maxLife: 40,
            red: 0.95, green: 0.96, blue: 1.0, buoyant: true))
    }

    private func debrisBurst(at p: CGPoint) {
        for _ in 0..<7 {
            let c = capColors.randomElement()!
            particles.append(Particle(
                x: p.x + rnd(-10...10) * u, y: p.y + rnd(-10...10) * u,
                vx: rnd(-5...5) * u, vy: rnd(1...7) * u,
                r: rnd(2...3.5) * u, growth: 0,
                life: 55, maxLife: 55,
                red: c.0, green: c.1, blue: c.2, buoyant: false))
        }
        for _ in 0..<3 {
            particles.append(Particle(
                x: p.x, y: p.y,
                vx: rnd(-1...1) * u, vy: rnd(1...2.5) * u,
                r: rnd(7...12) * u, growth: 0.25 * u,
                life: 60, maxLife: 60,
                red: 0.6, green: 0.6, blue: 0.62, buoyant: true))
        }
    }

    // MARK: - Drawing

    /// One device pixel in scene units — everything renders into a tiny
    /// buffer and gets blown up nearest-neighbor, so this is the chunk size.
    private var px: CGFloat = 4
    private var buffer: CGContext?
    private var bufferSize = (w: 0, h: 0)

    private var shakeOn: Bool { (boomT >= 0 && boomT < 10) || flying }

    public override func draw(_ rect: NSRect) {
        guard let screenCtx = NSGraphicsContext.current?.cgContext else { return }
        // The 8-bit magic: render the (clamped) W x H scene at roughly
        // VGA-era resolution with no antialiasing, then blit into bounds
        // with hard pixel edges. Sizing the buffer from W/H — not raw
        // bounds — means a small host (panel preview, picker thumbnail)
        // gets the whole lab scaled down instead of cropped, and covering
        // bounds exactly leaves no unpainted edge strip.
        px = max(2, (H / 340).rounded())
        let pw = Int((W / px).rounded(.up))
        let ph = Int((H / px).rounded(.up))
        if buffer == nil || bufferSize != (pw, ph) {
            buffer = CGContext(data: nil, width: pw, height: ph,
                               bitsPerComponent: 8, bytesPerRow: 0,
                               space: CGColorSpaceCreateDeviceRGB(),
                               bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
            bufferSize = (pw, ph)
        }
        guard let ctx = buffer else { return }
        ctx.saveGState()
        ctx.setShouldAntialias(false)
        ctx.interpolationQuality = .none
        ctx.scaleBy(x: 1 / px, y: 1 / px)
        drawScene(ctx)
        ctx.restoreGState()
        guard let img = ctx.makeImage() else { return }
        screenCtx.saveGState()
        screenCtx.interpolationQuality = .none
        screenCtx.draw(img, in: bounds)
        // Linus hangs OUTSIDE the pixel pass, at full resolution, judging —
        // sharing the lab's camera shake — until chaos knocks him into the
        // pixel world (drawScene draws him while anything is flying or the
        // art is down, so projectiles pass in front of the frame).
        if !(flying || artAskew) {
            screenCtx.scaleBy(x: bounds.width / W, y: bounds.height / H)
            if shakeOn {
                screenCtx.translateBy(x: sin(CGFloat(tick) * 2.7) * 4 * u,
                                      y: cos(CGFloat(tick) * 3.3) * 3 * u)
            }
            drawPauling(screenCtx)
        }
        screenCtx.restoreGState()
    }

    /// The real photograph, if one has been dropped into the bundle (or the
    /// repo, for harness previews). Nil means we fall back to painted oils.
    private lazy var paulingPhoto: CGImage? = {
        let bundle = Bundle(for: CastawayChemistView.self)
        let candidates = [
            // App bundle keeps module resources namespaced per saver.
            bundle.url(forResource: "pauling", withExtension: "png",
                       subdirectory: "CastawayChemist"),
            // Standalone .saver bundle carries it at the top level.
            bundle.url(forResource: "pauling", withExtension: "png"),
            // Preview harness runs from the repo root.
            URL(fileURLWithPath: "CastawayChemist/Resources/pauling.png"),
        ].compactMap { $0 }
        for url in candidates {
            if let data = try? Data(contentsOf: url),
               let rep = NSBitmapImageRep(data: data),
               let cg = rep.cgImage {
                return cg
            }
        }
        return nil
    }()

    /// Linus Pauling — the actual photograph when available, otherwise
    /// smooth antialiased oils — hanging in defiant contrast to the VGA lab
    /// around him. Gilt frame, nameplate.
    private func drawPauling(_ ctx: CGContext) {
        let cx = W * 0.355
        // Knocked clean off the wall by the rampage: face-up on the tile,
        // still crisp, still judging.
        let cy = artAskew ? floorY + 40 * u : H * 0.745
        let pw = 96 * u, ph = 124 * u
        ctx.saveGState()
        ctx.setShouldAntialias(true)
        if artAskew {
            ctx.translateBy(x: cx, y: cy)
            ctx.rotate(by: 0.42)
            ctx.translateBy(x: -cx, y: -cy)
        }
        // Gilt frame, two-tone with an inner bevel
        let outer = CGRect(x: cx - pw / 2, y: cy - ph / 2, width: pw, height: ph)
        ctx.setFillColor(CGColor(red: 0.55, green: 0.40, blue: 0.12, alpha: 1))
        ctx.fill(outer)
        ctx.setFillColor(CGColor(red: 0.87, green: 0.70, blue: 0.30, alpha: 1))
        ctx.fill(outer.insetBy(dx: 2.5 * u, dy: 2.5 * u))
        ctx.setFillColor(CGColor(red: 0.66, green: 0.50, blue: 0.18, alpha: 1))
        ctx.fill(outer.insetBy(dx: 5.5 * u, dy: 5.5 * u))
        let inner = outer.insetBy(dx: 8 * u, dy: 8 * u)
        if let photo = paulingPhoto {
            // The man himself, aspect-filled — downscaled ONCE into a 2x
            // cache instead of resampling 1.2 megapixels every frame.
            let key = Int(inner.width * 2)
            if paulingScaled?.key != key, key > 0 {
                let iw = CGFloat(photo.width), ih = CGFloat(photo.height)
                let scale = max(CGFloat(key) / iw, inner.height * 2 / ih)
                let dw = max(Int(iw * scale), 1), dh = max(Int(ih * scale), 1)
                if let c = CGContext(data: nil, width: dw, height: dh,
                                     bitsPerComponent: 8, bytesPerRow: 0,
                                     space: CGColorSpaceCreateDeviceRGB(),
                                     bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) {
                    c.interpolationQuality = .high
                    c.draw(photo, in: CGRect(x: 0, y: 0, width: dw, height: dh))
                    if let img = c.makeImage() { paulingScaled = (key, img) }
                }
            }
            if let scaled = paulingScaled?.img {
                ctx.saveGState()
                ctx.clip(to: inner)
                let iw = CGFloat(scaled.width), ih = CGFloat(scaled.height)
                let scale = max(inner.width / iw, inner.height / ih)
                let dw = iw * scale, dh = ih * scale
                ctx.draw(scaled, in: CGRect(x: inner.midX - dw / 2,
                                            y: inner.midY - dh / 2,
                                            width: dw, height: dh))
                ctx.restoreGState()
            }
            drawNameplate(ctx, outer: outer, cx: cx)
            ctx.restoreGState()
            return
        }
        // Studio backdrop, softly graded
        if let grad = CGGradient(
            colorsSpace: CGColorSpaceCreateDeviceRGB(),
            colors: [CGColor(red: 0.64, green: 0.68, blue: 0.74, alpha: 1),
                     CGColor(red: 0.30, green: 0.33, blue: 0.40, alpha: 1)] as CFArray,
            locations: [0, 1]) {
            ctx.saveGState()
            ctx.clip(to: inner)
            ctx.drawLinearGradient(grad,
                                   start: CGPoint(x: inner.midX, y: inner.maxY),
                                   end: CGPoint(x: inner.midX, y: inner.minY),
                                   options: [])
            ctx.restoreGState()
        }
        ctx.saveGState()
        ctx.clip(to: inner)
        let hx = inner.midX
        let hy = inner.midY + 10 * u
        // Suit and shirt
        ctx.setFillColor(CGColor(red: 0.15, green: 0.17, blue: 0.24, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: hx - 38 * u, y: inner.minY - 26 * u,
                                   width: 76 * u, height: 58 * u))
        ctx.setFillColor(CGColor(red: 0.95, green: 0.95, blue: 0.93, alpha: 1))
        let collar = CGMutablePath()
        collar.move(to: CGPoint(x: hx - 9 * u, y: inner.minY + 26 * u))
        collar.addLine(to: CGPoint(x: hx, y: inner.minY + 12 * u))
        collar.addLine(to: CGPoint(x: hx + 9 * u, y: inner.minY + 26 * u))
        collar.closeSubpath()
        ctx.addPath(collar)
        ctx.fillPath()
        ctx.setFillColor(CGColor(red: 0.45, green: 0.15, blue: 0.15, alpha: 1))
        ctx.fill(CGRect(x: hx - 2.5 * u, y: inner.minY, width: 5 * u, height: 16 * u))
        // Head: long face, the famous forehead; soft side shading
        let skin = CGColor(red: 0.93, green: 0.78, blue: 0.66, alpha: 1)
        let head = CGRect(x: hx - 19 * u, y: hy - 18 * u, width: 38 * u, height: 52 * u)
        ctx.setFillColor(skin)
        ctx.fillEllipse(in: head)
        ctx.saveGState()
        ctx.addEllipse(in: head)
        ctx.clip()
        ctx.setFillColor(CGColor(red: 0.78, green: 0.60, blue: 0.48, alpha: 0.55))
        ctx.fillEllipse(in: head.offsetBy(dx: 7 * u, dy: -3 * u))
        ctx.setFillColor(skin)
        ctx.fillEllipse(in: head.offsetBy(dx: -2 * u, dy: 1 * u)
                            .insetBy(dx: 2 * u, dy: 2 * u))
        ctx.restoreGState()
        // Ears
        ctx.setFillColor(skin)
        for sx: CGFloat in [-1, 1] {
            ctx.fillEllipse(in: CGRect(x: hx + sx * 19 * u - 3.5 * u,
                                       y: hy - 2 * u, width: 7 * u, height: 11 * u))
        }
        // White hair: side tufts and a thin sweep over the crown
        let hair = CGColor(red: 0.94, green: 0.94, blue: 0.92, alpha: 1)
        ctx.setFillColor(hair)
        for sx: CGFloat in [-1, 1] {
            ctx.fillEllipse(in: CGRect(x: hx + sx * 17 * u - 6 * u,
                                       y: hy + 4 * u, width: 12 * u, height: 18 * u))
        }
        ctx.setStrokeColor(hair)
        ctx.setLineWidth(3.2 * u)
        ctx.addArc(center: CGPoint(x: hx, y: hy + 8 * u), radius: 20 * u,
                   startAngle: 0.55, endAngle: .pi - 0.55, clockwise: false)
        ctx.strokePath()
        // Brows, wire glasses, eyes
        ctx.setStrokeColor(CGColor(red: 0.88, green: 0.88, blue: 0.86, alpha: 1))
        ctx.setLineWidth(2.2 * u)
        for sx: CGFloat in [-1, 1] {
            ctx.move(to: CGPoint(x: hx + sx * 4 * u, y: hy + 15 * u))
            ctx.addLine(to: CGPoint(x: hx + sx * 13 * u, y: hy + 16.5 * u))
            ctx.strokePath()
        }
        ctx.setStrokeColor(CGColor(red: 0.25, green: 0.25, blue: 0.28, alpha: 1))
        ctx.setLineWidth(1.2 * u)
        for sx: CGFloat in [-1, 1] {
            ctx.strokeEllipse(in: CGRect(x: hx + sx * 8.5 * u - 6 * u,
                                         y: hy + 4 * u, width: 12 * u,
                                         height: 10 * u))
        }
        ctx.move(to: CGPoint(x: hx - 2.5 * u, y: hy + 10 * u))
        ctx.addLine(to: CGPoint(x: hx + 2.5 * u, y: hy + 10 * u))
        ctx.strokePath()
        ctx.setFillColor(CGColor(red: 0.22, green: 0.26, blue: 0.34, alpha: 1))
        for sx: CGFloat in [-1, 1] {
            ctx.fillEllipse(in: CGRect(x: hx + sx * 8.5 * u - 1.8 * u,
                                       y: hy + 7.5 * u, width: 3.6 * u,
                                       height: 3.6 * u))
        }
        // Nose and the knowing smile of a double laureate
        ctx.setStrokeColor(CGColor(red: 0.72, green: 0.54, blue: 0.42, alpha: 1))
        ctx.setLineWidth(1.4 * u)
        ctx.move(to: CGPoint(x: hx - 1 * u, y: hy + 6 * u))
        ctx.addQuadCurve(to: CGPoint(x: hx + 2.5 * u, y: hy - 2 * u),
                         control: CGPoint(x: hx + 1 * u, y: hy + 2 * u))
        ctx.strokePath()
        ctx.setStrokeColor(CGColor(red: 0.55, green: 0.35, blue: 0.30, alpha: 1))
        ctx.setLineWidth(1.6 * u)
        ctx.move(to: CGPoint(x: hx - 6 * u, y: hy - 8 * u))
        ctx.addQuadCurve(to: CGPoint(x: hx + 7 * u, y: hy - 7.5 * u),
                         control: CGPoint(x: hx + 0.5 * u, y: hy - 11 * u))
        ctx.strokePath()
        ctx.restoreGState()
        drawNameplate(ctx, outer: outer, cx: cx)
        ctx.restoreGState()
    }

    private var paulingScaled: (key: Int, img: CGImage)?
    private var nameplateLine: (u: CGFloat, line: CTLine, width: CGFloat)?

    private func drawNameplate(_ ctx: CGContext, outer: CGRect, cx: CGFloat) {
        let plate = CGRect(x: cx - 27 * u, y: outer.minY + 1.2 * u,
                           width: 54 * u, height: 8.6 * u)
        ctx.setFillColor(CGColor(red: 0.92, green: 0.80, blue: 0.42, alpha: 1))
        ctx.fill(plate)
        ctx.setStrokeColor(CGColor(red: 0.45, green: 0.32, blue: 0.08, alpha: 1))
        ctx.setLineWidth(0.8 * u)
        ctx.stroke(plate)
        // CoreText draws into the ctx we were HANDED (NSAttributedString
        // .draw targets NSGraphicsContext.current, which is the screen even
        // when we're rendering the pixel buffer). Cached per text size.
        if nameplateLine?.u != u {
            let attrs: [NSAttributedString.Key: Any] = [
                .font: NSFont.boldSystemFont(ofSize: 5.4 * u),
                .foregroundColor: NSColor(calibratedRed: 0.30, green: 0.20,
                                          blue: 0.04, alpha: 1),
            ]
            let name = NSAttributedString(string: "L. PAULING", attributes: attrs)
            let line = CTLineCreateWithAttributedString(name)
            let width = CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
            nameplateLine = (u, line, width)
        }
        if let np = nameplateLine {
            ctx.saveGState()
            ctx.textMatrix = .identity
            ctx.textPosition = CGPoint(x: plate.midX - np.width / 2,
                                       y: plate.midY - 1.9 * u)
            CTLineDraw(np.line, ctx)
            ctx.restoreGState()
        }
    }

    private func drawScene(_ ctx: CGContext) {
        ctx.saveGState()
        // The whole lab jolts when the hood goes off — and the entire time
        // something heavy is airborne.
        if shakeOn {
            ctx.translateBy(x: sin(CGFloat(tick) * 2.7) * 4 * u,
                            y: cos(CGFloat(tick) * 3.3) * 3 * u)
        }
        drawRoom(ctx)
        // While chaos reigns the portrait lives inside the pixel world, so
        // it shakes, falls, and gets flown past like everything else.
        if flying || artAskew { drawPauling(ctx) }
        // Contact shadows ground everything on the tile
        shadow(ctx, cx: hoodCX, w: 300 * u)
        shadow(ctx, cx: (W * 0.36 + W * 0.56) / 2, w: (W * 0.56 - W * 0.36) * 0.94)
        shadow(ctx, cx: deskX, w: 150 * u)
        shadow(ctx, cx: nmrCX, w: 160 * u)
        shadow(ctx, cx: nmrCX - 150 * u, w: 70 * u)
        shadow(ctx, cx: charX, w: 66 * u)
        drawFumeHood(ctx)
        drawBenchAndRotovap(ctx)
        drawDesk(ctx)
        drawNMR(ctx)
        drawCeilingTubes(ctx)
        if tubeY >= 0 { drawFlyingTube(ctx) }
        drawGasRig(ctx)
        drawChar(ctx)
        drawZzz(ctx)
        drawParticles(ctx)
        ctx.restoreGState()
    }

    private func shadow(_ ctx: CGContext, cx: CGFloat, w: CGFloat) {
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 0.16))
        ctx.fillEllipse(in: CGRect(x: cx - w / 2, y: floorY - 7 * u,
                                   width: w, height: 14 * u))
    }

    /// One band of checkerboard dithering: the honest VGA gradient.
    /// Batched into a single fill so the pattern costs one CG call, not
    /// hundreds.
    private func dither(_ ctx: CGContext, yBase: CGFloat, rows: Int,
                        color: CGColor) {
        ctx.setFillColor(color)
        var rects: [CGRect] = []
        for r in 0..<rows {
            var x: CGFloat = r % 2 == 0 ? 0 : px
            while x < W {
                rects.append(CGRect(x: x, y: yBase + CGFloat(r) * px,
                                    width: px, height: px))
                x += 2 * px
            }
        }
        ctx.fill(rects)
    }

    private func drawRoom(_ ctx: CGContext) {
        // Wall: institutional VGA teal, two tones with a dithered seam
        ctx.setFillColor(CGColor(red: 0.45, green: 0.78, blue: 0.75, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: W, height: H))
        ctx.setFillColor(CGColor(red: 0.33, green: 0.62, blue: 0.60, alpha: 1))
        ctx.fill(CGRect(x: 0, y: floorY, width: W, height: 60 * u))
        dither(ctx, yBase: floorY + 60 * u, rows: 3,
               color: CGColor(red: 0.33, green: 0.62, blue: 0.60, alpha: 1))
        // Floor: checkered navy tile, straight off the Sierra island sea
        ctx.setFillColor(CGColor(red: 0.16, green: 0.20, blue: 0.44, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: W, height: floorY))
        ctx.setFillColor(CGColor(red: 0.26, green: 0.32, blue: 0.62, alpha: 1))
        let tileW = 64 * u
        var tx: CGFloat = 0
        var odd = false
        while tx < W {
            if odd {
                ctx.fill(CGRect(x: tx, y: 0, width: tileW, height: floorY - 8 * u))
            }
            odd.toggle()
            tx += tileW
        }
        dither(ctx, yBase: floorY - 3 * px, rows: 3,
               color: CGColor(red: 0.16, green: 0.20, blue: 0.44, alpha: 1))
        // Ceiling with tile seams and a dithered shadow line
        ctx.setFillColor(CGColor(red: 0.92, green: 0.93, blue: 0.88, alpha: 1))
        ctx.fill(CGRect(x: 0, y: ceilY, width: W, height: H - ceilY))
        ctx.setStrokeColor(CGColor(red: 0.70, green: 0.72, blue: 0.68, alpha: 1))
        ctx.setLineWidth(1.2 * u)
        var cx: CGFloat = 40 * u
        while cx < W {
            ctx.move(to: CGPoint(x: cx, y: ceilY))
            ctx.addLine(to: CGPoint(x: cx, y: H))
            ctx.strokePath()
            cx += 120 * u
        }
        ctx.move(to: CGPoint(x: 0, y: ceilY))
        ctx.addLine(to: CGPoint(x: W, y: ceilY))
        ctx.strokePath()
        dither(ctx, yBase: ceilY - 3 * px, rows: 3,
               color: CGColor(red: 0.92, green: 0.93, blue: 0.88, alpha: 1))
        // Periodic table poster, mandatory in every lab since forever.
        // (pox/poy, NOT px — px is the device-pixel size on self.)
        let pw = 110 * u, ph = 74 * u
        let pox = W * 0.70 - pw / 2, poy = H * 0.62
        ctx.saveGState()
        if artAskew {
            ctx.translateBy(x: pox + pw / 2, y: poy + ph / 2)
            ctx.rotate(by: -0.28)
            ctx.translateBy(x: -(pox + pw / 2), y: -(poy + ph / 2))
        }
        ctx.setFillColor(CGColor(red: 0.97, green: 0.97, blue: 0.94, alpha: 1))
        ctx.fill(CGRect(x: pox, y: poy, width: pw, height: ph))
        ctx.setStrokeColor(CGColor(red: 0.35, green: 0.36, blue: 0.40, alpha: 1))
        ctx.setLineWidth(1.4 * u)
        ctx.stroke(CGRect(x: pox, y: poy, width: pw, height: ph))
        let blockColors: [CGColor] = [
            CGColor(red: 0.90, green: 0.55, blue: 0.50, alpha: 1),
            CGColor(red: 0.55, green: 0.72, blue: 0.90, alpha: 1),
            CGColor(red: 0.62, green: 0.85, blue: 0.60, alpha: 1),
            CGColor(red: 0.95, green: 0.85, blue: 0.50, alpha: 1),
        ]
        let cell = 10.5 * u
        for row in 0..<5 {
            for col in 0..<9 {
                // The table's shape: full top-left and top-right corners,
                // gap in the middle of the upper rows.
                if row > 2 || col == 0 || col > 5 {
                    ctx.setFillColor(blockColors[(row + col) % 4])
                    ctx.fill(CGRect(x: pox + 5 * u + CGFloat(col) * cell,
                                    y: poy + ph - 12 * u - CGFloat(row) * cell,
                                    width: cell - 1.5 * u, height: cell - 1.5 * u))
                }
            }
        }
        ctx.restoreGState()
        // Wall clock, spinning through the years of the PhD
        let clockC = CGPoint(x: W * 0.285, y: H * 0.72)
        let cr = 22 * u
        ctx.saveGState()
        if artAskew {
            // Rattled off plumb by whatever is flying around
            ctx.translateBy(x: clockC.x, y: clockC.y)
            ctx.rotate(by: 0.35)
            ctx.translateBy(x: -clockC.x, y: -clockC.y)
        }
        ctx.setFillColor(CGColor(red: 0.95, green: 0.95, blue: 0.93, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: clockC.x - cr, y: clockC.y - cr,
                                   width: 2 * cr, height: 2 * cr))
        ctx.setStrokeColor(CGColor(red: 0.2, green: 0.2, blue: 0.25, alpha: 1))
        ctx.setLineWidth(2 * u)
        ctx.strokeEllipse(in: CGRect(x: clockC.x - cr, y: clockC.y - cr,
                                     width: 2 * cr, height: 2 * cr))
        let minA = .pi / 2 - CGFloat(tick) * 0.05
        let hourA = .pi / 2 - CGFloat(tick) * 0.0042
        ctx.setLineCap(.round)
        ctx.setLineWidth(1.8 * u)
        ctx.move(to: clockC)
        ctx.addLine(to: CGPoint(x: clockC.x + cos(minA) * cr * 0.78,
                                y: clockC.y + sin(minA) * cr * 0.78))
        ctx.strokePath()
        ctx.setLineWidth(2.4 * u)
        ctx.move(to: clockC)
        ctx.addLine(to: CGPoint(x: clockC.x + cos(hourA) * cr * 0.5,
                                y: clockC.y + sin(hourA) * cr * 0.5))
        ctx.strokePath()
        ctx.restoreGState()
    }

    private func drawFumeHood(_ ctx: CGContext) {
        let halfW = 130 * u
        let hoodTop = min(floorY + 540 * u, ceilY - 12 * u)
        let deckY = floorY + 150 * u
        let frame = CGColor(red: 0.80, green: 0.81, blue: 0.84, alpha: 1)
        let frameDark = CGColor(red: 0.52, green: 0.54, blue: 0.58, alpha: 1)
        // Cabinet below the deck
        ctx.setFillColor(CGColor(red: 0.58, green: 0.60, blue: 0.64, alpha: 1))
        ctx.fill(CGRect(x: hoodCX - halfW, y: floorY,
                        width: 2 * halfW, height: deckY - floorY))
        ctx.setStrokeColor(frameDark)
        ctx.setLineWidth(1.5 * u)
        ctx.stroke(CGRect(x: hoodCX - halfW, y: floorY,
                          width: 2 * halfW, height: deckY - floorY))
        // Hazard diamond on the cabinet, as required by people with clipboards
        let dc = CGPoint(x: hoodCX + 60 * u, y: floorY + 75 * u)
        let dr = 20 * u
        let diamond = CGMutablePath()
        diamond.move(to: CGPoint(x: dc.x, y: dc.y + dr))
        diamond.addLine(to: CGPoint(x: dc.x + dr, y: dc.y))
        diamond.addLine(to: CGPoint(x: dc.x, y: dc.y - dr))
        diamond.addLine(to: CGPoint(x: dc.x - dr, y: dc.y))
        diamond.closeSubpath()
        ctx.addPath(diamond)
        ctx.setFillColor(CGColor(red: 0.95, green: 0.80, blue: 0.15, alpha: 1))
        ctx.fillPath()
        ctx.addPath(diamond)
        ctx.setStrokeColor(CGColor(red: 0.2, green: 0.18, blue: 0.12, alpha: 1))
        ctx.setLineWidth(1.6 * u)
        ctx.strokePath()
        ctx.setFillColor(CGColor(red: 0.2, green: 0.18, blue: 0.12, alpha: 1))
        ctx.fill(CGRect(x: dc.x - 1.6 * u, y: dc.y - 8 * u,
                        width: 3.2 * u, height: 11 * u))
        ctx.fillEllipse(in: CGRect(x: dc.x - 1.8 * u, y: dc.y - 13 * u,
                                   width: 3.6 * u, height: 3.6 * u))
        // Interior
        ctx.setFillColor(CGColor(red: 0.46, green: 0.50, blue: 0.52, alpha: 1))
        ctx.fill(CGRect(x: hoodCX - halfW, y: deckY,
                        width: 2 * halfW, height: hoodTop - deckY))
        // Detonation flash fills the hood
        if boomT >= 0, boomT < 6 {
            ctx.setFillColor(CGColor(red: 1, green: 0.95, blue: 0.6, alpha: 0.95))
            ctx.fill(CGRect(x: hoodCX - halfW, y: deckY,
                            width: 2 * halfW, height: hoodTop - deckY))
        }
        // Deck
        ctx.setFillColor(CGColor(red: 0.25, green: 0.26, blue: 0.30, alpha: 1))
        ctx.fill(CGRect(x: hoodCX - halfW - 6 * u, y: deckY,
                        width: 2 * halfW + 12 * u, height: 8 * u))
        // Reagent bottles at the back
        for (i, bc) in capColors.enumerated() {
            let bx = hoodCX - 60 * u + CGFloat(i) * 34 * u
            ctx.setFillColor(CGColor(red: bc.0, green: bc.1, blue: bc.2, alpha: 0.85))
            ctx.fill(CGRect(x: bx, y: deckY + 8 * u, width: 14 * u, height: 26 * u))
            ctx.setFillColor(frameDark)
            ctx.fill(CGRect(x: bx + 3 * u, y: deckY + 34 * u,
                            width: 8 * u, height: 5 * u))
        }
        // Stir plate and flask, mid-reaction
        ctx.setFillColor(CGColor(red: 0.22, green: 0.23, blue: 0.27, alpha: 1))
        ctx.fill(CGRect(x: hoodCX - 88 * u, y: deckY + 8 * u,
                        width: 44 * u, height: 8 * u))
        let fx = hoodCX - 66 * u
        let flask = CGMutablePath()
        flask.move(to: CGPoint(x: fx - 17 * u, y: deckY + 16 * u))
        flask.addLine(to: CGPoint(x: fx + 17 * u, y: deckY + 16 * u))
        flask.addLine(to: CGPoint(x: fx + 5 * u, y: deckY + 46 * u))
        flask.addLine(to: CGPoint(x: fx + 5 * u, y: deckY + 60 * u))
        flask.addLine(to: CGPoint(x: fx - 5 * u, y: deckY + 60 * u))
        flask.addLine(to: CGPoint(x: fx - 5 * u, y: deckY + 46 * u))
        flask.closeSubpath()
        ctx.addPath(flask)
        ctx.setFillColor(CGColor(red: 0.35, green: 0.85, blue: 0.6, alpha: 0.8))
        ctx.fillPath()
        ctx.addPath(flask)
        ctx.setStrokeColor(CGColor(red: 0.9, green: 0.94, blue: 0.98, alpha: 0.9))
        ctx.setLineWidth(1.4 * u)
        ctx.strokePath()
        // Bubbles working up out of the mouth
        for i in 0..<3 {
            let by = (CGFloat(tick) * 0.9 + CGFloat(i) * 26)
                .truncatingRemainder(dividingBy: 78)
            ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1,
                                     alpha: 0.5 * (1 - by / 78)))
            let br = (2 + by * 0.04) * u
            ctx.fillEllipse(in: CGRect(x: fx + sin(by * 0.2 + CGFloat(i)) * 4 * u - br,
                                       y: deckY + 62 * u + by * u - br,
                                       width: 2 * br, height: 2 * br))
        }
        // Sash: glass pane hanging from the top, rattling after a boom
        let rattle = (boomT >= 0 && boomT < 24) ? sin(CGFloat(tick) * 2.1) * 3 * u : 0
        let sashBot = deckY + 190 * u + rattle
        ctx.setFillColor(CGColor(red: 0.85, green: 0.92, blue: 0.98, alpha: 0.22))
        ctx.fill(CGRect(x: hoodCX - halfW + 8 * u, y: sashBot,
                        width: 2 * halfW - 16 * u, height: hoodTop - sashBot))
        ctx.setFillColor(frame)
        ctx.fill(CGRect(x: hoodCX - halfW + 4 * u, y: sashBot,
                        width: 2 * halfW - 8 * u, height: 7 * u))
        // Frame posts and header
        ctx.setFillColor(frame)
        ctx.fill(CGRect(x: hoodCX - halfW - 10 * u, y: floorY,
                        width: 10 * u, height: hoodTop - floorY + 26 * u))
        ctx.fill(CGRect(x: hoodCX + halfW, y: floorY,
                        width: 10 * u, height: hoodTop - floorY + 26 * u))
        ctx.fill(CGRect(x: hoodCX - halfW - 10 * u, y: hoodTop,
                        width: 2 * halfW + 20 * u, height: 26 * u))
        ctx.setStrokeColor(frameDark)
        ctx.setLineWidth(1.2 * u)
        ctx.stroke(CGRect(x: hoodCX - halfW - 10 * u, y: hoodTop,
                          width: 2 * halfW + 20 * u, height: 26 * u))
    }

    private func drawBenchAndRotovap(_ ctx: CGContext) {
        let benchL = W * 0.36, benchR = W * 0.56
        // Legs
        ctx.setFillColor(CGColor(red: 0.42, green: 0.36, blue: 0.30, alpha: 1))
        ctx.fill(CGRect(x: benchL + 10 * u, y: floorY,
                        width: 12 * u, height: benchY - floorY))
        ctx.fill(CGRect(x: benchR - 22 * u, y: floorY,
                        width: 12 * u, height: benchY - floorY))
        // Top, with a darker front edge for depth
        ctx.setFillColor(CGColor(red: 0.16, green: 0.16, blue: 0.19, alpha: 1))
        ctx.fill(CGRect(x: benchL, y: benchY, width: benchR - benchL, height: 12 * u))
        ctx.setFillColor(CGColor(red: 0.08, green: 0.08, blue: 0.10, alpha: 1))
        ctx.fill(CGRect(x: benchL, y: benchY, width: benchR - benchL, height: 4 * u))

        let deck = benchY + 12 * u

        // — The rotovap, drawn to spec: stand tower with control head,
        // coil condenser with vacuum tap, receiving flask hanging beneath,
        // angled rotary drive, evaporating flask dipping into a proper
        // cylindrical bath pot. —
        let towerX = bathCX - 85 * u
        let cream = CGColor(red: 0.92, green: 0.92, blue: 0.88, alpha: 1)
        let creamEdge = CGColor(red: 0.58, green: 0.59, blue: 0.56, alpha: 1)
        let glass = CGColor(red: 0.85, green: 0.90, blue: 0.96, alpha: 0.9)
        ctx.setLineCap(.round)
        // Stand tower with foot plate
        ctx.setFillColor(creamEdge)
        ctx.fill(CGRect(x: towerX - 24 * u, y: deck, width: 48 * u, height: 5 * u))
        ctx.setFillColor(cream)
        ctx.fill(CGRect(x: towerX - 13 * u, y: deck,
                        width: 26 * u, height: 168 * u))
        ctx.setStrokeColor(creamEdge)
        ctx.setLineWidth(1.2 * u)
        ctx.stroke(CGRect(x: towerX - 13 * u, y: deck,
                          width: 26 * u, height: 168 * u))
        // Control head: blue display, knob
        ctx.setFillColor(CGColor(red: 0.78, green: 0.80, blue: 0.82, alpha: 1))
        ctx.fill(CGRect(x: towerX - 24 * u, y: deck + 168 * u,
                        width: 48 * u, height: 30 * u))
        ctx.setFillColor(CGColor(red: 0.20, green: 0.40, blue: 0.95, alpha: 1))
        ctx.fill(CGRect(x: towerX - 17 * u, y: deck + 180 * u,
                        width: 18 * u, height: 11 * u))
        ctx.setFillColor(CGColor(red: 0.12, green: 0.12, blue: 0.15, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: towerX + 7 * u, y: deck + 178 * u,
                                   width: 10 * u, height: 10 * u))

        let drive = CGPoint(x: towerX + 4 * u, y: deck + 138 * u)
        // Condenser column rising up-left, spiral coil inside
        let condTop = CGPoint(x: drive.x - 44 * u, y: drive.y + 126 * u)
        ctx.setStrokeColor(CGColor(red: 0.80, green: 0.88, blue: 0.95, alpha: 0.45))
        ctx.setLineWidth(17 * u)
        ctx.move(to: drive); ctx.addLine(to: condTop); ctx.strokePath()
        let ax = condTop.x - drive.x, ay = condTop.y - drive.y
        let alen = max(hypot(ax, ay), 0.001)
        let pxu = -ay / alen, pyu = ax / alen
        ctx.setStrokeColor(CGColor(red: 0.55, green: 0.75, blue: 0.95, alpha: 0.95))
        ctx.setLineWidth(2.2 * u)
        for i in 1...9 {
            let f = CGFloat(i) / 10.5
            let cx = drive.x + ax * f, cy = drive.y + ay * f
            ctx.move(to: CGPoint(x: cx - pxu * 7 * u - ax / alen * 3 * u,
                                 y: cy - pyu * 7 * u - ay / alen * 3 * u))
            ctx.addLine(to: CGPoint(x: cx + pxu * 7 * u + ax / alen * 3 * u,
                                    y: cy + pyu * 7 * u + ay / alen * 3 * u))
            ctx.strokePath()
        }
        // Vacuum tap with its green plug
        ctx.setStrokeColor(glass)
        ctx.setLineWidth(4 * u)
        ctx.move(to: condTop)
        ctx.addLine(to: CGPoint(x: condTop.x - 16 * u, y: condTop.y + 6 * u))
        ctx.strokePath()
        ctx.setFillColor(CGColor(red: 0.20, green: 0.70, blue: 0.35, alpha: 1))
        ctx.fillEllipse(in: CGRect(x: condTop.x - 22 * u, y: condTop.y + 2 * u,
                                   width: 9 * u, height: 9 * u))

        // Receiving flask hanging beneath the condenser on a metal clip
        let recv = CGPoint(x: drive.x - 42 * u, y: drive.y - 50 * u)
        ctx.setStrokeColor(glass)
        ctx.setLineWidth(5 * u)
        ctx.move(to: CGPoint(x: drive.x - 12 * u, y: drive.y - 4 * u))
        ctx.addLine(to: recv)
        ctx.strokePath()
        ctx.setStrokeColor(CGColor(red: 0.55, green: 0.58, blue: 0.63, alpha: 1))
        ctx.setLineWidth(3 * u)
        ctx.move(to: CGPoint(x: drive.x - 24 * u, y: drive.y - 20 * u))
        ctx.addLine(to: CGPoint(x: drive.x - 32 * u, y: drive.y - 32 * u))
        ctx.strokePath()
        let rr = 19 * u
        ctx.setFillColor(CGColor(red: 0.85, green: 0.90, blue: 1.0, alpha: 0.35))
        ctx.fillEllipse(in: CGRect(x: recv.x - rr, y: recv.y - rr,
                                   width: 2 * rr, height: 2 * rr))
        ctx.saveGState()
        ctx.addEllipse(in: CGRect(x: recv.x - rr, y: recv.y - rr,
                                  width: 2 * rr, height: 2 * rr))
        ctx.clip()
        ctx.setFillColor(CGColor(red: 0.98, green: 0.85, blue: 0.45, alpha: 0.85))
        ctx.fill(CGRect(x: recv.x - rr, y: recv.y - rr,
                        width: 2 * rr, height: rr * 0.55))
        ctx.restoreGState()
        ctx.setStrokeColor(CGColor(red: 0.9, green: 0.94, blue: 0.99, alpha: 0.95))
        ctx.setLineWidth(1.6 * u)
        ctx.strokeEllipse(in: CGRect(x: recv.x - rr, y: recv.y - rr,
                                     width: 2 * rr, height: 2 * rr))

        // Rotary drive block, angled like it means it, collars both ends
        ctx.saveGState()
        ctx.translateBy(x: drive.x, y: drive.y)
        ctx.rotate(by: -0.62)
        ctx.setFillColor(CGColor(red: 0.25, green: 0.26, blue: 0.30, alpha: 1))
        ctx.fill(CGRect(x: -20 * u, y: -12 * u, width: 40 * u, height: 24 * u))
        ctx.setFillColor(CGColor(red: 0.10, green: 0.10, blue: 0.12, alpha: 1))
        ctx.fill(CGRect(x: 14 * u, y: -9 * u, width: 10 * u, height: 18 * u))
        ctx.fill(CGRect(x: -24 * u, y: -9 * u, width: 10 * u, height: 18 * u))
        ctx.restoreGState()

        // Evaporating flask on the vapor duct — or bobbing in the drink —
        // with a red Keck clip at the joint
        let potX = bathCX + 34 * u
        let potW = 98 * u
        let attachC = CGPoint(x: potX - 12 * u, y: deck + 62 * u)
        var fc = attachC
        let dropC = CGPoint(x: potX + 2 * u,
                            y: deck + 38 * u + sin(bobPhase) * 2 * u)
        if !rotoAttached {
            // Ease into the bath on the way down, and ease back out for the
            // last dozen frames so reattachment isn't a one-frame teleport.
            var f = min(CGFloat(rotoDropT) / 12, 1)
            if rotoDropT > 268 { f = max(0, CGFloat(280 - rotoDropT) / 12) }
            fc = CGPoint(x: attachC.x + (dropC.x - attachC.x) * f,
                         y: attachC.y + (dropC.y - attachC.y) * f * f)
        }
        ctx.setStrokeColor(glass)
        ctx.setLineWidth(6 * u)
        ctx.move(to: drive)
        ctx.addLine(to: rotoAttached ? attachC : CGPoint(
            x: drive.x + (attachC.x - drive.x) * 0.55,
            y: drive.y + (attachC.y - drive.y) * 0.55))
        ctx.strokePath()
        ctx.setStrokeColor(CGColor(red: 0.85, green: 0.20, blue: 0.18, alpha: 1))
        ctx.setLineWidth(2.6 * u)
        let clipF: CGFloat = 0.42
        ctx.move(to: CGPoint(x: drive.x + (attachC.x - drive.x) * clipF - 4 * u,
                             y: drive.y + (attachC.y - drive.y) * clipF - 5 * u))
        ctx.addLine(to: CGPoint(x: drive.x + (attachC.x - drive.x) * clipF + 4 * u,
                                y: drive.y + (attachC.y - drive.y) * clipF + 5 * u))
        ctx.strokePath()
        let fr = 24 * u
        ctx.setFillColor(CGColor(red: 0.85, green: 0.90, blue: 1.0, alpha: 0.35))
        ctx.fillEllipse(in: CGRect(x: fc.x - fr, y: fc.y - fr,
                                   width: 2 * fr, height: 2 * fr))
        ctx.saveGState()
        ctx.addEllipse(in: CGRect(x: fc.x - fr, y: fc.y - fr,
                                  width: 2 * fr, height: 2 * fr))
        ctx.clip()
        ctx.setFillColor(CGColor(red: 0.95, green: 0.75, blue: 0.25, alpha: 0.9))
        ctx.fill(CGRect(x: fc.x - fr, y: fc.y - fr, width: 2 * fr, height: fr * 0.8))
        ctx.restoreGState()
        ctx.setStrokeColor(CGColor(red: 0.9, green: 0.94, blue: 0.99, alpha: 0.95))
        ctx.setLineWidth(1.8 * u)
        ctx.strokeEllipse(in: CGRect(x: fc.x - fr, y: fc.y - fr,
                                     width: 2 * fr, height: 2 * fr))
        if rotoAttached {
            let a = CGFloat(tick) * 0.4
            ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.75))
            ctx.setLineWidth(2.2 * u)
            ctx.addArc(center: fc, radius: fr * 0.7,
                       startAngle: a, endAngle: a + 0.9, clockwise: false)
            ctx.strokePath()
        }

        // The bath pot, drawn over the flask so it truly dips inside:
        // cream cylinder, silver rim, hazard sticker
        ctx.setFillColor(cream)
        ctx.fill(CGRect(x: potX - potW / 2, y: deck, width: potW, height: 48 * u))
        ctx.setStrokeColor(creamEdge)
        ctx.setLineWidth(1.2 * u)
        ctx.stroke(CGRect(x: potX - potW / 2, y: deck, width: potW, height: 48 * u))
        ctx.setFillColor(CGColor(red: 0.72, green: 0.74, blue: 0.78, alpha: 1))
        ctx.fill(CGRect(x: potX - potW / 2 - 2 * u, y: deck + 48 * u,
                        width: potW + 4 * u, height: 9 * u))
        ctx.setFillColor(CGColor(red: 0.92, green: 0.94, blue: 0.97, alpha: 1))
        ctx.fill(CGRect(x: potX - potW / 2 - 2 * u, y: deck + 55 * u,
                        width: potW + 4 * u, height: 2 * u))
        ctx.setFillColor(CGColor(red: 0.95, green: 0.80, blue: 0.15, alpha: 1))
        let hd = CGPoint(x: potX + potW * 0.28, y: deck + 18 * u)
        let hdr = 8 * u
        let hdd = CGMutablePath()
        hdd.move(to: CGPoint(x: hd.x, y: hd.y + hdr))
        hdd.addLine(to: CGPoint(x: hd.x + hdr, y: hd.y))
        hdd.addLine(to: CGPoint(x: hd.x, y: hd.y - hdr))
        hdd.addLine(to: CGPoint(x: hd.x - hdr, y: hd.y))
        hdd.closeSubpath()
        ctx.addPath(hdd)
        ctx.fillPath()
    }

    /// Writing desk with a laptop and the stool where dissertations go to
    /// die. The screen glows while anyone types, and falls asleep shortly
    /// after they do.
    private func drawDesk(_ ctx: CGContext) {
        let topY = floorY + 122 * u
        let halfW = 90 * u
        let wood = CGColor(red: 0.50, green: 0.34, blue: 0.20, alpha: 1)
        ctx.setFillColor(wood)
        ctx.fill(CGRect(x: deskX - halfW + 6 * u, y: floorY,
                        width: 10 * u, height: topY - floorY))
        ctx.fill(CGRect(x: deskX + halfW - 16 * u, y: floorY,
                        width: 10 * u, height: topY - floorY))
        ctx.setFillColor(CGColor(red: 0.64, green: 0.45, blue: 0.28, alpha: 1))
        ctx.fill(CGRect(x: deskX - halfW, y: topY,
                        width: 2 * halfW, height: 10 * u))
        ctx.setFillColor(CGColor(red: 0.38, green: 0.26, blue: 0.15, alpha: 1))
        ctx.fill(CGRect(x: deskX - halfW, y: topY,
                        width: 2 * halfW, height: 3 * u))
        // A proper chair: seat, tilted backrest behind the sitter, legs
        let chairX = stationX(.desk)
        ctx.setFillColor(wood)
        ctx.fill(CGRect(x: chairX - 18 * u, y: floorY,
                        width: 8 * u, height: 60 * u))
        ctx.fill(CGRect(x: chairX + 12 * u, y: floorY,
                        width: 8 * u, height: 60 * u))
        ctx.setFillColor(CGColor(red: 0.64, green: 0.45, blue: 0.28, alpha: 1))
        ctx.fill(CGRect(x: chairX - 24 * u, y: floorY + 60 * u,
                        width: 48 * u, height: 8 * u))
        ctx.setLineCap(.round)
        ctx.setStrokeColor(CGColor(red: 0.64, green: 0.45, blue: 0.28, alpha: 1))
        ctx.setLineWidth(9 * u)
        ctx.move(to: CGPoint(x: chairX - 36 * u, y: floorY + 62 * u))
        ctx.addLine(to: CGPoint(x: chairX - 47 * u, y: floorY + 158 * u))
        ctx.strokePath()
        ctx.setStrokeColor(CGColor(red: 0.38, green: 0.26, blue: 0.15, alpha: 1))
        ctx.setLineWidth(2 * u)
        ctx.move(to: CGPoint(x: chairX - 36 * u, y: floorY + 62 * u))
        ctx.addLine(to: CGPoint(x: chairX - 47 * u, y: floorY + 158 * u))
        ctx.strokePath()
        // Laptop: base near the typist, screen leaning away
        let lapX = deskX + 4 * u
        ctx.setFillColor(CGColor(red: 0.30, green: 0.31, blue: 0.35, alpha: 1))
        ctx.fill(CGRect(x: lapX - 26 * u, y: topY + 10 * u,
                        width: 48 * u, height: 5 * u))
        let asleep = state == .sleeping && sleepT > 220
        let inUse = (state == .typing || state == .sleeping) && !asleep
        ctx.setStrokeColor(CGColor(red: 0.22, green: 0.23, blue: 0.27, alpha: 1))
        ctx.setLineWidth(5 * u)
        ctx.setLineCap(.round)
        ctx.move(to: CGPoint(x: lapX + 17 * u, y: topY + 13 * u))
        ctx.addLine(to: CGPoint(x: lapX + 26 * u, y: topY + 54 * u))
        ctx.strokePath()
        // Screen glow toward the typist
        ctx.setStrokeColor(inUse
            ? CGColor(red: 0.55, green: 0.80, blue: 1.0,
                      alpha: 0.85 + 0.15 * sin(CGFloat(tick) * 0.7))
            : CGColor(red: 0.10, green: 0.11, blue: 0.14, alpha: 1))
        ctx.setLineWidth(3 * u)
        ctx.move(to: CGPoint(x: lapX + 15 * u, y: topY + 14 * u))
        ctx.addLine(to: CGPoint(x: lapX + 23 * u, y: topY + 52 * u))
        ctx.strokePath()
    }

    /// The gas cylinder (parked, tipping, or rocketing), its dolly, and
    /// the transfer hose wherever it currently leads.
    private func drawGasRig(_ ctx: CGContext) {
        let valveTip: CGPoint
        if flying && !flyIsNMR {
            // Tank on the loose: drawn at its offset, nose to the wind
            let p = CGPoint(x: tankHome.x + flyOff.dx, y: tankHome.y + flyOff.dy)
            ctx.saveGState()
            ctx.translateBy(x: p.x, y: p.y)
            ctx.rotate(by: flyAngle)
            drawTankBody(ctx, centered: true)
            ctx.restoreGState()
            return
        }
        if cylVisible {
            ctx.saveGState()
            ctx.translateBy(x: cylX, y: floorY)
            ctx.rotate(by: cylTilt)
            drawTankBody(ctx, centered: false)
            ctx.restoreGState()
            if onDolly {
                // Two-wheeled hand truck under the tilted tank
                let wheelC = CGPoint(x: cylX + 22 * u, y: floorY + 9 * u)
                ctx.setFillColor(CGColor(red: 0.12, green: 0.12, blue: 0.14, alpha: 1))
                ctx.fillEllipse(in: CGRect(x: wheelC.x - 10 * u, y: wheelC.y - 10 * u,
                                           width: 20 * u, height: 20 * u))
                ctx.setStrokeColor(CGColor(red: 0.45, green: 0.47, blue: 0.52, alpha: 1))
                ctx.setLineWidth(4 * u)
                ctx.setLineCap(.round)
                ctx.move(to: CGPoint(x: cylX + 26 * u, y: floorY + 2 * u))
                ctx.addLine(to: CGPoint(x: cylX - 40 * u, y: floorY + 150 * u))
                ctx.strokePath()
            }
            let tilt = cylTilt
            valveTip = CGPoint(x: cylX - sin(tilt) * 262 * u,
                               y: floorY + cos(tilt) * 262 * u)
        } else {
            return
        }
        // The hose, sagging like its owner's optimism
        if hoseToHand || hoseToPort {
            let end: CGPoint = hoseToPort
                ? CGPoint(x: nmrCX + 34 * u, y: magnetTopY + 32 * u)
                : CGPoint(x: charX + facing * 16 * u,
                          y: floorY + charYOff + 150 * u)
            ctx.setStrokeColor(CGColor(red: 0.30, green: 0.32, blue: 0.36, alpha: 1))
            ctx.setLineWidth(3.5 * u)
            ctx.setLineCap(.round)
            ctx.move(to: valveTip)
            ctx.addQuadCurve(to: end, control: CGPoint(
                x: (valveTip.x + end.x) / 2,
                y: min(valveTip.y, end.y) - 55 * u))
            ctx.strokePath()
        }
    }

    /// The cylinder itself: tall as the chemist, shoulder, valve, band.
    private func drawTankBody(_ ctx: CGContext, centered: Bool) {
        let h = 260 * u, w = 54 * u
        let y0 = centered ? -h / 2 : 0
        let steel = CGColor(red: 0.42, green: 0.55, blue: 0.52, alpha: 1)
        let steelDark = CGColor(red: 0.27, green: 0.37, blue: 0.35, alpha: 1)
        ctx.setFillColor(steel)
        ctx.addPath(CGPath(roundedRect: CGRect(x: -w / 2, y: y0,
                                               width: w, height: h),
                           cornerWidth: w * 0.45, cornerHeight: 26 * u,
                           transform: nil))
        ctx.fillPath()
        ctx.setFillColor(steelDark)
        ctx.fill(CGRect(x: -w / 2, y: y0 + h * 0.62, width: w, height: 10 * u))
        ctx.setStrokeColor(steelDark)
        ctx.setLineWidth(1.6 * u)
        ctx.addPath(CGPath(roundedRect: CGRect(x: -w / 2, y: y0,
                                               width: w, height: h),
                           cornerWidth: w * 0.45, cornerHeight: 26 * u,
                           transform: nil))
        ctx.strokePath()
        // Highlight
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.35))
        ctx.fill(CGRect(x: -w * 0.30, y: y0 + 12 * u,
                        width: w * 0.16, height: h - 40 * u))
        // Valve stem and handwheel
        ctx.setFillColor(CGColor(red: 0.55, green: 0.57, blue: 0.62, alpha: 1))
        ctx.fill(CGRect(x: -4 * u, y: y0 + h, width: 8 * u, height: 12 * u))
        ctx.fill(CGRect(x: -12 * u, y: y0 + h + 10 * u,
                        width: 24 * u, height: 5 * u))
    }

    /// Cartoon sleep: fat pixel Z's rising, drifting, dissolving.
    private func drawZzz(_ ctx: CGContext) {
        for z in zzz {
            let f = CGFloat(z.age) / 130
            let size = (8 + 10 * f) * u
            ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1,
                                       alpha: 0.95 * (1 - f)))
            ctx.setLineWidth(2.8 * u)
            ctx.setLineCap(.round)
            let x = z.x + sin(f * 6) * 8 * u + f * 26 * u
            let y = z.y + f * 95 * u
            ctx.move(to: CGPoint(x: x - size / 2, y: y + size / 2))
            ctx.addLine(to: CGPoint(x: x + size / 2, y: y + size / 2))
            ctx.addLine(to: CGPoint(x: x - size / 2, y: y - size / 2))
            ctx.addLine(to: CGPoint(x: x + size / 2, y: y - size / 2))
            ctx.strokePath()
        }
    }

    private func drawNMR(_ ctx: CGContext) {
        let halfW = 60 * u
        let bodyBot = floorY + 46 * u
        let steel = CGColor(red: 0.35, green: 0.37, blue: 0.42, alpha: 1)
        let rim = CGColor(red: 0.68, green: 0.70, blue: 0.76, alpha: 1)
        // When the magnet itself is airborne, the whole assembly rides its
        // offset (console and ladder stay behind, sensibly declining to go).
        let magnetFlying = flying && flyIsNMR
        ctx.saveGState()
        if magnetFlying {
            // Wobble fades with the remaining offset so touchdown is smooth.
            let wob = min(1, hypot(flyOff.dx, flyOff.dy) / (120 * u))
            ctx.translateBy(x: nmrHome.x + flyOff.dx, y: nmrHome.y + flyOff.dy)
            ctx.rotate(by: sin(CGFloat(tick) * 0.31) * 0.22 * wob)
            ctx.translateBy(x: -nmrHome.x, y: -nmrHome.y)
        }
        // Tall stilt legs with feet: the can floats above the floor
        ctx.setFillColor(steel)
        for lx in [nmrCX - 46 * u, nmrCX - 5 * u, nmrCX + 36 * u] {
            ctx.fill(CGRect(x: lx, y: floorY, width: 10 * u,
                            height: bodyBot - floorY))
            ctx.fill(CGRect(x: lx - 4 * u, y: floorY, width: 18 * u,
                            height: 5 * u))
        }
        // Magnet can: cream vacuum vessel with dished bottom and top rims
        let body = CGRect(x: nmrCX - halfW, y: bodyBot,
                          width: 2 * halfW, height: magnetTopY - bodyBot)
        ctx.setFillColor(CGColor(red: 0.93, green: 0.93, blue: 0.90, alpha: 1))
        ctx.addPath(CGPath(roundedRect: body, cornerWidth: 16 * u,
                           cornerHeight: 16 * u, transform: nil))
        ctx.fillPath()
        ctx.setStrokeColor(CGColor(red: 0.55, green: 0.57, blue: 0.63, alpha: 1))
        ctx.setLineWidth(1.6 * u)
        ctx.addPath(CGPath(roundedRect: body, cornerWidth: 16 * u,
                           cornerHeight: 16 * u, transform: nil))
        ctx.strokePath()
        // Weld seams
        for f in [0.34, 0.68] {
            let sy = bodyBot + (magnetTopY - bodyBot) * CGFloat(f)
            ctx.move(to: CGPoint(x: nmrCX - halfW + 4 * u, y: sy))
            ctx.addLine(to: CGPoint(x: nmrCX + halfW - 4 * u, y: sy))
            ctx.strokePath()
        }
        // Cylinder shading: bright band left of center, dusk on the right
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.55))
        ctx.fill(CGRect(x: nmrCX - halfW * 0.62, y: bodyBot + 10 * u,
                        width: halfW * 0.28, height: magnetTopY - bodyBot - 20 * u))
        ctx.setFillColor(CGColor(red: 0, green: 0, blue: 0, alpha: 0.07))
        ctx.fill(CGRect(x: nmrCX + halfW * 0.38, y: bodyBot + 6 * u,
                        width: halfW * 0.55, height: magnetTopY - bodyBot - 12 * u))
        // Top plate, then the three-tower turret crown every NMR wears:
        // tall sample neck in the middle, squat helium and nitrogen ports
        // flanking it.
        ctx.setFillColor(rim)
        ctx.fill(CGRect(x: nmrCX - halfW + 6 * u, y: magnetTopY - 3 * u,
                        width: 2 * halfW - 12 * u, height: 8 * u))
        ctx.setFillColor(rim)
        ctx.fill(CGRect(x: nmrCX - 9 * u, y: magnetTopY + 5 * u,
                        width: 18 * u, height: 42 * u))
        ctx.fill(CGRect(x: nmrCX - 13 * u, y: magnetTopY + 47 * u,
                        width: 26 * u, height: 7 * u))
        for sx: CGFloat in [-1, 1] {
            ctx.fill(CGRect(x: nmrCX + sx * 34 * u - 6 * u, y: magnetTopY + 5 * u,
                            width: 12 * u, height: 20 * u))
            ctx.fill(CGRect(x: nmrCX + sx * 34 * u - 9 * u, y: magnetTopY + 25 * u,
                            width: 18 * u, height: 6 * u))
        }
        // Status lights: green blink normally, angry red during a quench
        let blink = (tick / 20) % 2 == 0
        let ok = quenchT < 0
        ctx.setFillColor(ok
            ? CGColor(red: 0.3, green: 0.9, blue: 0.4, alpha: blink ? 1 : 0.3)
            : CGColor(red: 0.95, green: 0.2, blue: 0.15, alpha: blink ? 1 : 0.4))
        ctx.fillEllipse(in: CGRect(x: nmrCX - 24 * u, y: bodyBot + 30 * u,
                                   width: 7 * u, height: 7 * u))
        ctx.fillEllipse(in: CGRect(x: nmrCX + 17 * u, y: bodyBot + 30 * u,
                                   width: 7 * u, height: 7 * u))
        ctx.restoreGState()
        // Ladder for sample changes, leaning ambitions of a short person —
        // drawn OUTSIDE the flight transform: it stands on the floor and,
        // unlike the magnet, was never certified for flight.
        let lax = nmrCX + 96 * u
        ctx.setStrokeColor(CGColor(red: 0.80, green: 0.62, blue: 0.15, alpha: 1))
        ctx.setLineWidth(5 * u)
        ctx.setLineCap(.round)
        ctx.move(to: CGPoint(x: lax - 26 * u, y: floorY))
        ctx.addLine(to: CGPoint(x: lax - 4 * u, y: floorY + 190 * u))
        ctx.strokePath()
        ctx.move(to: CGPoint(x: lax + 28 * u, y: floorY))
        ctx.addLine(to: CGPoint(x: lax + 2 * u, y: floorY + 190 * u))
        ctx.strokePath()
        ctx.setLineWidth(3.5 * u)
        for i in 1...4 {
            let fy = floorY + CGFloat(i) * 42 * u
            let f = CGFloat(i) / 4.6
            ctx.move(to: CGPoint(x: lax - 26 * u + 22 * u * f, y: fy))
            ctx.addLine(to: CGPoint(x: lax + 28 * u - 26 * u * f, y: fy))
            ctx.strokePath()
        }
        // Console the chemist stands at, monitor up at chest height
        let conX = nmrCX - 150 * u
        ctx.setFillColor(CGColor(red: 0.40, green: 0.42, blue: 0.47, alpha: 1))
        ctx.fill(CGRect(x: conX - 20 * u, y: floorY, width: 40 * u, height: 150 * u))
        ctx.setFillColor(CGColor(red: 0.18, green: 0.19, blue: 0.23, alpha: 1))
        ctx.fill(CGRect(x: conX - 30 * u, y: floorY + 150 * u,
                        width: 60 * u, height: 48 * u))
        let screenOn = (tick / 45) % 2 == 0
        ctx.setFillColor(screenOn
            ? CGColor(red: 0.25, green: 0.85, blue: 0.45, alpha: 1)
            : CGColor(red: 0.95, green: 0.70, blue: 0.2, alpha: 1))
        ctx.fill(CGRect(x: conX - 25 * u, y: floorY + 156 * u,
                        width: 50 * u, height: 36 * u))
        // A trace wiggling across the screen, like it's acquiring
        ctx.setStrokeColor(CGColor(red: 0.05, green: 0.15, blue: 0.08, alpha: 0.8))
        ctx.setLineWidth(1.4 * u)
        ctx.move(to: CGPoint(x: conX - 22 * u, y: floorY + 172 * u))
        for i in 1...10 {
            let sx = conX - 22 * u + CGFloat(i) * 4.4 * u
            let sy = floorY + 172 * u
                + sin(CGFloat(i) * 1.7 + CGFloat(tick) * 0.15) * 7 * u
            ctx.addLine(to: CGPoint(x: sx, y: sy))
        }
        ctx.strokePath()
    }

    private func drawCeilingTubes(_ ctx: CGContext) {
        for t in ceilingTubes {
            let base = CGPoint(x: t.x, y: ceilY)
            let tip = CGPoint(x: t.x + t.tilt * 16 * u, y: ceilY - 15 * u)
            ctx.setStrokeColor(CGColor(red: 0.88, green: 0.92, blue: 0.98, alpha: 0.95))
            ctx.setLineWidth(3 * u)
            ctx.setLineCap(.round)
            ctx.move(to: base)
            ctx.addLine(to: tip)
            ctx.strokePath()
            let c = capColors[t.cap]
            ctx.setFillColor(CGColor(red: c.0, green: c.1, blue: c.2, alpha: 1))
            ctx.fillEllipse(in: CGRect(x: tip.x - 3 * u, y: tip.y - 3 * u,
                                       width: 6 * u, height: 6 * u))
        }
    }

    private func drawFlyingTube(_ ctx: CGContext) {
        ctx.setStrokeColor(CGColor(red: 0.9, green: 0.94, blue: 1.0, alpha: 0.95))
        ctx.setLineWidth(3 * u)
        ctx.setLineCap(.round)
        ctx.move(to: CGPoint(x: tubeX, y: tubeY))
        ctx.addLine(to: CGPoint(x: tubeX, y: tubeY + 16 * u))
        ctx.strokePath()
        // Motion streaks
        ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.35))
        ctx.setLineWidth(1.4 * u)
        for dx in [-5 * u, 5 * u] {
            ctx.move(to: CGPoint(x: tubeX + dx, y: tubeY - 18 * u))
            ctx.addLine(to: CGPoint(x: tubeX + dx, y: tubeY - 4 * u))
            ctx.strokePath()
        }
    }

    /// The grad student — properly tall, so the bench hits at the waist.
    /// Cartoon-inked: fills with dark outlines, knee-length coat, shoes,
    /// ear, nose, a mouth that goes "o" in a crisis. Origin at the feet;
    /// +x is the way they're facing.
    private func drawChar(_ ctx: CGContext) {
        let s = u * 2.9
        ctx.saveGState()
        ctx.translateBy(x: charX, y: floorY + charYOff)
        ctx.scaleBy(x: facing, y: 1)
        if fallTilt > 0 { ctx.rotate(by: fallTilt) }
        let onLadder = refill == .climbing || refill == .hooking
            || refill == .descending || refill == .tankTip

        let moving = state == .walking || state == .fleeing
        let swing = moving ? sin(walkPhase) * (state == .fleeing ? 13 : 9) : 0
        // At the hood they work the way you actually work a hood: back to
        // the room, arms in past the sash.
        let backTurned = state == .working && station == .hood

        let skin = CGColor(red: 0.96, green: 0.80, blue: 0.62, alpha: 1)
        let skinEdge = CGColor(red: 0.70, green: 0.52, blue: 0.36, alpha: 1)
        let coat = CGColor(red: 0.96, green: 0.96, blue: 0.99, alpha: 1)
        let coatShade = CGColor(red: 0.80, green: 0.82, blue: 0.88, alpha: 1)
        let ink = CGColor(red: 0.16, green: 0.15, blue: 0.20, alpha: 1)
        let pants = CGColor(red: 0.28, green: 0.33, blue: 0.46, alpha: 1)
        let shirt = CGColor(red: 0.24, green: 0.62, blue: 0.60, alpha: 1)

        ctx.setLineCap(.round)
        ctx.setLineJoin(.round)

        let seated = state == .typing || state == .sleeping
        if seated {
            // In the chair: thighs level with the seat, shins down
            ctx.setStrokeColor(pants)
            ctx.setLineWidth(8 * s)
            for lr: CGFloat in [1, -1] {
                let off = lr * 1.6 * s
                ctx.move(to: CGPoint(x: 0, y: 24 * s))
                ctx.addLine(to: CGPoint(x: 12 * s + off, y: 23 * s))
                ctx.addLine(to: CGPoint(x: 13.5 * s + off, y: 5 * s))
                ctx.strokePath()
                ctx.setFillColor(ink)
                ctx.fillEllipse(in: CGRect(x: 10 * s + off, y: 0,
                                           width: 13 * s, height: 6.5 * s))
            }
            // Torso drops to the seat; the coat folds at the lap instead
            // of pooling on the floor.
            ctx.translateBy(x: 0, y: -21.5 * s)
            ctx.clip(to: CGRect(x: -60 * s, y: 39 * s,
                                width: 120 * s, height: 200 * s))
        } else {
            // Legs with shoes; shins show below the knee-length coat
            for lr: CGFloat in [1, -1] {
                let fx = (moving ? swing * lr : 5 * lr) * s
                ctx.setStrokeColor(pants)
                ctx.setLineWidth(8 * s)
                ctx.move(to: CGPoint(x: 0, y: 45 * s))
                ctx.addLine(to: CGPoint(x: fx, y: 6 * s))
                ctx.strokePath()
                ctx.setFillColor(ink)
                ctx.fillEllipse(in: CGRect(x: fx - 4 * s, y: 0,
                                           width: 13 * s, height: 6.5 * s))
            }
        }

        // Knee-length coat, flared at the hem, back edge in shadow
        let body = CGMutablePath()
        body.move(to: CGPoint(x: -15 * s, y: 24 * s))
        body.addQuadCurve(to: CGPoint(x: -11 * s, y: 68 * s),
                          control: CGPoint(x: -14 * s, y: 48 * s))
        body.addQuadCurve(to: CGPoint(x: 0, y: 76 * s),
                          control: CGPoint(x: -9 * s, y: 75 * s))
        body.addQuadCurve(to: CGPoint(x: 11 * s, y: 68 * s),
                          control: CGPoint(x: 9 * s, y: 75 * s))
        body.addQuadCurve(to: CGPoint(x: 15 * s, y: 24 * s),
                          control: CGPoint(x: 14 * s, y: 48 * s))
        body.closeSubpath()
        ctx.addPath(body)
        ctx.setFillColor(coat)
        ctx.fillPath()
        ctx.saveGState()
        ctx.addPath(body)
        ctx.clip()
        ctx.setFillColor(coatShade)
        ctx.fill(CGRect(x: -16 * s, y: 22 * s, width: 5.5 * s, height: 56 * s))
        ctx.restoreGState()
        ctx.addPath(body)
        ctx.setStrokeColor(ink)
        ctx.setLineWidth(1.5 * s)
        ctx.strokePath()
        if backTurned {
            // Just the center back seam and yoke — coats are boring from
            // behind, that's how you know it's behind.
            ctx.setStrokeColor(coatShade)
            ctx.setLineWidth(1.1 * s)
            ctx.move(to: CGPoint(x: 0, y: 26 * s))
            ctx.addLine(to: CGPoint(x: 0, y: 70 * s))
            ctx.strokePath()
            ctx.move(to: CGPoint(x: -11 * s, y: 64 * s))
            ctx.addLine(to: CGPoint(x: 11 * s, y: 64 * s))
            ctx.strokePath()
        } else {
        // Shirt in the collar V, placket, buttons, pen pocket
        let vee = CGMutablePath()
        vee.move(to: CGPoint(x: -4 * s, y: 73 * s))
        vee.addLine(to: CGPoint(x: 2 * s, y: 61 * s))
        vee.addLine(to: CGPoint(x: 8 * s, y: 72 * s))
        vee.closeSubpath()
        ctx.addPath(vee)
        ctx.setFillColor(shirt)
        ctx.fillPath()
        ctx.setStrokeColor(coatShade)
        ctx.setLineWidth(1.1 * s)
        ctx.move(to: CGPoint(x: 2 * s, y: 61 * s))
        ctx.addLine(to: CGPoint(x: 2 * s, y: 27 * s))
        ctx.strokePath()
        ctx.setFillColor(ink)
        for by: CGFloat in [34, 43, 52] {
            ctx.fillEllipse(in: CGRect(x: 3.4 * s, y: by * s,
                                       width: 1.9 * s, height: 1.9 * s))
        }
        ctx.setStrokeColor(coatShade)
        ctx.stroke(CGRect(x: -10 * s, y: 52 * s, width: 6.5 * s, height: 7 * s))
        ctx.setStrokeColor(CGColor(red: 0.85, green: 0.25, blue: 0.20, alpha: 1))
        ctx.setLineWidth(1.3 * s)
        ctx.move(to: CGPoint(x: -8 * s, y: 57 * s))
        ctx.addLine(to: CGPoint(x: -8 * s, y: 62 * s))
        ctx.strokePath()
        }

        // Arms: inked sleeves ending in hands, posed by mood; each hangs
        // from the shoulder on its own side so they don't cross the coat.
        func arm(to end: CGPoint) {
            let sh = CGPoint(x: end.x >= 0 ? 8 * s : -8 * s, y: 68 * s)
            ctx.setStrokeColor(ink)
            ctx.setLineWidth(8.6 * s)
            ctx.move(to: sh)
            ctx.addLine(to: end)
            ctx.strokePath()
            ctx.setStrokeColor(coat)
            ctx.setLineWidth(6.2 * s)
            ctx.move(to: sh)
            ctx.addLine(to: end)
            ctx.strokePath()
            ctx.setFillColor(skin)
            ctx.fillEllipse(in: CGRect(x: end.x - 3.5 * s, y: end.y - 3.5 * s,
                                       width: 7 * s, height: 7 * s))
            ctx.setStrokeColor(skinEdge)
            ctx.setLineWidth(0.9 * s)
            ctx.strokeEllipse(in: CGRect(x: end.x - 3.5 * s, y: end.y - 3.5 * s,
                                         width: 7 * s, height: 7 * s))
        }
        if onLadder {
            // Gripping rungs, one hand busy with hose or port
            arm(to: CGPoint(x: 14 * s,
                            y: (86 + sin(climbH * 22) * 5) * s))
            arm(to: CGPoint(x: 10 * s,
                            y: (72 + cos(climbH * 22) * 5) * s))
        } else if backTurned {
            // Reaching in past the sash — or, on a schedule known only to
            // them, tending an itch through the lab coat.
            let scratching = (tick / 140) % 4 == 0
            if scratching {
                arm(to: CGPoint(x: 10 * s, y: (66 + sin(CGFloat(tick) * 0.22) * 3) * s))
                arm(to: CGPoint(x: (-2 + sin(CGFloat(tick) * 0.4) * 2.8) * s,
                                y: 41 * s))
            } else {
                arm(to: CGPoint(x: -12 * s, y: (68 + sin(CGFloat(tick) * 0.24) * 4) * s))
                arm(to: CGPoint(x: 12 * s, y: (66 + cos(CGFloat(tick) * 0.24) * 4) * s))
            }
        } else {
        switch state {
        case .working:
            arm(to: CGPoint(x: -9 * s, y: 46 * s))
            arm(to: CGPoint(x: 25 * s, y: (60 + sin(CGFloat(tick) * 0.25) * 5) * s))
        case .reacting:
            arm(to: CGPoint(x: -16 * s, y: 104 * s))
            arm(to: CGPoint(x: 20 * s, y: 104 * s))
        case .fleeing:
            arm(to: CGPoint(x: (15 + swing * 0.4) * s, y: 66 * s))
            arm(to: CGPoint(x: (-14 - swing * 0.4) * s, y: 60 * s))
        case .staring:
            arm(to: CGPoint(x: -9 * s, y: 46 * s))
            arm(to: CGPoint(x: 12 * s, y: 100 * s))  // hand shading the eyes
        case .typing:
            // Typing slows as the sandman closes in
            let prog = min(1, CGFloat(typingT) / CGFloat(max(sleepAt, 1)))
            let rate = 0.9 - 0.62 * prog
            let amp = (2.4 - 1.6 * prog) * s
            arm(to: CGPoint(x: 13 * s,
                            y: 62 * s + sin(CGFloat(tick) * rate) * amp))
            arm(to: CGPoint(x: 18 * s,
                            y: 61 * s + cos(CGFloat(tick) * rate) * amp))
        case .sleeping:
            arm(to: CGPoint(x: 14 * s, y: 60 * s))
            arm(to: CGPoint(x: 18 * s, y: 59 * s))
        case .walking, .idling:
            // No absent-minded head-scratching while mid-fall or prone.
            let scratch = state == .idling && fallTilt <= 0
                && refill == .none && (tick / 90) % 3 == 0
            arm(to: CGPoint(x: (-9 - swing * 0.3) * s, y: 44 * s))
            arm(to: scratch
                ? CGPoint(x: 8 * s, y: (105 + sin(CGFloat(tick) * 0.5) * 2) * s)
                : CGPoint(x: (9 + swing * 0.3) * s, y: 44 * s))
        }
        }

        // Neck and head, drooping toward the keyboard as sleep wins
        ctx.setFillColor(skin)
        ctx.fill(CGRect(x: -1 * s, y: 72 * s, width: 6 * s, height: 10 * s))
        var headC = CGPoint(x: 3 * s, y: 93 * s)
        if state == .typing {
            let prog = min(1, CGFloat(typingT) / CGFloat(max(sleepAt, 1)))
            headC.x += prog * 4 * s
            headC.y -= prog * prog * 8 * s
                + max(0, sin(CGFloat(tick) * 0.05)) * prog * 3 * s
        } else if state == .sleeping {
            headC.x += 6 * s
            headC.y -= 13 * s - sin(CGFloat(tick) * 0.06) * 1.5 * s
        }
        let hr = 11 * s
        ctx.setFillColor(skin)
        ctx.fillEllipse(in: CGRect(x: headC.x - hr, y: headC.y - hr,
                                   width: 2 * hr, height: 2 * hr))
        ctx.setStrokeColor(skinEdge)
        ctx.setLineWidth(1.1 * s)
        ctx.strokeEllipse(in: CGRect(x: headC.x - hr, y: headC.y - hr,
                                     width: 2 * hr, height: 2 * hr))
        if backTurned {
            // Back of the head: ears at both edges, a wall of hair, the
            // goggle strap, and the bun dead center.
            ctx.setFillColor(skin)
            for sx: CGFloat in [-1, 1] {
                ctx.fillEllipse(in: CGRect(x: headC.x + sx * hr - 2.3 * s,
                                           y: headC.y - 2.4 * s,
                                           width: 4.6 * s, height: 5.6 * s))
            }
            let hair = CGColor(red: 0.35, green: 0.23, blue: 0.13, alpha: 1)
            ctx.setFillColor(hair)
            ctx.fillEllipse(in: CGRect(x: headC.x - 10 * s, y: headC.y - 7 * s,
                                       width: 20 * s, height: 18.5 * s))
            ctx.setStrokeColor(ink)
            ctx.setLineWidth(1.8 * s)
            ctx.move(to: CGPoint(x: headC.x - 10.5 * s, y: headC.y + 1.5 * s))
            ctx.addLine(to: CGPoint(x: headC.x + 10.5 * s, y: headC.y + 1.5 * s))
            ctx.strokePath()
            ctx.setFillColor(hair)
            ctx.fillEllipse(in: CGRect(x: headC.x - 4.5 * s, y: headC.y - 2 * s,
                                       width: 9 * s, height: 9 * s))
            ctx.setStrokeColor(CGColor(red: 0.22, green: 0.14, blue: 0.07, alpha: 1))
            ctx.setLineWidth(1 * s)
            ctx.strokeEllipse(in: CGRect(x: headC.x - 4.5 * s, y: headC.y - 2 * s,
                                         width: 9 * s, height: 9 * s))
        } else {
        // Ear at the back, nose on the leading edge
        ctx.setFillColor(skin)
        ctx.fillEllipse(in: CGRect(x: headC.x - hr - 1.6 * s, y: headC.y - 2.4 * s,
                                   width: 4.6 * s, height: 5.6 * s))
        ctx.setStrokeColor(skinEdge)
        ctx.strokeEllipse(in: CGRect(x: headC.x - hr - 1.6 * s, y: headC.y - 2.4 * s,
                                     width: 4.6 * s, height: 5.6 * s))
        ctx.setFillColor(skin)
        ctx.fillEllipse(in: CGRect(x: headC.x + hr - 1.6 * s, y: headC.y - 1.4 * s,
                                   width: 4.6 * s, height: 4.2 * s))
        ctx.setStrokeColor(skinEdge)
        ctx.strokeEllipse(in: CGRect(x: headC.x + hr - 1.6 * s, y: headC.y - 1.4 * s,
                                     width: 4.6 * s, height: 4.2 * s))
        // Mouth: a small smile, or an "o" of horror
        let ink90 = CGColor(red: 0.16, green: 0.15, blue: 0.20, alpha: 0.9)
        if state == .reacting || state == .fleeing {
            ctx.setFillColor(ink90)
            ctx.fillEllipse(in: CGRect(x: headC.x + 4.5 * s, y: headC.y - 7 * s,
                                       width: 3.6 * s, height: 4.6 * s))
        } else {
            ctx.setStrokeColor(ink90)
            ctx.setLineWidth(1.1 * s)
            ctx.move(to: CGPoint(x: headC.x + 3.5 * s, y: headC.y - 5 * s))
            ctx.addQuadCurve(to: CGPoint(x: headC.x + 8.5 * s, y: headC.y - 3.2 * s),
                             control: CGPoint(x: headC.x + 6.5 * s, y: headC.y - 5.8 * s))
            ctx.strokePath()
        }
        // Neutral shag with a little bun at the back: could be anyone in
        // this lab, which is the point.
        let hair = CGColor(red: 0.35, green: 0.23, blue: 0.13, alpha: 1)
        let cap = CGMutablePath()
        cap.addArc(center: headC, radius: hr + 1.1 * s,
                   startAngle: 0.5, endAngle: .pi + 1.0, clockwise: false)
        cap.closeSubpath()
        ctx.addPath(cap)
        ctx.setFillColor(hair)
        ctx.fillPath()
        let fringe = CGMutablePath()
        fringe.move(to: CGPoint(x: headC.x + 9.5 * s, y: headC.y + 6.5 * s))
        fringe.addLine(to: CGPoint(x: headC.x + 11 * s, y: headC.y + 2.5 * s))
        fringe.addLine(to: CGPoint(x: headC.x + 5.5 * s, y: headC.y + 5 * s))
        fringe.closeSubpath()
        ctx.addPath(fringe)
        ctx.fillPath()
        ctx.setFillColor(hair)
        ctx.fillEllipse(in: CGRect(x: headC.x - hr - 5.5 * s, y: headC.y + 2 * s,
                                   width: 9 * s, height: 9 * s))
        // Goggle strap and lens, with a glint
        ctx.setStrokeColor(ink)
        ctx.setLineWidth(1.8 * s)
        ctx.move(to: CGPoint(x: headC.x - 9.5 * s, y: headC.y + 2.5 * s))
        ctx.addLine(to: CGPoint(x: headC.x + 6 * s, y: headC.y + 2.5 * s))
        ctx.strokePath()
        let lensY = state == .staring ? headC.y + 6 * s : headC.y + 2.5 * s
        let lens = CGRect(x: headC.x + 4.5 * s - 4.8 * s, y: lensY - 4.8 * s,
                          width: 9.6 * s, height: 9.6 * s)
        ctx.setFillColor(CGColor(red: 0.75, green: 0.88, blue: 0.98, alpha: 0.95))
        ctx.fillEllipse(in: lens)
        ctx.setStrokeColor(ink)
        ctx.setLineWidth(1.5 * s)
        ctx.strokeEllipse(in: lens)
        if state == .sleeping {
            // Eyes shut behind the lens
            ctx.setStrokeColor(CGColor(red: 0.1, green: 0.1, blue: 0.15, alpha: 1))
            ctx.setLineWidth(1.3 * s)
            ctx.move(to: CGPoint(x: headC.x + 2.5 * s, y: lensY))
            ctx.addLine(to: CGPoint(x: headC.x + 7 * s, y: lensY))
            ctx.strokePath()
        } else {
            ctx.setFillColor(CGColor(red: 0.1, green: 0.1, blue: 0.15, alpha: 1))
            ctx.fillEllipse(in: CGRect(x: headC.x + 5.5 * s, y: lensY - 1.2 * s,
                                       width: 2.8 * s, height: 2.8 * s))
        }
        ctx.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.8))
        ctx.fillEllipse(in: CGRect(x: lens.minX + 1.6 * s, y: lens.maxY - 3.6 * s,
                                   width: 2.2 * s, height: 2.2 * s))
        }

        // The cartoon soot face: everything blackened, hair frazzled into
        // spikes, wide blinking eyes, smoke curling off the head.
        if sootT >= 0 {
            ctx.setFillColor(CGColor(red: 0.14, green: 0.13, blue: 0.15, alpha: 0.9))
            ctx.fillEllipse(in: CGRect(x: headC.x - hr - 1.5 * s,
                                       y: headC.y - hr - 1.5 * s,
                                       width: 2 * hr + 3 * s,
                                       height: 2 * hr + 3 * s))
            ctx.setStrokeColor(ink)
            ctx.setLineWidth(1.7 * s)
            for i in 0..<6 {
                let a = 0.5 + CGFloat(i) * 0.42
                ctx.move(to: CGPoint(x: headC.x + cos(a) * (hr + 1 * s),
                                     y: headC.y + sin(a) * (hr + 1 * s)))
                ctx.addLine(to: CGPoint(x: headC.x + cos(a) * (hr + 6.5 * s),
                                        y: headC.y + sin(a) * (hr + 6.5 * s)))
            }
            ctx.strokePath()
            // Stunned eyes, blinking through the grime
            if (tick / 14) % 5 != 0 {
                for ex: CGFloat in [0.5, 7] {
                    let er = CGRect(x: headC.x + ex * s - 2.9 * s,
                                    y: headC.y + 1.5 * s - 3.4 * s,
                                    width: 5.8 * s, height: 6.8 * s)
                    ctx.setFillColor(CGColor(red: 1, green: 1, blue: 0.96, alpha: 1))
                    ctx.fillEllipse(in: er)
                    ctx.setFillColor(CGColor(red: 0.1, green: 0.1, blue: 0.12, alpha: 1))
                    ctx.fillEllipse(in: CGRect(x: er.midX - 1.2 * s,
                                               y: er.midY - 1.4 * s,
                                               width: 2.4 * s, height: 2.8 * s))
                }
            }
            // Smoke wisps off the scalp
            for k in 0..<2 {
                let f = (CGFloat(tick) * 0.02 + CGFloat(k) * 0.5)
                    .truncatingRemainder(dividingBy: 1)
                let wr = (1.6 + 2.4 * f) * s
                ctx.setFillColor(CGColor(red: 0.55, green: 0.55, blue: 0.58,
                                         alpha: 0.6 * (1 - f)))
                ctx.fillEllipse(in: CGRect(
                    x: headC.x - 3 * s + CGFloat(k) * 6 * s
                        + sin(f * 8 + CGFloat(k) * 3) * 2 * s - wr,
                    y: headC.y + hr + 2 * s + f * 16 * s - wr,
                    width: 2 * wr, height: 2 * wr))
            }
        }
        ctx.restoreGState()
    }

    private func drawParticles(_ ctx: CGContext) {
        for p in particles {
            let fade = CGFloat(p.life) / CGFloat(p.maxLife)
            ctx.setFillColor(CGColor(red: p.red, green: p.green, blue: p.blue,
                                     alpha: (p.buoyant ? 0.55 : 0.9) * fade))
            ctx.fillEllipse(in: CGRect(x: p.x - p.r, y: p.y - p.r,
                                       width: 2 * p.r, height: 2 * p.r))
        }
    }

    public override var hasConfigureSheet: Bool { false }
    public override var configureSheet: NSWindow? { nil }
}

#if HARNESS
let harnessMake: (NSRect) -> ScreenSaverView = { CastawayChemistView(frame: $0, isPreview: false)! }
#endif
