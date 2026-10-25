using System.Windows;
using System.Windows.Controls;
using WordCoach.App.Services;
using WordCoach.Core;

namespace WordCoach.App;

public partial class MainWindow
{
    private ProfileStore? _profiles;
    private string CurrentProfileName
    {
        get
        {
            try
            {
                _profiles ??= new ProfileStore(_store.ApplicationRoot);
                return _profiles.Manifest.Profiles.FirstOrDefault(p => System.IO.Path.GetFullPath(_profiles.FolderFor(p.Id))
                    .Equals(System.IO.Path.GetFullPath(_store.Folder), StringComparison.OrdinalIgnoreCase))?.Name ?? "本地学习档案";
            }
            catch { return "本地学习档案"; }
        }
    }
    public void ShowProfiles()
    {
        Page("profiles", "本地学习档案", "每个人的词库、进度与备份分别保存");
        _profiles = new ProfileStore(_store.ApplicationRoot);
        if (_profiles.LoadNotice.Length > 0) Notify(_profiles.LoadNotice);
        foreach (var profile in _profiles.Manifest.Profiles)
        {
            var active = System.IO.Path.GetFullPath(_store.Folder).TrimEnd('\\', '/') == System.IO.Path.GetFullPath(_profiles.FolderFor(profile.Id)).TrimEnd('\\', '/');
            var action = Btn(active ? "当前档案" : "切换到此档案", () => SwitchProfile(profile.Id), "", true);
            action.IsEnabled = !active;
            PageBody.Children.Add(Card(Stack(Row(Txt(profile.Name, 20, "#263547", true), action),
                Txt(profile.Description, 13, "#64748B"), Space(new Expander { Header = "数据存放位置", Content = Txt(_profiles.FolderFor(profile.Id), 11, "#64748B") }, 6))));
        }
        var name = Input("NewProfileName");
        name.MaxLength = 30;
        var preset = new ComboBox { Name = "NewProfilePreset", ItemsSource = new[] { "通用模式 · 导入个人不会词", "江苏 · 译林版已学完" }, SelectedIndex = 0 };
        PageBody.Children.Add(Card(Stack(Txt("新建独立档案", 18, "#263547", true),
            Space(Txt("适合家人或同学从自己的生词清单开始；新档案拥有单独的每日安排。", 13, "#64748B"), 6),
            Space(Txt("档案名称", 12, "#64748B"), 10), Space(name, 4), Space(preset, 10), Space(Btn("创建并进入", () =>
            {
                if (!Save()) return;
                var profile = _profiles.Create(name.Text, preset.SelectedItem?.ToString() ?? "通用模式");
                SwitchProfile(profile.Id, openImport: true);
            }, "CreateLocalProfile"), 12))));
        PageBody.Children.Add(Txt("切换前自动保存当前档案。备份学习记录时请先进入对应档案；复制整个程序文件夹可同时带走全部档案。", 12, "#64748B"));
    }

    private void SwitchProfile(string id, bool openImport = false)
    {
        _profiles ??= new ProfileStore(_store.ApplicationRoot);
        if (!Save()) return;
        var nextStore = _profiles.CreateDataStore(id);
        LearningEngine nextEngine;
        try
        {
            nextEngine = new LearningEngine(nextStore.LoadCatalog(), nextStore.LoadState(), _clock);
            nextEngine.RefreshPlan();
            nextStore.Save(nextEngine.State);
            _profiles.SetActive(id);
        }
        catch { nextStore.Dispose(); throw; }
        var previousStore = _store;
        _dictionary?.Dispose(); _dictionary = null;
        _store = nextStore; _engine = nextEngine;
        previousStore.Dispose();
        _reviewedThisRun.Clear(); _reviewWords.Clear(); _activeReview = null;
        _lessonHistory.Clear(); _lessonHistoryIndex = -1;
        _todaySession = false; _lessonNotes.Clear(); _dictionaryDrafts.Clear(); _dictionarySearchVersion++;
        _dictionaryEntryId = ""; _spellingMode = false;
        _selectedWords.Clear(); _catalogFilter = ""; _catalogScope = "全部学习词汇"; _catalogPage = 0;
        _dictionaryQuery = ""; _importText = ""; _importComplete = false; _importExtensions = false;
        _importSource = "TXT 生词清单"; _importGeneration++;
        if (openImport) ShowImport(); else ShowHome();
        Notify($"已进入“{_profiles.ActiveName}”。学习进度、撤销与备份均独立保存。" + nextStore.LoadNotice);
    }
}
