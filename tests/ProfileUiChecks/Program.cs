using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WordCoach.App;
using WordCoach.App.Services;
using WordCoach.Core;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var root = Path.GetFullPath(Path.Combine("qa-v05", "profiles-ui-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            var now = new DateTimeOffset(2026, 9, 12, 19, 0, 0, TimeSpan.FromHours(8));
            var store = new DataStore(Path.Combine(root, "Data"), appRoot: root);
            var engine = new LearningEngine(store.LoadCatalog(), store.LoadState(), () => now);
            engine.AddPersonalWord(new VocabularyWord { Term = "firstprofileonly", Meaning = "第一档案个人词" }, "学校");
            store.Save(engine.State);
            var window = new MainWindow(store, () => now);
            Layout(window);
            window.ShowDictionary("retain");
            PumpUntil(() => Find<TextBox>(window, "DictionaryMeaning") is not null);
            Find<TextBox>(window, "DictionaryMeaning")!.Text = "第一档案尚未保存的查词草稿";
            window.ShowProfiles(); Layout(window);
            Assert(Text(window).Contains("江苏 · 译林版已学完"), "原有默认档案保留个人背景");
            Find<TextBox>(window, "NewProfileName")!.Text = "通用学习者";
            Click(Find<Button>(window, "CreateLocalProfile")!);
            Assert(Find<TextBox>(window, "ImportText") is not null, "新建通用档案直接进入不会词导入");
            Assert(!window.Engine.Catalog.Any(w => w.Term == "firstprofileonly"), "新档案不带入原档案个人词");
            Assert(window.Engine.State.ImportBatches.Count == 0 && !window.Engine.CanUndo, "新档案无其他档案导入批次和撤销记录");
            var profiles = new ProfileStore(root);
            var secondId = profiles.Manifest.ActiveProfileId;
            Assert(secondId != "default" && profiles.Manifest.Profiles.Count == 2, "创建动作持久保存当前档案");
            window.ShowDictionary("retain");
            PumpUntil(() => Find<TextBox>(window, "DictionaryMeaning") is not null);
            Assert(Find<TextBox>(window, "DictionaryMeaning")!.Text != "第一档案尚未保存的查词草稿", "未保存查词草稿不串入新档案");
            window.Engine.AddPersonalWord(new VocabularyWord { Term = "secondprofileonly", Meaning = "第二档案个人词" }, "个人");
            window.ShowProfiles(); Layout(window);
            var switchDefault = Walk(window).OfType<Button>().First(b => b.Content?.ToString() == "切换到此档案");
            Click(switchDefault);
            Assert(window.Engine.Catalog.Any(w => w.Term == "firstprofileonly") && !window.Engine.Catalog.Any(w => w.Term == "secondprofileonly"),
                "切回默认档案恢复它的词库，隔离另一档案数据");
            Assert(new ProfileStore(root).Manifest.ActiveProfileId == "default", "切换后重启入口记录正确档案");
            window.ShowProfiles(); Layout(window);
            Render(window, Path.Combine(root, "profiles.png"));
            window.Close(); store.Dispose();
            using (var secondStore = new ProfileStore(root).CreateDataStore(secondId))
            {
                var secondState = secondStore.LoadState();
                Assert(secondState.SchoolWords.Any(w => w.Term == "secondprofileonly"), "切换前保存第二档案的新词");
            }
            using var reopenedStore = new ProfileStore(root).CreateDataStore("default");
            var reopened = new MainWindow(reopenedStore, () => now);
            Assert(reopened.Engine.Catalog.Any(w => w.Term == "firstprofileonly") && !reopened.Engine.CanUndo,
                "关闭重启后进度恢复且不会复用旧窗口撤销");
            reopened.Close();
            Console.WriteLine($"Profile UI checks passed: {_checks}. Render: {Path.Combine(root, "profiles.png")}");
            app.Shutdown(0);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Assert(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("FAIL: " + name); _checks++; Console.WriteLine("PASS " + name); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        if (root is Window window) root = (DependencyObject)window.Content;
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    { if (root is MainWindow window) Layout(window); return Walk(root).OfType<T>().FirstOrDefault(x => x.Name == name); }
    private static string Text(DependencyObject root) => string.Join("\n", Walk(root).OfType<TextBlock>().Select(x => x.Text));
    private static void Click(Button button) { if (!button.IsEnabled) throw new InvalidOperationException("按钮不可用"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void Layout(MainWindow window)
    { var root = (FrameworkElement)window.Content; root.Measure(new Size(1200, 860)); root.Arrange(new Rect(0, 0, 1200, 860)); root.UpdateLayout(); }
    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame(); var begin = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (condition() || (DateTime.UtcNow - begin).TotalSeconds > 15) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        if (!condition()) throw new TimeoutException("异步界面未在 15 秒内完成");
    }
    private static void Render(MainWindow window, string path)
    {
        var bitmap = new RenderTargetBitmap(1200, 860, 96, 96, PixelFormats.Pbgra32); bitmap.Render((Visual)window.Content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
