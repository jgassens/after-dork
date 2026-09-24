using System.Runtime.InteropServices;
using AfterDork.Graphics;

namespace AfterDork.Savers;

// Castaway Chemist — an homage to Sierra's "Johnny Castaway", except the
// island is a chemistry lab and the castaway is a grad student who is never
// allowed to leave. They shuffle between the fume hood, the rotovap, and the
// NMR, and slapstick follows them: the hood detonates in colored smoke, the
// rotovap drops its flask into the bath, and the NMR either quenches or
// fires their sample into the ceiling, where the tubes accumulate as a
// permanent record of their sins.
// Port of CastawayChemist/CastawayChemist.swift. This file holds the state
// and the brain; CastawayChemist.Draw.cs holds the drawing.

public sealed partial class CastawayChemistView : SaverView
{
    static double rnd(double lo, double hi) => Rng.Range(lo, hi);
    static bool chance(double p) => Rng.Chance(p);

    /// <summary>
    /// Test hook: receives "event detail tick=N" strings at each notable
    /// state-machine event. Null (the default) costs one null check.
    /// </summary>
    public static Action<string>? Trace;

    void trace(string what)
    {
        var t = Trace;
        if (t is not null) t($"{what} tick={tick}");
    }

    struct Particle
    {
        public double x, y;
        public double vx, vy;
        public double r;
        public double growth;
        public int life;
        public int maxLife;
        public double red, green, blue;
        public bool buoyant;  // smoke rises and grows; droplets fall under gravity

        public Particle(double x, double y, double vx, double vy, double r, double growth,
                        int life, int maxLife, double red, double green, double blue, bool buoyant)
        {
            this.x = x; this.y = y; this.vx = vx; this.vy = vy; this.r = r; this.growth = growth;
            this.life = life; this.maxLife = maxLife;
            this.red = red; this.green = green; this.blue = blue; this.buoyant = buoyant;
        }
    }

    struct CeilingTube
    {
        public double x;
        public double tilt;
        public int cap;
    }

    struct Zzz
    {
        public double x, y;
        public int age;
    }

    enum Station { hood, rotovap, nmr, desk }
    static readonly Station[] allStations = [Station.hood, Station.rotovap, Station.nmr, Station.desk];

    enum CharState
    {
        walking, working, reacting, fleeing, staring, idling, typing, sleeping
    }

    /// The helium refill saga, phase by phase. Two coin flips stand between the
    /// chemist and success, and neither of them is friendly.
    enum RefillPhase
    {
        none, exiting, entering, parking, connecting, toLadder, climbing,
        falling, down, rising, hooking, tankTip, tankFly, descending,
        toValve, opening, filling, nmrFly, landing, cleanup
    }

    long tick = 0;
    double chaos = 1;
    bool quenchOn = true;

    // The grad student
    double charX = 300;
    double targetX = 300;
    double facing = 1;
    CharState state = CharState.idling;
    int stateTimer = 90;
    Station? station;
    double walkPhase = 0;

    // Prop state
    bool rotoAttached = true;
    int rotoDropT = -1;
    double bobPhase = 0;
    int boomT = -1;
    double boomHue = 0;
    int sootT = -1;        // >= 0: wearing the cartoon soot face
    double? pendingFleeX;  // run here once the stun wears off
    int typingT = 0;       // frames at the laptop this sitting
    int sleepAt = 500;     // typing frames until sleep claims them
    int sleepT = 0;
    int wakeAt = 700;      // nap length, rolled once at lights-out
    readonly List<Zzz> zzz = new();

    // The refill saga
    RefillPhase refill = RefillPhase.none;
    int rfT = 0;
    long heliumT = 0;  // frames since last fill; the magnet is thirsty
    double cylX = 0;
    double cylTilt = 0;
    bool cylVisible = false;
    bool onDolly = false;
    bool hoseToHand = false;
    bool hoseToPort = false;
    double climbH = 0;
    double charYOff = 0;
    double fallTilt = 0;
    bool flying = false;
    bool flyIsNMR = false;
    CGVector flyOff = CGVector.Zero;
    CGVector flyVel = CGVector.Zero;
    double flyAngle = 0;  // drawn tilt; decays smoothly on landing
    bool artAskew = false;

