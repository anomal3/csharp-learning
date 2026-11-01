using CsharpLearning.Core.Model;
using CsharpLearning.Core.Storage;

namespace CsharpLearning.Core.Analytics;

public enum Health { Good, Normal, Struggling, NotRead }

public sealed record ConceptStat(
    string Concept,
    double ReadMinutes,
    int LessonsRead,
    int LessonsTotal,
    int QuestionsAnswered,
    int QuestionsCorrect,
    double Accuracy,
    double MinutesPerCorrectAnswer,
    double MinutesPerLesson,
    Health Health,
    string Verdict);

public sealed record ChapterStat(int Number, string Title, int TotalLessons, int ReadLessons, double ReadMinutes, int QuestionsAnswered, int QuestionsCorrect, double Accuracy, double ProgressPercent);

public sealed record LearnerSummary(
    double TotalMinutes,
    int LessonsRead,
    int LessonsTotal,
    int QuestionsAnswered,
    int QuestionsCorrect,
    double Accuracy,
    DateTime? FirstActivity,
    DateTime? LastActivity,
    IReadOnlyList<ConceptStat> Concepts,
    IReadOnlyList<ChapterStat> Chapters)
{
    public double OverallProgressPercent => LessonsTotal == 0 ? 0 : 100.0 * LessonsRead / LessonsTotal;
}

/// <summary>
/// Аналитика обучения: связывает время чтения с верными ответами и делает вывод уровня
/// «по теме «Цикл» потрачено 22 мин, точность 40% — скорее всего, do..while непонятен».
/// </summary>
public static class AnalyticsEngine
{
    public static LearnerSummary Build(ProgressDb progress, IReadOnlyList<Chapter> chapters)
    {
        var byLesson = progress.LoadProgress().ToDictionary(p => p.LessonId);

        var byConcept = new Dictionary<string, ConceptAcc>();

        foreach (var ch in chapters)
        {
            foreach (var lref in ch.Lessons)
            {
                var concepts = ConceptsOf(lref.Title);
                byLesson.TryGetValue(lref.Id, out var p);

                double minutes = (p?.SecondsRead ?? 0) / 60.0;
                int answered = p?.QuestionsAnswered ?? 0;
                int correct = p?.QuestionsCorrect ?? 0;
                bool read = minutes > 0.4 || answered > 0;

                foreach (var concept in concepts)
                {
                    if (!byConcept.TryGetValue(concept, out var acc))
                        byConcept[concept] = acc = new ConceptAcc();
                    acc.Minutes += minutes;
                    acc.QuestionsAnswered += answered;
                    acc.QuestionsCorrect += correct;
                    acc.LessonsTotal++;
                    if (read) acc.LessonsRead++;
                }
            }
        }

        var conceptStats = byConcept.Select(kv =>
        {
            var a = kv.Value;
            double acc = a.QuestionsAnswered == 0 ? 0 : (double)a.QuestionsCorrect / a.QuestionsAnswered;
            double perCorrect = a.QuestionsCorrect == 0 ? a.Minutes : a.Minutes / a.QuestionsCorrect;
            double perLesson = a.Minutes / Math.Max(1, a.LessonsRead);
            var health = Judge(a, acc, perCorrect);
            return new ConceptStat(kv.Key, a.Minutes, a.LessonsRead, a.LessonsTotal, a.QuestionsAnswered, a.QuestionsCorrect,
                acc, perCorrect, perLesson, health, Verdict(kv.Key, a, acc, perCorrect, perLesson, health));
        })
        .OrderByDescending(c => c.Health switch { Health.Struggling => 4, Health.Normal => 3, Health.NotRead => 2, _ => 1 })
        .ThenByDescending(c => c.ReadMinutes)
        .ToList();

        var chapterStats = chapters.Select(ch =>
        {
            int read = ch.Lessons.Count(l => byLesson.TryGetValue(l.Id, out var p) && (p.SecondsRead > 25 || p.QuestionsAnswered > 0));
            double mins = ch.Lessons.Sum(l => byLesson.TryGetValue(l.Id, out var p) ? p.SecondsRead / 60.0 : 0);
            int ans = ch.Lessons.Sum(l => byLesson.TryGetValue(l.Id, out var p) ? p.QuestionsAnswered : 0);
            int cor = ch.Lessons.Sum(l => byLesson.TryGetValue(l.Id, out var p) ? p.QuestionsCorrect : 0);
            return new ChapterStat(ch.Number, ch.Title, ch.Lessons.Count, read, mins, ans, cor,
                ans == 0 ? 0 : (double)cor / ans, ch.Lessons.Count == 0 ? 0 : 100.0 * read / ch.Lessons.Count);
        }).ToList();

        string? first = progress.GetMeta("first_activity");
        string? last = progress.GetMeta("last_activity");

        return new LearnerSummary(
            conceptStats.Sum(c => c.ReadMinutes),
            byLesson.Count(p => p.Value.SecondsRead > 25 || p.Value.QuestionsAnswered > 0),
            chapters.Sum(c => c.Lessons.Count),
            conceptStats.Sum(c => c.QuestionsAnswered),
            conceptStats.Sum(c => c.QuestionsCorrect),
            conceptStats.Sum(c => c.QuestionsAnswered) == 0 ? 0
                : (double)conceptStats.Sum(c => c.QuestionsCorrect) / conceptStats.Sum(c => c.QuestionsAnswered),
            first is null ? null : DateTime.Parse(first),
            last is null ? null : DateTime.Parse(last),
            conceptStats,
            chapterStats);
    }

