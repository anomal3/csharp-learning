using System.Globalization;
using System.Text;

namespace CsharpLearning.Core.Graph;

/// <summary>Типы данных графа и правила «что к чему можно подключить».</summary>
public static class GraphTypes
{
    public static readonly string[] Primitive = { "int", "long", "double", "decimal", "bool", "string", "char" };
    public static readonly string[] Numeric = { "int", "long", "double", "decimal" };

    public static bool IsNumeric(string t) => Array.IndexOf(Numeric, t) >= 0;

    public static string Elem(string listType) =>
        listType.StartsWith("List<") && listType.EndsWith('>') ? listType[5..^1] : "object";

    public static bool IsList(string t) => t.StartsWith("List<") && t.EndsWith('>');

    /// <summary>Можно ли значение типа <paramref name="from"/> положить в вход типа <paramref name="to"/>.</summary>
    public static bool CanAssign(GraphProject? p, string from, string to)
    {
        if (from == to || to == "object" || string.IsNullOrEmpty(to)) return true;

        // любое значение можно превратить в текст — генератор добавит Convert.ToString
        if (to == "string") return true;

        switch (from)
        {
            case "int": if (to is "long" or "double" or "decimal") return true; break;
            case "long": if (to is "double" or "decimal") return true; break;
            case "char": if (to is "int" or "long" or "double" or "decimal") return true; break;
        }

        // наследование: потомок подходит там, где ждут предка
        if (p is not null)
        {
            var c = p.FindClass(from);
            for (int guard = 0; c?.BaseClass is not null && guard < 16; guard++)
            {
                if (c.BaseClass == to) return true;
                c = p.FindClass(c.BaseClass);
            }
        }
        return false;
    }

    /// <summary>Цвет пина по типу (как в Blueprint-редакторах).</summary>
    public static string Color(string type) => type switch
    {
        "int" or "long" => "#4FC1E9",
        "double" or "decimal" => "#8CD867",
        "bool" => "#E9573F",
        "string" => "#F6BB42",
        "char" => "#F0A0C8",
        "object" => "#AAB2BD",
        "" => "#FFFFFF",
        _ when type.StartsWith("List<") => "#AC92EC",
        _ => "#48CFAD",
    };

    public static string Default(string type) => type switch
    {
        "int" or "long" or "double" or "decimal" => "0",
        "bool" => "false",
        "string" => "",
        "char" => "a",
        _ => "",
    };

    /// <summary>Выражение «значение по умолчанию» для объявления переменной.</summary>
    public static string DefaultExpr(string type) => type switch
    {
        "int" or "long" or "double" or "decimal" or "bool" or "char" => Literal(type, Default(type)),
        "string" => "\"\"",
        _ when IsList(type) => $"new {type}()",
        _ => "null",
    };

    /// <summary>Текст значения → литерал C# нужного типа.</summary>
    public static string Literal(string type, string text)
    {
        switch (type)
        {
            case "string": return Quote(text);
            case "char": return "'" + EscapeChar(text.Length > 0 ? text[0] : ' ') + "'";
            case "bool": return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            case "int":
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i is >= int.MinValue and <= int.MaxValue
                    ? (i < 0 ? $"({i})" : i.ToString(CultureInfo.InvariantCulture)) : "0";
            case "long":
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? (l < 0 ? $"({l}L)" : l + "L") : "0L";
            case "double":
                return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? Frac(d) : "0.0";
            case "decimal":
                return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
                    ? (m < 0 ? $"({m.ToString(CultureInfo.InvariantCulture)}m)" : m.ToString(CultureInfo.InvariantCulture) + "m") : "0m";
            case "object": return Quote(text);
            default: return "null";
        }

        static string Frac(double d)
        {
            var s = d.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains('.') && !s.Contains('E')) s += ".0";
            return d < 0 ? $"({s})" : s;
        }
    }

    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s) sb.Append(c == '"' ? "\\\"" : EscapeChar(c));
        return sb.Append('"').ToString();
    }

    static string EscapeChar(char c) => c switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ => c.ToString(),
    };

    /// <summary>Имя пригодно как идентификатор C#.</summary>
    public static string Ident(string name, string fallback = "x")
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0) return fallback;
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