    double cylParkX => nmrCX - 92 * u;
    double ladderX => nmrCX + 96 * u;
    CGPoint tankHome => new(cylX, floorY + 130 * u);
    CGPoint nmrHome => new(nmrCX, floorY + 220 * u);
    int quenchT = -1;
    double tubeX = 0;
    double tubeY = -1;  // < 0 means no tube in flight
    readonly List<CeilingTube> ceilingTubes = new();
    readonly List<Particle> particles = new();

    static readonly (double, double, double)[] capColors =
    [
        (0.85, 0.20, 0.22), (0.20, 0.45, 0.90), (0.25, 0.72, 0.35),
        (0.92, 0.78, 0.20),
    ];

    // MARK: - Layout

    double W => Math.Max(Bounds.Width, 640);
    double H => Math.Max(Bounds.Height, 400);
    // Scene unit: divisor > 720 shrinks all furniture and the chemist
    // together, buying elbow room between stations.
    double u => H / 860;
    // Slim floor and ceiling bands: the lab reads long, like a corridor
    // you will spend seven years in.
    double floorY => H * 0.09;
    double ceilY => H * 0.93;
    double hoodCX => W * 0.13;
    double benchY => floorY + 128 * u;
    double bathCX => W * 0.475;
    double deskX => W * 0.67;
    double nmrCX => W * 0.90;
    double magnetTopY => floorY + 400 * u;

    double stationX(Station s) => s switch
    {
        Station.hood => hoodCX + 30 * u,  // squarely in front, back to us
        Station.rotovap => bathCX - 165 * u,
        Station.nmr => nmrCX - 105 * u,
        _ => deskX - 58 * u,               // .desk
    };

    static double stationFacing(Station s) => 1;

    // MARK: - Lifecycle

