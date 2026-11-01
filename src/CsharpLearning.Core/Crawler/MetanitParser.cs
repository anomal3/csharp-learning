using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace CsharpLearning.Core.Crawler;

public sealed class MetanitParser
{
    static readonly HtmlParser Parser = new();

    public static readonly string BaseUrl = "https://metanit.com/sharp/tutorial/";

    /// <summary>Сайт частично отдаёт кириллицу в cp1251 — подбираем кодировку по содержимому.</summary>
    static MetanitParser()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* уже зарегистрирован */ }
    }

    public static string Decode(byte[] raw)
    {
        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return utf8Strict.GetString(raw);
        }
        catch
        {
            // metanit иногда отдаёт кириллицу в cp1251, объявляя UTF-8
            try { return Encoding.GetEncoding(1251).GetString(raw); }
            catch { return Encoding.UTF8.GetString(raw); }
        }
    }

    public static string NormalizeUrl(string href, string baseHref)
    {
        href = href.Trim();
        if (href.StartsWith("//")) return "https:" + href;
        if (href.StartsWith("http")) return href;
        if (href.StartsWith("/")) return "https://metanit.com" + href;
        return new Uri(new Uri(baseHref), href).ToString();
    }

/// <summary>Урок оглавления: slug + название именно то, что учебник показывает в списке глав.</summary>
    public sealed record TocLesson(string Slug, string Title);

    public static void BuildToc(string indexHtml, string indexUrl, out List<string> lessonSlugs, out List<(int number, string title, List<TocLesson> lessons)> chapters)
    {
        lessonSlugs = new();
        chapters = new();

        var doc = Parser.ParseDocument(indexHtml);
        var ol = doc.QuerySelector("ol.content");
        if (ol is null) return;

        foreach (var li in ol.Children.Where(e => e.TagName == "LI"))
        {
            var chapterLink = li.QuerySelector(":scope > p > a");
            if (chapterLink is null) continue;

            var chapterTitle = Clean(chapterLink.TextContent);
            int number = ExtractChapterNumber(chapterTitle);
            var sub = li.QuerySelector("ol");
            var lessons = new List<TocLesson>();

            if (sub is not null)
            {
                foreach (var a in sub.QuerySelectorAll("li > p > a"))
                {
                    var href = a.GetAttribute("href") ?? "";
                    if (!href.EndsWith(".php")) continue;
                    var abs = NormalizeUrl(href, indexUrl);
                    if (!abs.Contains("/sharp/tutorial/")) continue;
                    var slug = SlugOf(abs);
                    if (lessonSlugs.Contains(slug)) continue;
                    lessonSlugs.Add(slug);
                    // Название берём из оглавления: на самих страницах h1 нередко равен названию РАЗДЕЛА,
                    // из-за чего урок подписывался «Обработка исключений» вместо «Блок catch и фильтры».
                    lessons.Add(new TocLesson(slug, Clean(a.TextContent)));
                }
            }

            chapters.Add((number, chapterTitle, lessons));
        }
    }

    /// <summary>Название страницы из &lt;title&gt;: «C# и .NET | Типы данных» → «Типы данных».</summary>
    public static string TitleFromHead(string html)
    {
        var m = System.Text.RegularExpressions.Regex.Match(html, @"<title>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return "";
        var t = Clean(m.Groups[1].Value);
        int pipe = t.IndexOf('|');
        if (pipe >= 0) t = Clean(t[(pipe + 1)..]);
        return t.TrimStart('–', '-', ' ');
    }

    static int ExtractChapterNumber(string title)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"Глава\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return int.Parse(m.Groups[1].Value);
        m = System.Text.RegularExpressions.Regex.Match(title, @"^(\d+)\.");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    public static string SlugOf(string absoluteUrl)
    {
        var uri = new Uri(absoluteUrl);
        return uri.AbsolutePath.Split('/').Last(s => s.Length > 0);
    }

    /// <summary>Разбор страницы урока в блоки: заголовки, абзацы, код.</summary>
    public static LessonParseResult ParseLesson(string html, string url, string titleHint)
    {
        var doc = Parser.ParseDocument(html);
        var container = doc.QuerySelector("div.item.menC") ?? doc.QuerySelector("div.innercontainer") ?? doc.Body;

        var title = Clean(container?.QuerySelector("h2")?.TextContent ?? "");
        if (string.IsNullOrWhiteSpace(title)) title = TitleFromHead(html);
        if (string.IsNullOrWhiteSpace(title) || title.EndsWith(".php")) title = Clean(titleHint);

        var updated = Clean(container?.QuerySelector("div.date")?.TextContent ?? "");
        updated = System.Text.RegularExpressions.Regex.Replace(updated, @"[^0-9.]", "");

        var blocks = new List<Core.Model.ContentBlock>();
        int idx = 0;

        if (container is null) return new LessonParseResult(title, updated, blocks, "", 0, null);

        foreach (var el in EnumerateContent(container))
        {
            switch (el.TagName)
            {
                case "PRE":
                {
                    var code = PreText(el);
                    if (code.Trim().Length > 0)
                        blocks.Add(new Core.Model.ContentBlock { Index = idx++, Kind = Core.Model.BlockKind.Code, Code = code, Text = code });
                    break;
                }
                case "H2":
                case "H3":
                case "H4":
                {
                    var t = Clean(el.TextContent);
                    if (t.Length > 0) blocks.Add(new Core.Model.ContentBlock { Index = idx++, Kind = Core.Model.BlockKind.Heading, Text = t });
                    break;
                }
                case "UL":
                case "OL":
                {
                    var items = el.QuerySelectorAll(":scope > li").Select(li => Clean(li.TextContent)).Where(s => s.Length > 0).ToList();
                    if (items.Count > 0) blocks.Add(new Core.Model.ContentBlock { Index = idx++, Kind = Core.Model.BlockKind.List, Text = string.Join("\n", items.Select(i => "• " + i)) });
                    break;
                }
                case "P":
                {
                    var t = InlineText(el);
                    if (t.Length > 0) blocks.Add(new Core.Model.ContentBlock { Index = idx++, Kind = Core.Model.BlockKind.Paragraph, Text = t });
                    break;
                }
                case "TABLE":
                {
                    var rows = el.QuerySelectorAll("tr").Select(tr => string.Join(" | ", tr.QuerySelectorAll("td,th").Select(c => Clean(c.TextContent)))).Where(s => s.Length > 0);
                    var txt = string.Join("\n", rows);
                    if (txt.Length > 0) blocks.Add(new Core.Model.ContentBlock { Index = idx++, Kind = Core.Model.BlockKind.Table, Text = txt });
                    break;
                }
            }
        }

        var plain = string.Join("\n", blocks.Where(b => b.Kind != Core.Model.BlockKind.Code).Select(b => b.Text));
        var questionsUrl = FindQuestionsUrl(html, url);
        return new LessonParseResult(title, updated, blocks, plain, blocks.Count(b => b.Kind == Core.Model.BlockKind.Code), questionsUrl);
    }

    static readonly string[] ContentTags = { "H2", "H3", "H4", "P", "PRE", "UL", "OL", "TABLE", "BLOCKQUOTE", "DL" };

    /// <summary>
    /// Обходим только «тело» урока: блоки идут в документальном порядке,
    /// реклама, соцсети, меню и «дополнительные материалы» отбрасываются по классам/id предков.
    /// </summary>
    static IEnumerable<IElement> EnumerateContent(IElement root)
    {
        foreach (var el in root.QuerySelectorAll(string.Join(",", ContentTags)))
        {
            if (InsideJunk(el, root)) continue;
            if (el.TagName == "P" && el.ParentElement?.TagName == "LI") continue; // пункты списка обрабатываются целиком
            if (el.TagName == "LI") continue;
            yield return el;
        }
    }

    static bool InsideJunk(IElement el, IElement stop)
    {
        var node = el;
        while (node is not null && !ReferenceEquals(node, stop))
        {
            var cls = node.ClassList;
            if (cls.Contains("socBlock") || cls.Contains("share") || cls.Contains("soctop") ||
                cls.Contains("dop") || cls.Contains("nav") || cls.Contains("menu") ||
                cls.Contains("socbtns") || cls.Contains("help"))
                return true;

            var id = node.Id ?? "";
            if (id.StartsWith("yandex_rtb") || id.StartsWith("model-r") || id.StartsWith("adslot") ||
                id.Contains("rtb") || id.Contains("adv"))
                return true;

            if (node.TagName == "FIELDSET" || node.TagName == "STYLE" || node.TagName == "SCRIPT") return true;
            node = node.ParentElement;
        }
        return false;
    }

    static string PreText(IElement pre)
    {
        var s = pre.TextContent;
        return s.Replace("\r\n", "\n").Trim('\n');
    }

    static string InlineText(IElement el) => Clean(el.TextContent);

    public static string Clean(string s)
    {
        s = s.Replace('\u00A0', ' ').Replace('\r', ' ');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\n\s*", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s{2,}", " ");
        return s.Trim();
    }

    public static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>Находит ссылку на страницу «Вопросы для самопроверки» внутри урока.</summary>
    public static string? FindQuestionsUrl(string lessonHtml, string lessonUrl)
    {
        var doc = Parser.ParseDocument(lessonHtml);
        foreach (var a in doc.QuerySelectorAll("a[href*='questions']"))
        {
            var href = a.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (lessonUrl.Contains("/questions/")) return null;
            return NormalizeUrl(href, lessonUrl);
        }
        return null;
    }

    /// <summary>Страница «Вопросы для самопроверки»: готовые вопросы с ответами.</summary>
    public static List<Core.Model.QuizItem> ParseQuestions(string html, int lessonId)
    {
        var doc = Parser.ParseDocument(html);
        var items = new List<Core.Model.QuizItem>();
        int idx = 0;

        foreach (var q in doc.QuerySelectorAll("div.question"))
        {
            var textEl = q.QuerySelector("div.question-text");
            var answerEl = q.QuerySelector("div.answer-content");
            if (textEl is null) continue;

            var code = textEl.QuerySelector("pre");
            var promptParts = new List<string>();
            foreach (var p in textEl.QuerySelectorAll("p")) promptParts.Add(InlineText(p));

            var codeText = code is null ? null : PreText(code).Trim();
            var answer = answerEl is null ? "" : InlineText(answerEl);

            if (answer.Length == 0) continue;

            string correct = answer;
            var options = ExtractOptions(answer);

            items.Add(new Core.Model.QuizItem
            {
                LessonId = lessonId,
                Index = idx++,
                Format = options.Count >= 2 ? Core.Model.QuizFormat.MultipleChoice : Core.Model.QuizFormat.OpenText,
                Prompt = string.Join(" ", promptParts),
                Code = codeText,
                Options = options,
                CorrectAnswer = correct,
                Explanation = answer,
                FromSource = true,
            });
        }
        return items;
    }

    static List<string> ExtractOptions(string answer)
    {
        var m = System.Text.RegularExpressions.Regex.Match(answer, @"^(.{0,120}?)[:?]\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return new();
        var parts = m.Groups[2].Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Clean(s)).Where(s => s.Length > 0 && s.Length < 80).Take(6).ToList();
        return parts.Count >= 2 ? parts : new();
    }
}

public sealed record LessonParseResult(string Title, string UpdatedAt, List<Core.Model.ContentBlock> Blocks, string PlainText, int CodeCount, string? QuestionsUrl);
