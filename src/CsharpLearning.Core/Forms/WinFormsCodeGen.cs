using System.Globalization;
using System.Text;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.Core.Forms;

/// <summary>Из описания окна пишет настоящий код Windows Forms: ровно то, что ученик набрал бы в Visual Studio.</summary>
public static class WinFormsCodeGen
{
    sealed record Ex(string Code, string Type);

    public static string Generate(FormSpec f, string className = "Form1")
    {
        var sb = new StringBuilder();
        var g = new Gen(f, sb);
        g.Run(className);
        return sb.ToString().TrimEnd() + "\n";
    }

    sealed class Gen
    {
        readonly FormSpec _f;
        readonly StringBuilder _sb;
        readonly Dictionary<string, string> _vars;
        readonly HashSet<string> _loops = new();

        public Gen(FormSpec f, StringBuilder sb)
        {
            _f = f; _sb = sb;
            _vars = f.State.ToDictionary(s => s.Name, s => s.Type);
        }

        void L(int indent, string s) => _sb.AppendLine(new string(' ', indent * 4) + s);

        static string TypeName(ControlKind k) => k.ToString();

        public void Run(string cls)
        {
            L(0, "using System;");
            L(0, "using System.Drawing;");
            L(0, "using System.Windows.Forms;");
            L(0, "");
            L(0, $"public class {cls} : Form");
            L(0, "{");

            // поля: элементы и состояние
            foreach (var c in _f.Controls) L(1, $"private {TypeName(c.Kind)} {c.Name};");
            foreach (var v in _f.State)
                L(1, $"private {v.Type} {v.Name}" + (v.Type == "string" ? $" = {GraphTypes.Quote(v.Initial)};" : $" = {v.Initial};"));
            L(0, "");

            L(1, $"public {cls}()");
            L(1, "{");
            L(2, $"Text = {GraphTypes.Quote(_f.Title)};");
            L(2, $"ClientSize = new Size({_f.Width}, {_f.Height});");
            L(2, "StartPosition = FormStartPosition.CenterScreen;");
            L(0, "");

            foreach (var c in _f.Controls) Create(c);
            L(0, "");
            foreach (var c in _f.Controls)
                foreach (var h in c.Handlers)
                    L(2, $"{c.Name}.{h.Event} += {h.Name};");
            L(0, "");
            L(2, "Controls.AddRange(new Control[] { " + string.Join(", ", _f.Controls.Select(c => c.Name)) + " });");
            L(1, "}");

            foreach (var c in _f.Controls)
                foreach (var h in c.Handlers)
                {
                    L(0, "");
                    L(1, $"private void {h.Name}(object sender, EventArgs e)");
                    L(1, "{");
                    Emit(h.Actions, 2);
                    L(1, "}");
                }

            L(0, "");
            L(1, "[STAThread]");
            L(1, "static void Main()");
            L(1, "{");
            L(2, "Application.EnableVisualStyles();");
            L(2, $"Application.Run(new {cls}());");
            L(1, "}");
            L(0, "}");
        }

        void Create(ControlSpec c)
        {
            string loc = $"Location = new Point({c.X}, {c.Y})";
            string size = $"Size = new Size({c.W}, {c.H})";
            string text = c.Text.Length > 0 ? $", Text = {GraphTypes.Quote(c.Text)}" : "";
            string vis = c.Visible ? "" : ", Visible = false";

            switch (c.Kind)
            {
                case ControlKind.Label:
                    L(2, $"{c.Name} = new Label {{ {loc}{text}, AutoSize = true{vis} }};");
                    break;
                case ControlKind.Button:
                    L(2, $"{c.Name} = new Button {{ {loc}, {size}{text}{vis} }};");
                    break;
                case ControlKind.TextBox:
                    L(2, $"{c.Name} = new TextBox {{ {loc}, Width = {c.W}{text}{vis} }};");
                    break;
                case ControlKind.CheckBox:
                    L(2, $"{c.Name} = new CheckBox {{ {loc}{text}, AutoSize = true{(c.Checked ? ", Checked = true" : "")}{vis} }};");
                    break;
                case ControlKind.ListBox:
                    L(2, $"{c.Name} = new ListBox {{ {loc}, {size}{vis} }};");
                    if (c.Items.Count > 0)
                        L(2, $"{c.Name}.Items.AddRange(new object[] {{ {string.Join(", ", c.Items.Select(GraphTypes.Quote))} }});");
                    break;
                case ControlKind.PictureBox:
                    L(2, $"{c.Name} = new PictureBox {{ {loc}, {size}, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle{vis} }};");
                    if (c.Picture.Length > 0)
                        L(2, $"{c.Name}.Image = Image.FromFile(\"{c.Picture}.png\");   // файл картинки лежит рядом с exe");
                    break;
                case ControlKind.ProgressBar:
                    L(2, $"{c.Name} = new ProgressBar {{ {loc}, {size}, Minimum = 0, Maximum = 100, Value = {c.Value}{vis} }};");
                    break;
            }
        }

