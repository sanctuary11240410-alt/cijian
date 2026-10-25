using System.IO;
using System.Text.Json;

namespace WordCoach.App.Services;

public sealed class LocalProfile
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "我的学习";
    public string Description { get; set; } = "江苏 · 译林版已学完";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class ProfileManifest
{
    public int Version { get; set; } = 1;
    public string ActiveProfileId { get; set; } = "default";
    public List<LocalProfile> Profiles { get; set; } = [new()];
}

/// <summary>Only the manifest is shared. Learning data, backups and locks stay inside each profile folder.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public ProfileManifest Manifest { get; private set; }
    public string LoadNotice { get; private set; } = "";
    private string ManifestPath => Path.Combine(Root, "profiles.json");
    public ProfileStore(string? root = null)
    {
        Root = Path.GetFullPath(root ?? AppContext.BaseDirectory);
        Manifest = ReadManifest();
    }
    public static DataStore CreateActiveDataStore()
    {
        var profiles = new ProfileStore();
        return profiles.CreateDataStore(profiles.Manifest.ActiveProfileId);
    }
    public string ActiveName => Manifest.Profiles.Single(p => p.Id == Manifest.ActiveProfileId).Name;
    public string ActiveDescription => Manifest.Profiles.Single(p => p.Id == Manifest.ActiveProfileId).Description;
    public string FolderFor(string profileId)
    {
        ValidateId(profileId);
        if (!Manifest.Profiles.Any(p => p.Id == profileId)) throw new ArgumentException("未找到该学习档案。");
        return profileId == "default" ? Path.Combine(Root, "Data") : Path.Combine(Root, "Profiles", profileId, "Data");
    }
    public DataStore CreateDataStore(string id) => new(FolderFor(id), allowMigration: id == "default", appRoot: Root);
    public LocalProfile Create(string name, string description = "通用模式 · 导入个人不会词")
    {
        name = name.Trim();
        if (name.Length is 0 or > 30) throw new ArgumentException("档案名称需为 1 至 30 个字符。");
        if (Manifest.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("已有同名档案，请换一个名称。");
        using var lease = AcquireManifestLease();
        // Re-read under the shared lock so two windows cannot silently drop each other's profiles.
        Manifest = ReadManifest();
        if (Manifest.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("已有同名档案，请换一个名称。");
        var profile = new LocalProfile { Id = Guid.NewGuid().ToString("N"), Name = name, Description = description };
        Manifest.Profiles.Add(profile);
        SaveManifest();
        return profile;
    }
    public void SetActive(string id)
    {
        ValidateId(id);
        using var lease = AcquireManifestLease();
        Manifest = ReadManifest();
        if (!Manifest.Profiles.Any(p => p.Id == id)) throw new ArgumentException("未找到该学习档案。");
        Manifest.ActiveProfileId = id;
        SaveManifest();
    }
    private FileStream AcquireManifestLease()
    {
        Directory.CreateDirectory(Root);
        return new FileStream(Path.Combine(Root, ".profiles.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private ProfileManifest ReadManifest()
    {
        if (!File.Exists(ManifestPath) && !File.Exists(ManifestPath + ".backup")) return new();
        try { return Parse(ManifestPath); }
        catch (Exception error)
        {
            try
            {
                var manifest = Parse(ManifestPath + ".backup");
                LoadNotice = "档案目录文件损坏，已使用上一份目录备份；各档案学习记录未改动。";
                return manifest;
            }
            catch { throw new InvalidDataException("档案目录与目录备份均无法读取。学习记录未重置，请保留整个程序文件夹。", error); }
        }
    }
    private static ProfileManifest Parse(string path)
    {
        var manifest = JsonSerializer.Deserialize<ProfileManifest>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("档案目录为空。");
        if (manifest.Version != 1 || manifest.Profiles is null || manifest.Profiles.Count == 0 ||
            manifest.Profiles.Select(p => p.Id).Distinct().Count() != manifest.Profiles.Count ||
            !manifest.Profiles.Any(p => p.Id == manifest.ActiveProfileId)) throw new InvalidDataException("档案目录格式无效。");
        foreach (var profile in manifest.Profiles) ValidateId(profile.Id);
        return manifest;
    }
    private static void ValidateId(string id)
    {
        if (id != "default" && !(id.Length == 32 && Guid.TryParseExact(id, "N", out _))) throw new InvalidDataException("档案标识无效。");
    }
    private void SaveManifest()
    {
        Directory.CreateDirectory(Root);
        var temp = ManifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, Manifest, JsonOptions); stream.Flush(true); }
            if (File.Exists(ManifestPath)) File.Replace(temp, ManifestPath, ManifestPath + ".backup", true);
            else File.Move(temp, ManifestPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
