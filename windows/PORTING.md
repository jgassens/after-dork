# Porting a saver from Swift to C#

The Windows port translates each `<Saver>/<Saver>.swift` **line by line** into
`windows/src/AfterDork.Core/Savers/<Saver>.cs`. The graphics layer in
`AfterDork.Core/Graphics` imitates CoreGraphics on SkiaSharp, and its user
space is **y-up with the origin at bottom-left**, just like an unflipped
`NSView`. That means Swift drawing code maps across with no changes to the
coordinate maths. `Savers/OrbitalBox.cs` is the reference port; read it first.

## Shape of a saver

```csharp
namespace AfterDork.Savers;

public sealed class FooView : SaverView          // name = "<ModuleId>View" (found by reflection)
{
    public FooView(CGRect frame, bool isPreview) : base(frame, isPreview)
    {
        AnimationTimeInterval = 1.0 / 30.0;
        // body of Swift init + setup()
    }
    public override void AnimateOneFrame() { ... NeedsDisplay = true; }
    public override void Draw(CGContext ctx) { ... }   // Swift draw(_:); drop the `guard let ctx`
}
```

Leave out `init?(coder:)`, `hasConfigureSheet`, `configureSheet` and the
`#if HARNESS` block.

## API mapping

| Swift | C# |
|---|---|
| `CGFloat`, `Double` | `double` |
| `CGPoint(x:y:)`, `.x` | `new CGPoint(x, y)`, `.X` (records, so use `with { X = … }` to change a field) |
| `CGRect(x:y:width:height:)`, `.midX/.maxY/.insetBy/.offsetBy` | `new CGRect(...)`, `.MidX/.MaxY/.InsetBy/.OffsetBy` |
| `CGVector(dx:dy:)` | `new CGVector(dx, dy)` |
| `CGColor(red:green:blue:alpha:)` | `new CGColor(r, g, b, a)` |
| `NSColor(calibratedRed:…/White:…/Hue:…)` | `NSColor.CalibratedRed/White/Hue(...)`, which returns a `CGColor` (so `.cgColor` goes away) |
| `color.components`, `.redComponent` | `.Components`, `.RedComponent` |
| `ctx.setFillColor/setStrokeColor/setLineWidth/setLineCap(.round)/setLineJoin/setShouldAntialias` | `ctx.SetFillColor(...)` … `SetLineCap(CGLineCap.Round)` |
| `ctx.saveGState()/restoreGState()` | `SaveGState()/RestoreGState()`, which also save colours, width, cap, join, AA and interpolation, as CG does |
| `ctx.translateBy(x:y:)`, `scaleBy`, `rotate(by:)` | `TranslateBy`, `ScaleBy`, `RotateBy` |
| `ctx.move(to:)`, `addLine(to:)`, `addQuadCurve(to:control:)`, `addCurve(to:control1:control2:)` | `Move`, `AddLine`, `AddQuadCurve(to, control)`, `AddCurve(to, c1, c2)` |
| `ctx.addArc(center:radius:startAngle:endAngle:clockwise:)` | `AddArc(center, radius, start, end, clockwise)`, with exact CG semantics |
| `ctx.addEllipse(in:)`, `addRect`, `addPath`, `closePath`, `beginPath` | same names, PascalCase |
| `ctx.fillPath()`, `fillPath(using: .evenOdd)`, `strokePath()`, `clip()` | `FillPath()`, `FillPath(evenOdd: true)`, `StrokePath()`, `Clip()` |
| `ctx.fill(rect)`, `fill([rects])`, `fillEllipse(in:)`, `strokeEllipse(in:)`, `stroke(rect)`, `clip(to:)` | `Fill`, `Fill(span/list)`, `FillEllipse`, `StrokeEllipse`, `Stroke`, `Clip(rect)` |
| `CGMutablePath()`, `CGPath(roundedRect:cornerWidth:cornerHeight:transform:nil)`, `CGPath(ellipseIn:)` | `new CGMutablePath()`, `CGPath.RoundedRect(r, cw, ch)`, `CGPath.Ellipse(r)`; `path.CloseSubpath()` |
| `CGGradient(colorsSpace:colors:locations:)` | `new CGGradient(CGColor[], double[])` (locations can be in any order) |
| `drawLinearGradient(g, start:, end:, options: [...])` | `DrawLinearGradient(g, start, end, CGGradientDrawingOptions.DrawsAfterEndLocation \| …)`, or `.None` for `[]` |
| `drawRadialGradient(...)` | `DrawRadialGradient(g, c0, r0, c1, r1, options)` |
| `ctx.interpolationQuality = .none` | `ctx.InterpolationQuality = CGInterpolationQuality.None` |
| Offscreen `CGContext(data:nil,width:height:…)` | `new BitmapContext(w, h)`: y-up drawing, starts transparent, `Pixels` is a `Span<uint>` (0xAARRGGBB premultiplied, row 0 = top), `MakeImage()` |
| `CGImage(... dataProvider ...)` from a `[UInt32]` buffer | `CGImage.FromPixels(span, w, h, opaque)` (same layout) |
| `ctx.draw(image, in: rect)` | `ctx.Draw(image, rect)`, drawn upright as in CG |
| `NSFont.boldSystemFont(ofSize:)`, `.monospacedSystemFont(ofSize:weight:)`, `NSFont(name:"Menlo-Bold",size:)` | `NSFont.BoldSystemFont(s)`, `NSFont.MonospacedSystemFont(s, NSFontWeight.Bold)`, `NSFont.Named("Menlo-Bold", s)` (can return null) |
| `NSAttributedString(string:attributes:[.font,.foregroundColor])` | `new NSAttributedString(s, font, color)` |
| `.size()`, `.draw(at: p)` | `.Size()`, `.Draw(ctx, p)`: `p` is the bottom-left of the line box; follows the current transform; missing glyphs fall back to another font |
| Drawing text into an offscreen bitmap via `NSGraphicsContext(cgContext:)` | call `.Draw(bitmapCtx, p)` on the `BitmapContext` directly |
| CoreText `CTLineDraw` at `textPosition` | `ctx.DrawTextAtBaseline(s, baselinePoint, font, color)`; width is `font.MeasureWidth(s)` |
| `AfterDork.value(m, k, def)`, `AfterDork.flag(m, k, def)` | `Settings.Value(m, k, def)`, `Settings.Flag(m, k, def)` |
| `CGFloat.random(in: a...b)`, `Double.random` | `Rng.Range(a, b)` |
| `Int.random(in: a..<b)` / `a...b` | `Rng.Int(a, b)` / `Rng.IntInclusive(a, b)` |
| `Bool.random()`, `arr.randomElement()!`, `arr.shuffled()` | `Rng.Bool()`, `Rng.Element(arr)`, `Rng.Shuffled(arr)` |
| `SIMD3<Double>`, `simd_dot/cross/length/normalize` | `V3`, `V3.Dot/Cross/Length/Normalize` |
| `SIMD3<Int>` | `I3` |
| `.pi`, `hypot`, `atan2` | `Math.PI`, `double.Hypot`, `Math.Atan2` |

