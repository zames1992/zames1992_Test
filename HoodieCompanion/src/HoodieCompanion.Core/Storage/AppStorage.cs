using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoodieCompanion.Storage;

/// <summary>
/// Local-only JSON persistence with atomic writes and a backup copy.
/// write: file.tmp -> (replace) file, previous file kept as file.bak.
/// read: file, else file.bak, else defaults (a corrupt file is preserved as file.corrupt-*).
/// </summary>
public sealed class AppStorage
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly object _gate = new();

    public AppStorage(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoodieCompanion");

    public string PathFor(string name) => System.IO.Path.Combine(Root, name);

    public T Load<T>(string name, Func<T> fallback) where T : class
    {
        lock (_gate)
        {
            var path = PathFor(name);
            if (TryRead(path, out T? value)) return value!;
            if (File.Exists(path))
            {
                try { File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { /* best effort */ }
            }
            if (TryRead(path + ".bak", out value)) return value!;
            return fallback();
        }
    }

    public void Save<T>(string name, T value)
    {
        lock (_gate)
        {
            var path = PathFor(name);
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(value, Json);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(json);
                sw.Flush();
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
                File.Copy(path, path + ".bak", true);
            }
            File.Move(tmp, path, true);
        }
    }

    private static bool TryRead<T>(string path, out T? value) where T : class
    {
        value = null;
        try
        {
            if (!File.Exists(path)) return false;
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return false;
            value = JsonSerializer.Deserialize<T>(text, Json);
            return value is not null;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}
