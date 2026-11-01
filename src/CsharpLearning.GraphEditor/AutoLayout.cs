using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>
/// «Упорядочить»: раскладывает ноды слева направо по ходу выполнения, ноды-формулы ставит рядом
/// с тем, кто их использует. Размеры берутся у настоящих нод, поэтому ничего не накладывается.
/// </summary>
public static class AutoLayout
{
    public static void ArrangeAll(GraphProject p)
    {
        foreach (var c in p.Classes)
            foreach (var m in c.Methods)
                Arrange(p, c, m);
    }

    public static void Arrange(GraphProject p, ClassDef cls, MethodDef m)
    {
        var g = m.Graph;
        if (g.Nodes.Count == 0) return;

        var ctx = new NodeVisualContext { P = p, Cls = cls, M = m, ConnectedInputs = g.Wires.Select(w => w.ToPin).ToHashSet() };
        var size = new Dictionary<string, (double w, double h)>();
        var pure = new HashSet<string>();
        foreach (var n in g.Nodes)
        {
            var spec = NodeSpecs.Get(n.Kind);
            if (spec is null) { size[n.Id] = (240, 120); continue; }
            var v = new NodeVisual(n, spec, GraphScope.PinsOf(p, cls, m, n), ctx);
            size[n.Id] = (v.NodeWidth, v.NodeHeight);
            if (spec.Mode == NodeMode.Pure) pure.Add(n.Id);
        }

        var incoming = g.Wires.ToLookup(w => w.ToNode);
        var outgoing = g.Wires.ToLookup(w => w.FromNode);

        // глубина = длина самой длинной цепочки до ноды
        var depth = new Dictionary<string, int>();
        int Depth(string id, int guard)
        {
            if (depth.TryGetValue(id, out var d)) return d;
            if (guard > 200) return 0;
            int best = 0;
            foreach (var w in incoming[id])
                if (w.FromNode != id && g.Find(w.FromNode) is not null) best = Math.Max(best, Depth(w.FromNode, guard + 1) + 1);
            return depth[id] = best;
        }
        foreach (var n in g.Nodes) Depth(n.Id, 0);

        // формулы (pure) ставим сразу левее того, кто их использует
        for (int pass = 0; pass < 4; pass++)
            foreach (var id in pure.OrderByDescending(x => depth[x]))
            {
                var consumers = outgoing[id].Select(w => w.ToNode).Where(depth.ContainsKey).ToList();
                if (consumers.Count > 0) depth[id] = Math.Max(0, consumers.Min(c => depth[c]) - 1);
            }

        var columns = g.Nodes.GroupBy(n => depth[n.Id]).OrderBy(x => x.Key).ToList();
        var colX = new Dictionary<int, double>();
        double x = 40;
        foreach (var col in columns)
        {
            colX[col.Key] = Math.Round(x / 10) * 10;
            x += col.Max(n => size[n.Id].w) + 90;
        }

        var placed = new Dictionary<string, (double y, double h, int col)>();

        // 1) шаги программы — ровно по колонкам сверху вниз
        foreach (var col in columns)
        {
            double SourceY(NodeData n)
            {
                var ys = incoming[n.Id].Where(w => placed.ContainsKey(w.FromNode)).Select(w => placed[w.FromNode].y).ToList();
                return ys.Count > 0 ? ys.Average() : n.Y;
            }

            double y = 60;
            foreach (var n in col.Where(n => !pure.Contains(n.Id)).OrderBy(SourceY).ThenBy(n => n.Y))
            {
                n.X = colX[col.Key];
                n.Y = Math.Round(y / 10) * 10;
                placed[n.Id] = (n.Y, size[n.Id].h, col.Key);
                y = n.Y + size[n.Id].h + 36;
            }
        }

        // 2) формулы — ниже того, кто их использует, чтобы провода потока не шли «сквозь» них
        foreach (var n in g.Nodes.Where(n => pure.Contains(n.Id)).OrderByDescending(n => depth[n.Id]).ThenBy(n => n.Y))
        {
            var users = outgoing[n.Id].Select(w => w.ToNode).Where(placed.ContainsKey).ToList();
            double want = users.Count > 0 ? users.Min(u => placed[u].y) + 90 : 60;
            double h = size[n.Id].h;

            double y = Math.Round(want / 10) * 10;
            bool moved = true;
            for (int guard = 0; moved && guard < 200; guard++)
            {
                moved = false;
                foreach (var other in placed.Values.Where(o => o.col == depth[n.Id]))
                    if (y < other.y + other.h + 24 && y + h + 24 > other.y) { y = Math.Round((other.y + other.h + 30) / 10) * 10; moved = true; }
            }
            n.X = colX[depth[n.Id]];
            n.Y = y;
            placed[n.Id] = (y, h, depth[n.Id]);
        }
    }
}
