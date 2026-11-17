using System.IO;
using CsharpLearning.Core.Pedagogy;

namespace CsharpLearning.App.Services;

public static class Paths
{
    public static string DataDir { get; private set; } = "";
    public static string AppBase => AppContext.BaseDirectory;

    /// <summary>Какой курс открыт сейчас (C# или Rust): от него зависят имена баз и манифеста.</summary>
    public static CourseProfile Profile { get; set; } = CourseProfile.CSharp;

    public static string CourseDb => Path.Combine(DataDir, Profile.DbFile);
    public static string ProgressDb => Path.Combine(DataDir, Profile.ProgressFile);
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string RunWork => Path.Combine(DataDir, "run");
    public static string UpdateCache => Path.Combine(DataDir, "update");

    /// <summary>Контент, упакованный рядом с программой (офлайн-дистрибутив).</summary>
    public static string BundledCourseDb => Path.Combine(AppBase, "content", Profile.DbFile);

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