Always use `Rng`, never `System.Random` or `Random.Shared`, so that seeded renders can be reproduced.

## Translation rules (these are where ports go wrong)

1. **Value semantics.** A Swift struct changed inside an array (`items[i].x += 1`, `inout`, `mutating func`) must stay a C# `struct`, stored in a `T[]` (or reached through `CollectionsMarshal.AsSpan(list)[i]`) and changed with `ref`. Going through a `List<T>` indexer changes a **copy**, and the compiler doesn't warn you. When Swift takes a copy on purpose (`let s = arr[i]`) and then changes the array, keep that snapshot.
2. **Integers.** Use `long` for counters that grow forever. Swift traps on overflow, while C# `int` wraps. `&+` and `&*` wrap in both languages; use `unchecked` with `ulong`.
3. **Rounding.** Swift `x.rounded()` rounds half away from zero, so use `Math.Round(x, MidpointRounding.AwayFromZero)`. `.rounded(.down)` becomes `Math.Floor`, `.rounded(.up)` becomes `Math.Ceiling`. `Int(x)` truncates toward zero, and a `(int)` cast matches that. Integer `/` and `%` behave the same as Swift. `truncatingRemainder` is C# `%` on doubles.
4. **Ranges.** `a...b` includes `b` (`<=`), and `a..<b` excludes it. For `stride(from:through:by:)` over doubles, loop over an integer counter.
5. **Sorting.** Swift's `sort` is not guaranteed stable. Where equal keys could flicker, use a stable sort (LINQ `OrderBy`).
6. **Graphics state carries over.** An unsaved `setLineCap(.round)` stays in effect for the rest of `draw`. The port does the same because the context behaves like CG. Keep the same order of calls.
7. **Performance.** Draw runs 30 times a second at up to 4K. Hoist fixed paths, arrays and fonts into fields, and avoid LINQ and allocation in per-frame loops. Keep behaviour identical to the Swift.
8. **Leave the graphics layer alone.** If the layer is truly missing something or wrong, make a minimal additive change, add a unit test in `tests/AfterDork.Tests/GraphicsTests.cs`, and call it out in your report.

## Verifying a port

```powershell
$dn = "C:\Program Files\dotnet\dotnet.exe"
& $dn build tools\AfterDork.Cli -c Release -v q        # from windows/
$cli = "tools\AfterDork.Cli\bin\Release\net8.0-windows\afterdork-cli.exe"
& $cli render Foo 1280 720 300 out\foo\f --seed 1 --capture 60,150,299   # PNGs to view
& $cli render Foo 1280 720 300 out\foo\min --seed 1 --set key=value      # try an option
& $cli soak Foo 1920 1080 20000 --draw-every 10                          # stability + timing
& $cli soak Foo 152 112 5000 --preview                                   # Windows preview size
& $dn test tests\AfterDork.Tests -v q
```

Compare your renders with `docs/<saver>.png`, the Mac screenshot. Check orientation, colours, layout, text, line caps and joins, and anything upside down or mirrored. Draw time should stay under about 20 ms per frame at 1920×1080.
