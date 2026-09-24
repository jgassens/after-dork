using System.Globalization;
using AfterDork;

// Developer harness for the savers (the Windows twin of `make previews`).
//
//   afterdork-cli render <Module> <w> <h> <frames> <outPrefix> [options]
//   afterdork-cli soak   <Module|all> <w> <h> <frames> [options]
//
// Options: --preview  --all  --seed N  --scale S  --draw-every N
//          --capture a,b,c  --set key=value (repeatable)

if (args.Length < 1) return Usage();
var cmd = args[0];
var pos = new List<string>();
bool preview = false, all = false;
int? seed = null;
float scale = 1;
int drawEvery = 1;
HashSet<int>? capture = null;
var sets = new List<(string, string)>();
for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--preview": preview = true; break;
        case "--all": all = true; break;
        case "--seed": seed = int.Parse(args[++i]); break;
        case "--scale": scale = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--draw-every": drawEvery = int.Parse(args[++i]); break;
        case "--capture": capture = args[++i].Split(',').Select(int.Parse).ToHashSet(); break;
        case "--set":
            var kv = args[++i].Split('=', 2);
            sets.Add((kv[0], kv[1]));
            break;
        default: pos.Add(args[i]); break;
    }
}

if (seed is int s) Rng.Seed(s);

IEnumerable<Module> Modules(string id) =>
    id.Equals("all", StringComparison.OrdinalIgnoreCase)
        ? ModuleCatalog.All.Where(m => m.IsAvailable)
        : [ModuleCatalog.Find(id) ?? throw new ArgumentException($"Unknown module {id}")];

void ApplySets(Module m)
{
    Settings.Overrides.Clear();
    if (sets.Count == 0) return;
    var d = new Dictionary<string, object>();
    foreach (var (k, v) in sets)
    {
        d[k] = v switch
        {
            "true" => true,
            "false" => false,
            _ => double.Parse(v, CultureInfo.InvariantCulture),
        };
    }
    Settings.Overrides[m.Id] = d;
}

switch (cmd)
{
    case "render" when pos.Count >= 5:
    {
        var m = Modules(pos[0]).Single();
        ApplySets(m);
        var r = Harness.Render(m, int.Parse(pos[1]), int.Parse(pos[2]), int.Parse(pos[3]), pos[4],
                               preview, scale, drawEvery: 0, capture: capture, captureAll: all);
        Console.WriteLine($"{m.Id}: {r.Frames} frames, animate {r.AnimateMsAvg:0.00} ms, draw {r.DrawMsAvg:0.00} ms avg / {r.DrawMsMax:0.0} max");
        foreach (var f in r.Files) Console.WriteLine($"  {f}");
        return 0;
    }
    case "soak" when pos.Count >= 4:
    {
        int failures = 0;
        foreach (var m in Modules(pos[0]))
        {
            ApplySets(m);
            try
            {
                var r = Harness.Render(m, int.Parse(pos[1]), int.Parse(pos[2]), int.Parse(pos[3]), null,
                                       preview, scale, drawEvery);
                Console.WriteLine($"PASS {m.Id,-16} {pos[1]}x{pos[2]}{(preview ? " preview" : "")}: {r.Frames} frames, animate {r.AnimateMsAvg:0.000} ms, draw {r.DrawMsAvg:0.00} ms avg / {r.DrawMsMax:0.0} max");
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine($"FAIL {m.Id,-16} {pos[1]}x{pos[2]}: {e}");
            }
        }
        return failures == 0 ? 0 : 1;
    }
    default:
        return Usage();
}

static int Usage()
{
    Console.Error.WriteLine("usage: afterdork-cli render <Module> <w> <h> <frames> <outPrefix> [--preview] [--all] [--seed N] [--scale S] [--capture a,b] [--set k=v]");
    Console.Error.WriteLine("       afterdork-cli soak <Module|all> <w> <h> <frames> [--preview] [--draw-every N] [--set k=v]");
    return 2;
}
