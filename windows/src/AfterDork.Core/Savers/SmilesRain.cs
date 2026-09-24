using AfterDork.Graphics;

namespace AfterDork.Savers;

// SMILES Rain — the Matrix digital rain, except the falling glyphs are SMILES
// strings of famous molecules. Read a column top to bottom and you can name
// the compound; every so often the saver names one for you.
// Port of SmilesRain/SmilesRain.swift.

public sealed class SmilesRainView : SaverView
{
    static double Rnd(double lo, double hi) => Rng.Range(lo, hi);

    static readonly (string name, string smiles)[] molecules =
    [
        ("CAFFEINE", "Cn1cnc2c1c(=O)n(C)c(=O)n2C"),
        ("ASPIRIN", "CC(=O)Oc1ccccc1C(=O)O"),
        ("PARACETAMOL", "CC(=O)Nc1ccc(O)cc1"),
        ("IBUPROFEN", "CC(C)Cc1ccc(cc1)C(C)C(=O)O"),
        ("DOPAMINE", "NCCc1ccc(O)c(O)c1"),
        ("SEROTONIN", "NCCc1c[nH]c2ccc(O)cc12"),
        ("ADRENALINE", "CNCC(O)c1ccc(O)c(O)c1"),
        ("NICOTINE", "CN1CCCC1c1cccnc1"),
        ("VANILLIN", "COc1cc(C=O)ccc1O"),
        ("CAPSAICIN", "COc1cc(CNC(=O)CCCCC=CC(C)C)ccc1O"),
        ("GLUCOSE", "OCC1OC(O)C(O)C(O)C1O"),
        ("TNT", "Cc1c([N+](=O)[O-])cc([N+](=O)[O-])cc1[N+](=O)[O-]"),
        ("CUBANE", "C12C3C4C1C5C2C3C45"),
        ("PENICILLIN G", "CC1(C)SC2C(NC(=O)Cc3ccccc3)C(=O)N2C1C(=O)O"),
        ("CITRIC ACID", "OC(=O)CC(O)(C(=O)O)CC(=O)O"),
        ("UREA", "NC(N)=O"),
        ("BENZENE", "c1ccccc1"),
        ("ETHANOL", "CCO"),
        ("ACETONE", "CC(C)=O"),
        ("MENTHOL", "CC(C)C1CCC(C)CC1O"),
    ];

    // Array(molecules[i].smiles), precomputed once instead of per charAt call.
    static readonly char[][] smilesChars = Array.ConvertAll(molecules, m => m.smiles.ToCharArray());

    struct Drop
    {
        public double Head;
        public double Speed;
        public int Mol;
        public int Offset;
        public int Trail;
    }

    // A class so `reveal!.age += 1` mutates in place.
    sealed class Reveal
    {
        public string Text = "";
        public double X, Y;
        public int Age;
    }

    const double cellW = 14, cellH = 17;
    int cols = 0, rows = 0;
    Drop[] drops = [];
    readonly Dictionary<char, CGImage[]> glyphs = new();  // 9 brightness levels
    Reveal? reveal;
    double speedMul = 1;
    bool revealsOn = true;
    int revealTimer = 0;
    long tick = 0;

