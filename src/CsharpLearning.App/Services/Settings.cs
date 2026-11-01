using System.IO;
using System.Text.Json;

namespace CsharpLearning.App.Services;

public sealed class AppSettings
{
    public bool AdvancedMode { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public int KnownContentVersion { get; set; }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Opts) ?? new AppSettings();
        }
        catch { /* повреждённый файл — начинаем с настроек по умолчанию */ }
        return new AppSettings();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Opts));
        }
        catch { /* настройки не критичны для работы */ }
    }
}
