using System.IO;

namespace CsharpLearning.App.Services;

public static class Paths
{
    public static string DataDir { get; private set; } = "";
    public static string AppBase => AppContext.BaseDirectory;

    public static string CourseDb => Path.Combine(DataDir, "course.db");
    public static string ProgressDb => Path.Combine(DataDir, "progress.db");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string RunWork => Path.Combine(DataDir, "run");
    public static string UpdateCache => Path.Combine(DataDir, "update");

    /// <summary>Контент, упакованный рядом с программой (офлайн-дистрибутив).</summary>
    public static string BundledCourseDb => Path.Combine(AppBase, "content", "course.db");

    public static void Init(string[] args)
    {
        string? overrideDir = args.FirstOrDefault(a => a.StartsWith("--data="))?[7..];
        DataDir = string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CsharpLearning")
            : overrideDir;

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(RunWork);
        Directory.CreateDirectory(UpdateCache);
    }
}