        // ---------------- действия ----------------
        void Emit(List<ActionSpec> list, int ind)
        {
            foreach (var a in list)
            {
                switch (a.Kind)
                {
                    case ActionKind.SetText:
                        L(ind, $"{a.Target}.Text = {Str(Tpl(a.Value))};");
                        break;
                    case ActionKind.SetVar:
                    {
                        var ex = Tpl(a.Value);
                        string t = _vars.GetValueOrDefault(a.Target, "string");
                        string code = t == "string" ? Str(ex) : ex.Type == "string" ? $"{t}.Parse({ex.Code})" : ex.Code;
                        L(ind, $"{a.Target} = {code};");
                        break;
                    }
                    case ActionKind.Inc:
                        L(ind, a.Value.Length == 0 ? $"{a.Target}++;" : $"{a.Target} += {Num(Tpl(a.Value))};");
                        break;
                    case ActionKind.AddItem:
                        L(ind, $"{a.Target}.Items.Add({Str(Tpl(a.Value))});");
                        break;
                    case ActionKind.ClearItems:
                        L(ind, $"{a.Target}.Items.Clear();");
                        break;
                    case ActionKind.SetVisible:
                        L(ind, a.Value == "toggle" ? $"{a.Target}.Visible = !{a.Target}.Visible;" : $"{a.Target}.Visible = {a.Value};");
                        break;
                    case ActionKind.SetEnabled:
                        L(ind, a.Value == "toggle" ? $"{a.Target}.Enabled = !{a.Target}.Enabled;" : $"{a.Target}.Enabled = {a.Value};");
                        break;
                    case ActionKind.SetPicture:
                        L(ind, $"{a.Target}.Image = Image.FromFile(\"{a.Value}.png\");");
                        break;
                    case ActionKind.SetProgress:
                        L(ind, $"{a.Target}.Value = Math.Clamp({Num(Tpl(a.Value))}, 0, 100);");
                        break;
                    case ActionKind.Message:
                        L(ind, $"MessageBox.Show({Str(Tpl(a.Value))});");
                        break;
                    case ActionKind.If:
                        L(ind, $"if ({Cond(a.Condition)})");
                        L(ind, "{");
                        Emit(a.Then, ind + 1);
                        L(ind, "}");
                        if (a.Else.Count > 0)
                        {
                            L(ind, "else");
                            L(ind, "{");
                            Emit(a.Else, ind + 1);
                            L(ind, "}");
                        }
                        break;
                    case ActionKind.For:
                        L(ind, $"for (int {a.Target} = {Num(Tpl(a.From))}; {a.Target} <= {Num(Tpl(a.To))}; {a.Target}++)");
                        L(ind, "{");
                        _loops.Add(a.Target);
                        Emit(a.Body, ind + 1);
                        _loops.Remove(a.Target);
                        L(ind, "}");
                        break;
                    case ActionKind.Try:
                        L(ind, "try");
                        L(ind, "{");
                        Emit(a.Body, ind + 1);
                        L(ind, "}");
                        L(ind, "catch (Exception)");
                        L(ind, "{");
                        Emit(a.Catch, ind + 1);
                        L(ind, "}");
                        break;
                }
            }
        }