    public SmilesRainView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        Setup();
    }

    void Setup()
    {
        speedMul = Math.Max(0.3, Math.Min(3, Settings.Value("SmilesRain", "speed", 1.0)));
        revealsOn = Settings.Flag("SmilesRain", "reveals", true);
        double w = Math.Max(Bounds.Width, 640), h = Math.Max(Bounds.Height, 400);
        cols = (int)(w / cellW);
        rows = (int)(h / cellH) + 2;
        drops = new Drop[cols];
        for (int i = 0; i < cols; i++) drops[i] = MakeDrop(scattered: true);
        BuildGlyphs();
    }

    Drop MakeDrop(bool scattered)
    {
        double head = scattered ? Rnd(-30, rows) : -Rnd(0, 40);
        double speed = Rnd(0.28, 0.85) * speedMul;
        int mol = Rng.Int(0, molecules.Length);
        int offset = Rng.Int(0, 64);
        int trail = Rng.IntInclusive(13, 30);
        return new Drop { Head = head, Speed = speed, Mol = mol, Offset = offset, Trail = trail };
    }

    static NSFont MenloBold(double size) =>
        NSFont.Named("Menlo-Bold", size) ?? NSFont.MonospacedSystemFont(size, NSFontWeight.Bold);

    // Swift builds this font in every draw(); it never changes, so hoist it.
    static readonly NSFont revealFont = MenloBold(19);

    void BuildGlyphs()
    {
        var charset = new HashSet<char>();
        foreach (var m in molecules) foreach (char c in m.smiles) charset.Add(c);
        var font = MenloBold(15);
        int gw = (int)cellW, gh = (int)cellH;
        foreach (char ch in charset)
        {
            var levels = new CGImage[9];
            for (int level = 0; level <= 8; level++)
            {
                double t = Math.Min(level, 7) / 7.0;
                CGColor color = level == 8
                    ? NSColor.CalibratedRed(0.82, 1.0, 0.86, 1)
                    : NSColor.CalibratedRed(0.30 * t * t,
                                            0.16 + 0.84 * t,
                                            0.10 + 0.25 * t * t, 1);
                using var ctx = new BitmapContext(gw, gh);
                var str = new NSAttributedString(ch.ToString(), font, color);
                var sz = str.Size();
                str.Draw(ctx, new CGPoint((gw - sz.Width) / 2, 1));
                levels[level] = ctx.MakeImage();
            }
            glyphs[ch] = levels;
        }
    }

    static char? CharAt(in Drop drop, int row)
    {
        var s = smilesChars[drop.Mol];
        int n = s.Length + 3;  // gap between repeats
        int idx = ((row + drop.Offset) % n + n) % n;
        return idx < s.Length ? s[idx] : null;
    }

    public override void AnimateOneFrame()
    {
        tick += 1;
        for (int i = 0; i < drops.Length; i++)
        {
            drops[i].Head += drops[i].Speed;
            if (drops[i].Head - drops[i].Trail > rows)
            {
                drops[i] = MakeDrop(scattered: false);
            }
        }
        revealTimer += 1;
        if (revealsOn && reveal == null && revealTimer > 240)
        {
            revealTimer = 0;
            var m = Rng.Element(molecules);
            double x = Rnd(60, Math.Max(Bounds.Width - 460, 120));
            double y = Rnd(80, Math.Max(Bounds.Height - 120, 160));
            reveal = new Reveal
            {
                Text = $"{m.name}  ≡  {m.smiles}",
                X = x, Y = y,
                Age = 0,
            };
        }
        if (reveal != null)
        {
            reveal.Age += 1;
            if (reveal.Age > 150) reveal = null;
        }
        NeedsDisplay = true;
    }

    public override void Draw(CGContext ctx)
    {
        ctx.SetFillColor(new CGColor(0, 0.012, 0, 1));
        ctx.Fill(Bounds);
        double h = Bounds.Height;
        for (int c = 0; c < drops.Length; c++)
        {
            ref readonly var drop = ref drops[c];
            int headRow = (int)Math.Floor(drop.Head);
            for (int k = 0; k < drop.Trail; k++)
            {
                int row = headRow - k;
                if (row < 0 || row >= rows) continue;
                if (CharAt(drop, row) is not char ch || !glyphs.TryGetValue(ch, out var levels)) continue;
                int level = k == 0 ? 8 : Math.Max(0, 7 - (k * 8) / drop.Trail);
                double x = c * cellW;
                double y = h - (row + 1) * cellH;
                ctx.Draw(levels[level], new CGRect(x, y, cellW, cellH));
            }
        }
        if (reveal is Reveal r)
        {
            double alpha = 0.9 * Math.Sin(Math.PI * r.Age / 150);
            new NSAttributedString(r.Text, revealFont, NSColor.CalibratedRed(0.62, 1.0, 0.68, alpha))
                .Draw(ctx, new CGPoint(r.X, r.Y));
        }
    }
}
