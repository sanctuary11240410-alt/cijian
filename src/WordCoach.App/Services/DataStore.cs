using System.Reflection;
using System.Text.Json;
using System.IO;
using WordCoach.Core;

namespace WordCoach.App.Services;

public sealed class DataStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string Folder { get; }
    public string ApplicationRoot => _appRoot;
    public string LoadNotice { get; private set; } = "";
    private string DataPath => Path.Combine(Folder, "learning-data.json");
    private string BackupPath => Path.Combine(Folder, "learning-data.backup.json");
    private FileStream? _lease;
    private readonly bool _allowMigration;
    private readonly string _appRoot;

    public DataStore(string? folder = null, bool? allowMigration = null, string? appRoot = null)
    { _appRoot = Path.GetFullPath(appRoot ?? AppContext.BaseDirectory); Folder = folder ?? Path.Combine(_appRoot, "Data"); _allowMigration = allowMigration ?? folder is null; }

    public IReadOnlyList<VocabularyWord> LoadCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Assets.vocabulary.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("词库资源不存在");
        var words = JsonSerializer.Deserialize<List<VocabularyWord>>(stream, JsonOptions) ?? [];
        words = CoreVocabulary.Merge(words).ToList();
        TeachingSupplements.Apply(words);
        return ExtensionVocabulary.Merge(words);
    }

    public UserState LoadState()
    {
        Directory.CreateDirectory(Folder);
        _lease ??= new FileStream(Path.Combine(Folder, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (_allowMigration && !File.Exists(DataPath) && !File.Exists(BackupPath))
        {
            var parent = Directory.GetParent(_appRoot.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
            var legacy = parent is null ? "" : new[] { "词间-课外拓展版", "词间-循环学习版", "高中单词计划桌面版" }
                .Select(name => Path.Combine(parent, name, "Data", "learning-data.json"))
                .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "";
            if (File.Exists(legacy) && Path.GetFullPath(legacy) != Path.GetFullPath(DataPath))
            {
                var state = ReadBackup(legacy);
                File.Copy(legacy, Path.Combine(Folder, "旧版原始记录.json"), false);
                Save(state);
                LoadNotice = "已复制同目录最近更新的旧版学习记录，原始文件保留；已有生词、复现时间和熟词移出记录继续有效。";
            }
        }
        if (!File.Exists(DataPath) && !File.Exists(BackupPath)) return new UserState { SchemaVersion = 4 };
        try { return ReadBackup(DataPath); }
        catch (Exception mainError)
        {
            try
            {
                var state = ReadBackup(BackupPath);
                if (File.Exists(DataPath)) File.Copy(DataPath, Path.Combine(Folder, $"损坏原文件-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
                File.Copy(BackupPath, DataPath, true);
                LoadNotice = "主数据文件读取失败，已恢复上一份自动备份。损坏文件保留供检查。";
                return state;
            }
            catch { throw new InvalidDataException("学习记录与自动备份都无法读取。未重置或覆盖数据，请使用之前导出的备份。", mainError); }
        }
    }

    public UserState ReadBackup(string path)
    {
        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.EnumerateObject().Any(x => x.Name.Equals("Progress", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("不是有效的学习记录文件");
        var state = JsonSerializer.Deserialize<UserState>(text, JsonOptions) ?? throw new InvalidDataException("数据为空");
        if (state.SchemaVersion > 4) throw new InvalidDataException("数据来自更新版本，请使用对应版本打开");
        if (state.Progress is null || state.Weeks is null || state.Events is null || state.Sessions is null || state.ImportedWords is null || state.SchoolWords is null || state.ImportBatches is null)
            throw new InvalidDataException("学习记录包含无效空字段");
        return state;
    }

    public void Save(UserState state)
    {
        Directory.CreateDirectory(Folder);
        var temporary = Path.Combine(Folder, $"learning-data.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(DataPath)) File.Replace(temporary, DataPath, BackupPath, true);
            else File.Move(temporary, DataPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string Export(UserState state, string destination)
    { File.WriteAllText(destination, JsonSerializer.Serialize(state, JsonOptions)); return destination; }
    public void Dispose() { _lease?.Dispose(); _lease = null; }
}