    static HashSet<string> ConceptsOf(string lessonTitle)
    {
        var set = new HashSet<string>();
        var t = lessonTitle.ToLowerInvariant();
        foreach (var a in Pedagogy.AnalogyLibrary.All)
        {
            foreach (var kw in a.Keywords)
            {
                if (t.Contains(kw.ToLowerInvariant()))
                {
                    set.Add(a.Concept);
                    break;
                }
            }
        }
        if (set.Count == 0) set.Add(lessonTitle);
        return set;
    }

    static Health Judge(ConceptAcc a, double acc, double perCorrect)
    {
        if (a.LessonsRead == 0 && a.Minutes == 0) return Health.NotRead;
        if (a.QuestionsAnswered == 0) return Health.Normal;
        if (acc < 0.6) return Health.Struggling;
        if (acc < 0.8 && a.Minutes / Math.Max(1, a.LessonsRead) > 7) return Health.Struggling;
        if (acc >= 0.9 && perCorrect < 5) return Health.Good;
        return Health.Normal;
    }

    static string Verdict(string concept, ConceptAcc a, double acc, double perCorrect, double perLesson, Health health) => health switch
    {
        Health.Struggling when a.QuestionsAnswered > 0 && acc < 0.6 =>
            $"Тема «{concept}»: прочитано {a.Minutes:F1} мин ({a.LessonsRead} ур.), отвечено {a.QuestionsAnswered} вопросов, верно {a.QuestionsCorrect} ({acc:P0}). Много чтения при низкой точности — материал, скорее всего, непонятен. Перечитайте пояснения и решите ещё 3–5 задач.",
        Health.Struggling =>
            $"Тема «{concept}»: {a.Minutes:F1} мин чтения, точность {acc:P0} ниже ожидаемой ({a.QuestionsCorrect}/{a.QuestionsAnswered}). Похоже, читается тяжело — вернитесь к аналогиям и примерам.",
        Health.Good =>
            $"Тема «{concept}» усвоена: {acc:P0} верных ответов ({a.QuestionsCorrect}/{a.QuestionsAnswered}), ~{perCorrect:F1} мин на верный ответ.",
        Health.Normal when a.QuestionsAnswered == 0 =>
            $"Тема «{concept}»: {a.Minutes:F1} мин чтения, ответов нет. Теория без задач забывается — пройдите вопросы урока.",
        Health.Normal =>
            $"Тема «{concept}»: {acc:P0} верных, {a.Minutes:F1} мин чтения (~{perLesson:F1} мин на урок). Нормально, но можно быстрее.",
        Health.NotRead => $"Тема «{concept}» ещё не начата.",
        _ => "",
    };

    sealed class ConceptAcc
    {
        public double Minutes;
        public int LessonsRead;
        public int LessonsTotal;
        public int QuestionsAnswered;
        public int QuestionsCorrect;
    }
}