        static string Str(Ex e) => e.Type == "string" ? e.Code : e.Code + ".ToString()";
        static string Num(Ex e) => e.Type == "string" ? $"int.Parse({e.Code})" : e.Code;

        // ---------------- выражения ----------------
        Ex Tpl(string template)
        {
            var parts = new List<Ex>();
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                if (open < 0) { parts.Add(new Ex(GraphTypes.Quote(template[i..]), "string")); break; }
                if (open > i) parts.Add(new Ex(GraphTypes.Quote(template[i..open]), "string"));
                int close = template.IndexOf('}', open);
                if (close < 0) { parts.Add(new Ex(GraphTypes.Quote(template[open..]), "string")); break; }

                string token = template.Substring(open + 1, close - open - 1);
                parts.Add(token.StartsWith('=') ? CalcEx(token[1..].Trim()) : Operand(token));
                i = close + 1;
            }

            if (parts.Count == 0) return new Ex("\"\"", "string");
            if (parts.Count == 1) return parts[0];
            string joined = string.Join(" + ", parts.Select(p => p.Code));
            if (parts[0].Type != "string") joined = "\"\" + " + joined;
            return new Ex(joined, "string");
        }

        Ex CalcEx(string expr)
        {
            var p = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var a = Operand(p[0]);
            var b = Operand(p[2]);
            string type = a.Type == "double" || b.Type == "double" ? "double" : "int";
            return new Ex($"({a.Code} {p[1]} {b.Code})", type);
        }

        Ex Operand(string token)
        {
            token = token.Trim();
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && !token.Contains(':'))
                return new Ex(token, token.Contains('.') ? "double" : "int");

            string name = token, fmt = "";
            int colon = token.IndexOf(':');
            if (colon > 0) { name = token[..colon]; fmt = token[(colon + 1)..]; }

            if (name == "now") return new Ex($"DateTime.Now.ToString({GraphTypes.Quote(fmt.Length > 0 ? fmt : "HH:mm:ss")})", "string");

            Ex baseEx;
            if (_loops.Contains(name)) baseEx = new Ex(name, "int");
            else if (_vars.TryGetValue(name, out var vt)) baseEx = new Ex(name, vt);
            else baseEx = new Ex(name + ".Text", "string");

            return fmt switch
            {
                "" => baseEx,
                "int" => baseEx.Type == "int" ? baseEx : new Ex($"int.Parse({baseEx.Code})", "int"),
                "double" => baseEx.Type == "double" ? baseEx : new Ex($"double.Parse({baseEx.Code})", "double"),
                "upper" => new Ex($"{baseEx.Code}.ToUpper()", "string"),
                "lower" => new Ex($"{baseEx.Code}.ToLower()", "string"),
                "len" => new Ex($"{baseEx.Code}.Length", "int"),
                _ => new Ex($"{baseEx.Code}.ToString({GraphTypes.Quote(fmt)})", "string"),
            };
        }

        string Cond(string cond)
        {
            cond = cond.Trim();
            bool neg = cond.StartsWith('!');
            if (neg) cond = cond[1..];

            string core;
            if (cond.EndsWith(".IsEmpty")) core = $"string.IsNullOrWhiteSpace({cond[..^8]}.Text)";
            else if (cond.EndsWith(".Checked")) core = cond;
            else if (cond.EndsWith(".NotInt")) core = $"!int.TryParse({cond[..^7]}.Text, out _)";
            else
            {
                string[] ops = { "==", "!=", "<=", ">=", "<", ">" };
                string op = ops.First(o => cond.Contains(" " + o + " "));
                var sides = cond.Split(" " + op + " ", 2);
                core = $"{Side(sides[0])} {op} {Side(sides[1])}";
            }
            return neg ? $"!({core})" : core;

            string Side(string t) => t.Trim().StartsWith('"') ? t.Trim() : Operand(t).Code;
        }
    }
}