    public CastawayChemistView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        setup();
    }

    void setup()
    {
        chaos = Math.Max(0.3, Math.Min(3, Settings.Value("CastawayChemist", "chaos", 1.0)));
        quenchOn = Settings.Flag("CastawayChemist", "quench", true);
        charX = W * 0.4;
        targetX = charX;
        state = CharState.idling;
        stateTimer = 60;
    }

    // MARK: - Brain

    void chooseNext()
    {
        // When the helium is overdue, duty overrides wanderlust: go check
        // the magnet, which is where the saga begins.
        if (refill == RefillPhase.none && (double)heliumT > 4500 / chaos)
        {
            station = Station.nmr;
            targetX = stationX(Station.nmr);
            state = CharState.walking;
            trace("chooseNext nmr(helium)");
            return;
        }
        if (chance(0.65))
        {
            var options = new List<Station>(4);
            foreach (var st in allStations) if (st != station) options.Add(st);
            var s = Rng.Element(options);
            station = s;
            targetX = stationX(s);
            state = CharState.walking;
            if (Trace is not null) trace($"chooseNext {s}");
        }
        else
        {
            station = null;
            state = CharState.idling;
            stateTimer = (int)rnd(80, 240);
            trace("chooseNext idle");
        }
    }

    void startBoom()
    {
        boomT = 0;
        boomHue = rnd(0, 1);
        sootT = 0;
        // Take it on the chin first: face the hood, stunned and sooty,
        // arms up — THEN run.
        facing = stationFacing(Station.hood);
        state = CharState.reacting;
        stateTimer = 70;
        station = null;
        pendingFleeX = Math.Min(charX + W * 0.32, W - 80 * u);
        trace("startBoom");
    }

    void startRotoDrop()
    {
        rotoAttached = false;
        rotoDropT = 0;
        if (state == CharState.working && station == Station.rotovap)
        {
            state = CharState.reacting;
            stateTimer = 100;
        }
        trace("startRotoDrop");
    }

    void launchTube()
    {
        tubeX = nmrCX;
        tubeY = magnetTopY + 56 * u;
        state = CharState.staring;
        stateTimer = 150;
        trace("launchTube");
    }

    void startQuench()
    {
        quenchT = 0;
        if (Math.Abs(charX - nmrCX) < W * 0.35)
        {
            state = CharState.fleeing;
            station = null;
            targetX = Math.Max(hoodCX + 200 * u, 80 * u);
            facing = -1;
        }
        trace("startQuench");
    }

    public override void AnimateOneFrame()
    {
        tick += 1;
        bobPhase += 0.12;

        heliumT += 1;
        if (refill != RefillPhase.none)
        {
            updateRefill();
        }
        else
        {
            switch (state)
            {
                case CharState.walking:
                case CharState.fleeing:
                {
                    double speed = (state == CharState.fleeing ? 8.5 : 4.2) * u;
                    walkPhase += state == CharState.fleeing ? 0.55 : 0.34;
                    if (Math.Abs(targetX - charX) <= speed)
                    {
                        charX = targetX;
                        if (state == CharState.fleeing || station is null)
                        {
                            state = CharState.idling;
                            stateTimer = (int)rnd(70, 160);
                        }
                        else if (station == Station.desk)
                        {
                            state = CharState.typing;
                            typingT = 0;
                            sleepAt = (int)rnd(420, 650);
                            facing = 1;
                            trace("typing");
                        }
                        else
                        {
                            state = CharState.working;
                            stateTimer = (int)rnd(320, 720);
                            facing = stationFacing(station!.Value);
                        }
                    }
                    else
                    {
                        facing = targetX > charX ? 1 : -1;
                        charX += facing * speed;
                    }
                    break;
                }
                case CharState.working:
                {
                    stateTimer -= 1;
                    double p = 0.005 * chaos;
                    if (station == Station.hood && boomT < 0 && chance(p)) startBoom();
                    if (station == Station.rotovap && rotoAttached && chance(p)) startRotoDrop();
                    // The helium clock: once the magnet is due, the current NMR
                    // session turns into the refill saga after a beat of gauge
                    // reading — and no other magnet shenanigans can preempt it.
                    if (station == Station.nmr && (double)heliumT > 4500 / chaos)
                    {
                        if (chance(0.03)) startRefill();
                    }
                    else if (station == Station.nmr && tubeY < 0 && chance(p))
                    {
                        launchTube();
                    }
                    if (stateTimer <= 0) chooseNext();
                    break;
                }
                case CharState.typing:
                    // Thesis words go in; consciousness drains out.
                    typingT += 1;
                    if (typingT >= sleepAt)
                    {
                        state = CharState.sleeping;
                        sleepT = 0;
                        // Drawn ONCE — re-rolling per frame collapses the range.
                        wakeAt = (int)rnd(650, 900);
                        trace("sleeping");
                    }
                    break;
                case CharState.sleeping:
                    sleepT += 1;
                    if (sleepT % 55 == 12)
                    {
                        zzz.Add(new Zzz { x = charX + facing * 32 * u, y = floorY + 250 * u, age = 0 });
                        trace("zzz");
                    }
                    if (sleepT > wakeAt)
                    {
                        state = CharState.idling;
                        stateTimer = 50;
                        trace("wake");
                    }
                    break;
                case CharState.reacting:
                case CharState.staring:
                case CharState.idling:
                    stateTimer -= 1;
                    if (stateTimer <= 0)
                    {
                        if (state == CharState.reacting && pendingFleeX is double fleeTo)
                        {
                            pendingFleeX = null;
                            station = null;
                            state = CharState.fleeing;
                            targetX = fleeTo;
                        }
                        else
                        {
                            chooseNext();
                        }
                    }
                    break;
            }

            // The magnet quenches on its own schedule, chemist or no chemist —
            // though deliberately never mid-saga: one catastrophe at a time.
            if (quenchOn && quenchT < 0 && chance(0.0005 * chaos)) startQuench();
        }

        // Ambient timers tick whether or not a saga is running: the soot
        // wears off eventually (the shame does not), and Z's age out.
        if (sootT >= 0)
        {
            sootT += 1;
            if (sootT > 400) sootT = -1;
        }
        var zs = CollectionsMarshal.AsSpan(zzz);
        for (int i = 0; i < zs.Length; i++) zs[i].age += 1;
        zzz.RemoveAll(z => z.age > 130);

        // Fume hood detonation: flash, then colored smoke pours out.
        if (boomT >= 0)
        {
            boomT += 1;
            if (boomT < 70 && boomT % 2 == 0)
            {
                var c = NSColor.CalibratedHue(boomHue, 0.7, 0.85, 1);
                particles.Add(new Particle(
                    x: hoodCX + rnd(-50, 50) * u, y: benchY + rnd(30, 60) * u,
                    vx: rnd(-0.5, 0.5) * u, vy: rnd(1.4, 2.6) * u,
                    r: rnd(6, 11) * u, growth: 0.22 * u,
                    life: 90, maxLife: 90,
                    red: c.RedComponent, green: c.GreenComponent,
                    blue: c.BlueComponent, buoyant: true));
            }
            if (boomT > 160) boomT = -1;
        }

        // Rotovap flask overboard: falls for 12 frames, splashes, bobs in
        // the bath a while, then gets quietly fished back on.
        if (rotoDropT >= 0)
        {
            rotoDropT += 1;
            if (rotoDropT == 12)
            {
                for (int k = 0; k < 9; k++)
                {
                    particles.Add(new Particle(
                        x: bathCX + 34 * u + rnd(-30, 30) * u, y: benchY + 68 * u,
                        vx: rnd(-1.6, 1.6) * u, vy: rnd(2.0, 4.2) * u,
                        r: rnd(2, 3.5) * u, growth: 0,
                        life: 42, maxLife: 42,
                        red: 0.55, green: 0.75, blue: 0.95, buoyant: false));
                }
            }
            if (rotoDropT > 280)
            {
                rotoAttached = true;
                rotoDropT = -1;
            }
        }

        // NMR tube en route to the ceiling.
        if (tubeY >= 0)
        {
            tubeY += 21 * u;
            if (tubeY >= ceilY)
            {
                double tx = tubeX + rnd(-6, 6) * u;
                double tt = rnd(-0.22, 0.22);
                int tc = Rng.Int(0, 4);
                ceilingTubes.Add(new CeilingTube { x = tx, tilt = tt, cap = tc });
                if (ceilingTubes.Count > 15) ceilingTubes.RemoveAt(0);
                tubeY = -1;
                trace("tubeCeiling");
            }
        }

        // Quench: the magnet vents its helium in great white plumes.
        if (quenchT >= 0)
        {
            quenchT += 1;
            if (quenchT < 210 && quenchT % 2 == 0)
            {
                particles.Add(new Particle(
                    x: nmrCX + rnd(-20, 20) * u, y: magnetTopY + 52 * u,
                    vx: rnd(-1.1, 1.1) * u, vy: rnd(2.2, 3.8) * u,
                    r: rnd(9, 16) * u, growth: 0.34 * u,
                    life: 110, maxLife: 110,
                    red: 0.96, green: 0.97, blue: 1.0, buoyant: true));
            }
            if (quenchT > 300) quenchT = -1;
        }

        var ps = CollectionsMarshal.AsSpan(particles);
        for (int i = 0; i < ps.Length; i++)
        {
            ref var pt = ref ps[i];
            pt.x += pt.vx;
            pt.y += pt.vy;
            if (pt.buoyant)
            {
                pt.r += pt.growth;
                // Smoke pools along the ceiling and drifts sideways —
                // clamped, or the compounding 1.05x rips it off-screen.
                if (pt.y > ceilY - pt.r * 0.4)
                {
                    pt.y = ceilY - pt.r * 0.4;
                    pt.vy = 0;
                    if (Math.Abs(pt.vx) < 3.5 * u) pt.vx *= 1.05;
                }
            }
            else
            {
                pt.vy -= 0.3 * u;
            }
            pt.life -= 1;
        }
        double uu = u, ww = W;
        particles.RemoveAll(q =>
            q.life <= 0 || q.y < 0 || q.x < -80 * uu || q.x > ww + 80 * uu);

        NeedsDisplay = true;
    }

    // MARK: - The refill saga

    void startRefill()
    {
        refill = RefillPhase.exiting;
        rfT = 0;
        station = null;
        // Keep the same-frame `stateTimer <= 0 → chooseNext()` check from
        // clobbering the station we just cleared.
        stateTimer = 100;
        zzz.Clear();
        trace("startRefill");
    }

    void rf(RefillPhase p)
    {
        refill = p;
        rfT = 0;
        if (Trace is not null) trace($"rf {p}");
    }

    void updateRefill()
    {
        rfT += 1;
        double walkSpeed = 3.6 * u;
        bool walk(double x)
        {
            state = CharState.walking;
            walkPhase += 0.34;
            if (Math.Abs(x - charX) <= walkSpeed) { charX = x; return true; }
            facing = x > charX ? 1 : -1;
            charX += facing * walkSpeed;
            return false;
        }
        switch (refill)
        {
            case RefillPhase.none:
                break;
            case RefillPhase.exiting:
                // Off the left edge: somewhere out there is a gas cylinder depot
                if (walk(-150 * u))
                {
                    cylVisible = true;
                    onDolly = true;
                    cylTilt = -0.22;
                    charX = -220 * u;
                    cylX = charX + 62 * u;
                    rf(RefillPhase.entering);
                }
                break;
            case RefillPhase.entering:
                _ = walk(cylParkX - 62 * u);
                cylX = charX + 62 * u;
                if (cylX >= cylParkX) { cylX = cylParkX; rf(RefillPhase.parking); }
                break;
            case RefillPhase.parking:
                state = CharState.working;
                facing = 1;
                cylTilt = Math.Min(0, -0.22 + rfT * 0.006);
                if (rfT > 45) { cylTilt = 0; onDolly = false; rf(RefillPhase.connecting); }
                break;
            case RefillPhase.connecting:
                state = CharState.working;
                facing = 1;
                if (rfT > 70) { hoseToHand = true; rf(RefillPhase.toLadder); }
                break;
            case RefillPhase.toLadder:
                if (walk(ladderX - 2 * u)) { climbH = 0; rf(RefillPhase.climbing); }
                break;
            case RefillPhase.climbing:
                state = CharState.working;
                facing = -1;
                climbH = Math.Min(1, climbH + 0.012);
                charYOff = climbH * 148 * u;
                walkPhase += 0.2;
                if (climbH >= 1)
                {
                    // First flip of the coin: gravity or glory. A fall tips the
                    // body away from the ladder (screen-left), so face +1 —
                    // mirrored drawing would rotate the torso off-screen right.
                    if (chance(0.5)) { trace("flip1 fall"); facing = 1; rf(RefillPhase.falling); }
                    else { trace("flip1 hook"); rf(RefillPhase.hooking); }
                }
                break;
            case RefillPhase.falling:
            {
                state = CharState.idling;
                double f = rfT / 22.0;
                charYOff = Math.Max(0, 148 * u * (1 - f * f));
                charX -= 1.5 * u;
                fallTilt = Math.Min(1.35, f * 1.35);
                if (charYOff <= 0) { charYOff = 0; rf(RefillPhase.down); }
                break;
            }
            case RefillPhase.down:
                state = CharState.idling;
                if (rfT > 55) rf(RefillPhase.rising);
                break;
            case RefillPhase.rising:
                state = CharState.idling;
                fallTilt = Math.Max(0, fallTilt - 0.12);
                if (fallTilt <= 0) { fallTilt = 0; rf(RefillPhase.toLadder); }
                break;
            case RefillPhase.hooking:
                state = CharState.working;
                facing = -1;
                if (rfT > 85)
                {
                    hoseToHand = false;
                    hoseToPort = true;
                    // Second flip: does the tank respect the hose?
                    if (chance(0.5)) { trace("flip2 tip"); rf(RefillPhase.tankTip); }
                    else { trace("flip2 fill"); rf(RefillPhase.descending); }
                }
                break;
            case RefillPhase.tankTip:
                state = CharState.reacting;
                facing = 1;
                cylTilt = Math.Min(1.45, rfT * 0.09);
                if (rfT > 22)
                {
                    // Valve snaps off. Newton takes it from here — seeding the
                    // flight from the TIPPED pose (center offset and heading
                    // along the tank's axis) so there is no one-frame teleport.
                    hoseToPort = false;
                    cylVisible = false;
                    flying = true;
                    flyIsNMR = false;
                    flyOff = new CGVector(-130 * u * Math.Sin(cylTilt),
                                          130 * u * (Math.Cos(cylTilt) - 1));
                    flyVel = new CGVector(-16 * u * Math.Sin(cylTilt),
                                          16 * u * Math.Cos(cylTilt));
                    flyAngle = cylTilt;
                    debrisBurst(new CGPoint(tankHome.X + flyOff.Dx,
                                            tankHome.Y + flyOff.Dy));
                    climbH = 0;
                    rf(RefillPhase.tankFly);
                }
                break;
            case RefillPhase.tankFly:
            case RefillPhase.nmrFly:
                charYOff = Math.Max(0, charYOff - 14 * u);
                if (rfT < 60)
                {
                    _ = walk(W * 0.07);
                }
                else
                {
                    state = CharState.reacting;
                    facing = 1;
                }
                updateFlight();
                if (rfT > (refill == RefillPhase.tankFly ? 330 : 400)) rf(RefillPhase.landing);
                break;
            case RefillPhase.descending:
                state = CharState.working;
                facing = -1;
                climbH = Math.Max(0, climbH - 0.015);
                charYOff = climbH * 148 * u;
                if (climbH <= 0) rf(RefillPhase.toValve);
                break;
            case RefillPhase.toValve:
                if (walk(cylX + 46 * u)) { facing = -1; rf(RefillPhase.opening); }
                break;
            case RefillPhase.opening:
                state = CharState.working;
                facing = -1;
                if (rfT > 60) rf(RefillPhase.filling);
                break;
            case RefillPhase.filling:
            {
                if (rfT < 110)
                {
                    state = CharState.staring;
                    facing = 1;
                }
                else if (rfT < 150)
                {
                    _ = walk(nmrCX - 240 * u);
                }
                else
                {
                    state = CharState.staring;
                    facing = 1;
                }
                // Vapor from the far port, ramping from burp to jet engine
                int interval = Math.Max(2, 9 - rfT / 40);
                if (rfT % interval == 0)
                {
                    particles.Add(new Particle(
                        x: nmrCX - 34 * u + rnd(-6, 6) * u,
                        y: magnetTopY + 30 * u,
                        vx: rnd(-1.5, -0.4) * u, vy: rnd(2.0, 3.6) * u,
                        r: rnd(6, 12) * u, growth: 0.3 * u,
                        life: 90, maxLife: 90,
                        red: 0.96, green: 0.97, blue: 1.0, buoyant: true));
                }
                if (rfT > 340)
                {
                    // The magnet has had enough of this
                    flying = true;
                    flyIsNMR = true;
                    flyOff = CGVector.Zero;
                    flyVel = new CGVector(-7 * u, 12 * u);
                    hoseToPort = false;
                    debrisBurst(nmrHome);
                    rf(RefillPhase.nmrFly);
                }
                break;
            }
            case RefillPhase.landing:
                state = CharState.reacting;
                facing = 1;
                flyOff = flyOff with { Dx = flyOff.Dx * 0.88 };
                flyOff = flyOff with { Dy = flyOff.Dy * 0.88 };
                flyAngle *= 0.85;  // settle upright instead of snapping there
                if (Math.Abs(flyOff.Dx) < 3 * u && Math.Abs(flyOff.Dy) < 3 * u)
                {
                    flyOff = CGVector.Zero;
                    flying = false;
                    if (!flyIsNMR) { cylVisible = true; cylTilt = 0; }
                    rf(RefillPhase.cleanup);
                }
                break;
            case RefillPhase.cleanup:
                state = CharState.idling;
                if (rfT > 50)
                {
                    // The scene refreshes: art rehung, glass swept, tank gone,
                    // no one speaks of it again.
                    artAskew = false;
                    cylVisible = false;
                    onDolly = false;
                    hoseToHand = false;
                    hoseToPort = false;
                    fallTilt = 0;
                    charYOff = 0;
                    particles.Clear();
                    refill = RefillPhase.none;
                    heliumT = 0;
                    trace("refillDone");
                    chooseNext();
                }
                break;
        }
    }

    void updateFlight()
    {
        var home = flyIsNMR ? nmrHome : tankHome;
        flyVel = flyVel with { Dx = flyVel.Dx + rnd(-1.3, 1.3) * u };
        flyVel = flyVel with { Dy = flyVel.Dy + rnd(-1.3, 1.3) * u };
        double sp = Math.Max(double.Hypot(flyVel.Dx, flyVel.Dy), 0.001);
        double maxSp = 14 * u;
        if (sp > maxSp) flyVel = new CGVector(flyVel.Dx * (maxSp / sp), flyVel.Dy * (maxSp / sp));
        flyOff = new CGVector(flyOff.Dx + flyVel.Dx, flyOff.Dy + flyVel.Dy);
        flyAngle = Math.Atan2(flyVel.Dy, flyVel.Dx) - Math.PI / 2;
        var p = new CGPoint(home.X + flyOff.Dx, home.Y + flyOff.Dy);
        bool bounced = false;
        if (p.X < 80 * u) { flyVel = flyVel with { Dx = Math.Abs(flyVel.Dx) }; bounced = true; }
        if (p.X > W - 80 * u) { flyVel = flyVel with { Dx = -Math.Abs(flyVel.Dx) }; bounced = true; }
        if (p.Y < floorY + 90 * u) { flyVel = flyVel with { Dy = Math.Abs(flyVel.Dy) }; bounced = true; }
        if (p.Y > H - 70 * u) { flyVel = flyVel with { Dy = -Math.Abs(flyVel.Dy) }; bounced = true; }
        if (bounced)
        {
            artAskew = true;
            debrisBurst(p);
        }
        // Exhaust screaming out behind it
        particles.Add(new Particle(
            x: p.X - flyVel.Dx * 1.6 + rnd(-4, 4) * u,
            y: p.Y - flyVel.Dy * 1.6 + rnd(-4, 4) * u,
            vx: -flyVel.Dx * 0.15, vy: -flyVel.Dy * 0.15,
            r: rnd(5, 9) * u, growth: 0.3 * u,
            life: 40, maxLife: 40,
            red: 0.95, green: 0.96, blue: 1.0, buoyant: true));
    }

    void debrisBurst(CGPoint p)
    {
        for (int k = 0; k < 7; k++)
        {
            var c = Rng.Element(capColors);
            particles.Add(new Particle(
                x: p.X + rnd(-10, 10) * u, y: p.Y + rnd(-10, 10) * u,
                vx: rnd(-5, 5) * u, vy: rnd(1, 7) * u,
                r: rnd(2, 3.5) * u, growth: 0,
                life: 55, maxLife: 55,
                red: c.Item1, green: c.Item2, blue: c.Item3, buoyant: false));
        }
        for (int k = 0; k < 3; k++)
        {
            particles.Add(new Particle(
                x: p.X, y: p.Y,
                vx: rnd(-1, 1) * u, vy: rnd(1, 2.5) * u,
                r: rnd(7, 12) * u, growth: 0.25 * u,
                life: 60, maxLife: 60,
                red: 0.6, green: 0.6, blue: 0.62, buoyant: true));
        }
    }
}
