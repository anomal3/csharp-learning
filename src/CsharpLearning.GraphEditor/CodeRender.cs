using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CsharpLearning.Core.Runner;

namespace CsharpLearning.GraphEditor;

/// <summary>Раскрашенный код (палитра Visual Studio) в обычном TextBlock.</summary>
public static class CodeRender
{
    static readonly Dictionary<CodeKind, Brush> Palette = new()
    {
        [CodeKind.Plain] = Solid("#FFDCDCDC"),
        [CodeKind.Keyword] = Solid("#FF569CD6"),
        [CodeKind.Control] = Solid("#FFD8A0DF"),
        [CodeKind.Type] = Solid("#FF4EC9B0"),
        [CodeKind.Method] = Solid("#FFDCDCAA"),
        [CodeKind.Local] = Solid("#FF9CDCFE"),
        [CodeKind.String] = Solid("#FFD69D85"),
        [CodeKind.Number] = Solid("#FFB5CEA8"),
        [CodeKind.Comment] = Solid("#FF57A64A"),
        [CodeKind.Operator] = Solid("#FFB4B4B4"),
    };

    static readonly Brush Highlight = Solid("#552C6FA6");

    public static Brush Solid(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    /// <summary>Заполняет TextBlock кодом. Строки hiFrom..hiTo (с 1) подсвечиваются фоном.</summary>
    public static void Fill(TextBlock tb, string? source, int hiFrom = 0, int hiTo = 0)
    {
        tb.Inlines.Clear();
        string code = (source ?? "").Replace("\r\n", "\n").Replace("\t", "    ").TrimEnd('\n');
        if (code.Length == 0) return;

        int line = 1;
        void Append(string text, Brush brush)
        {
            var parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) { tb.Inlines.Add(new LineBreak()); line++; }
                if (parts[i].Length == 0) continue;
                var run = new Run(parts[i]) { Foreground = brush };
                if (hiFrom > 0 && line >= hiFrom && line <= hiTo) run.Background = Highlight;
                tb.Inlines.Add(run);
            }
        }

        int pos = 0;
        foreach (var span in CodeClassifier.Classify(code))
        {
            if (span.Start < pos || span.Start + span.Length > code.Length) continue;
            if (span.Start > pos) Append(code[pos..span.Start], Palette[CodeKind.Plain]);
            Append(code.Substring(span.Start, span.Length), Palette[span.Kind]);
            pos = span.Start + span.Length;
        }
        if (pos < code.Length) Append(code[pos..], Palette[CodeKind.Plain]);
    }
}
