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
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : "qa-v05");
        Directory.CreateDirectory(root);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var now = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.FromHours(8));
            var dataRoot = Path.Combine(root, "ui-" + Guid.NewGuid().ToString("N"));
            using var store = new DataStore(Path.Combine(dataRoot, "Data"), false, dataRoot);
            var engine = new LearningEngine(store.LoadCatalog(), new UserState { SchemaVersion = 4, WeeklyNewGoal = 6, DailyNewLimit = 1 }, () => now);
            Assert(engine.Catalog.Count(w => w.ScopeId == VocabularyImport.CoreScopeId) == 3000, "真实核心词库为3000独立条目");
            var word = engine.AddSchoolWord("remarkable", "adj. 非凡的；显著的", "She made remarkable progress by learning a little every day.");
            store.Save(engine.State);
            var window = new MainWindow(store, () => now);
            Render(window, root, "01-today");
            Assert(Find<Button>(window, "NavReport") == null && Find<Button>(window, "NavReview") == null, "主导航简化，未恢复晚间汇报");
            Click(window, "StartLearning");
            Assert(VisibleText(window).Contains(word.Meaning), "新词先显示教学内容");
            Assert(Find<Button>(window, "CompleteLesson") is not null, "学习主操作可用");
            Render(window, root, "02-learn");
            Layout(window, 1000, 680);
            var primary = Find<Button>(window, "CompleteLesson", false)!;
            var point = primary.TransformToAncestor((Visual)window.Content).Transform(new Point());
            Assert(point.Y + primary.ActualHeight <= 680 && point.Y > 0, "最小窗口高度下主操作固定可见");
            Render(window, root, "03-learn-small", 1000, 680);
            foreach (var expander in Descendants((DependencyObject)window.Content).OfType<Expander>()) expander.IsExpanded = true;
            var note = Find<TextBox>(window, "LessonNote")!;
            note.Text = "remember remarkable progress";
            window.ShowDictionary(""); window.ShowLearn();
            foreach (var expander in Descendants((DependencyObject)window.Content).OfType<Expander>()) expander.IsExpanded = true;
            Assert(Find<TextBox>(window, "LessonNote")!.Text.Contains("remember"), "切页返回保留未提交学习笔记");
            Click(window, "CompleteLesson");
            Assert(window.Engine.GetProgress(word.Id).NextReviewAt > now.AddHours(6), "初学安排次日，不强制十分钟等待");
            Assert(window.Engine.GetSession(window.Engine.Today).LearnedWordIds.Contains(word.Id), "完成新学真实计数");
            now = now.AddDays(1);
            window.ShowHome(); window.ShowLearn();
            Assert(!VisibleText(window).Contains(word.Meaning), "到期词先回忆，释义隐藏");
            Assert(Find<TextBox>(window, "RecallAnswer") is null, "识义不强迫输入中文");
            Render(window, root, "04-recall-hidden");
            Click(window, "SubmitRecall");
            Assert(VisibleText(window).Contains(word.Meaning), "显示答案后有释义和语境");
            Render(window, root, "05-recall-answer");
            Click(window, "RecallGood");
            Assert(window.Engine.GetProgress(word.Id).NextReviewAt > now, "一次正确回忆即安排未来复现");
            Assert(!window.Engine.TodayQueue().Any(x => x.Word.Id == word.Id), "正确到期词不再强制读讲解");
            var due = window.Engine.GetProgress(word.Id).NextReviewAt;
            window.ShowReviewHome(); Find<CheckBox>(window, "SpellingMode")!.IsChecked = true;
            window.StartReview("All");
            Assert(!VisibleText(window).Contains(word.Term, StringComparison.OrdinalIgnoreCase), "拼写前含撤销栏均不泄露英文");
            Find<TextBox>(window, "RecallAnswer")!.Text = "wrongspelling";
            Click(window, "SubmitRecall");
            Assert(Find<Button>(window, "RecallGood")!.IsEnabled == false, "拼错不允许自评拼对");
            Click(window, "RecallAgain");
            Assert(window.Engine.GetProgress(word.Id).NextReviewAt == due, "拼写失败不清空识义进度");
            Click(window, "UndoAction");
            Assert(window.Engine.GetProgress(word.Id).Spelling.Lapses == 0, "撤销恢复独立拼写状态");
            window.ShowReviewHome(); Find<CheckBox>(window, "SpellingMode")!.IsChecked = false;
            window.StartReview("All"); Click(window, "HintRecall");
            Assert(Find<Button>(window, "RecallGood")!.IsEnabled == false, "没想起来不能标独立记得");
            Click(window, "RecallAgain");
            window.ShowHome(); Click(window, "AppendToday");
            Assert(window.Engine.TodayQueue().Any(x => x.Word.Id == word.Id && x.Task.Kind == "Relearn"), "主动加练包含需要补学的词");
            for (var i = 0; i < 5 && !VisibleText(window).Contains(word.Meaning); i++) Click(window, "CompleteLesson");
            Assert(VisibleText(window).Contains(word.Meaning), "保留当前卡片后可连续进入补学");
            Click(window, "ExcludeCandidate");
            Assert(window.Engine.GetProgress(word.Id).PermanentlySkipped, "移出一步完成");
            Click(window, "UndoAction");
            Assert(!window.Engine.GetProgress(word.Id).PermanentlySkipped, "撤销恢复误移出的词");
            window.ShowAllWords();
            Find<TextBox>(window, "CatalogSearch")!.Text = word.Term;
            Click(window, "SelectCatalogPage"); Click(window, "ExcludeSelectedWords");
            Assert(window.Engine.GetProgress(word.Id).PermanentlySkipped, "词库批量移除生效");
            Click(window, "UndoAction");
            Assert(!window.Engine.GetProgress(word.Id).PermanentlySkipped, "批量移除整体撤销");
            Render(window, root, "06-library");
            window.ShowDictionary("serendipity");
            WaitUntil(() => Find<TextBox>(window, "DictionaryMeaning") is not null);
            Assert(!string.IsNullOrWhiteSpace(Find<TextBox>(window, "DictionaryMeaning")!.Text), "后台离线查询得到教材外中文释义");
            Render(window, root, "07-dictionary");
            Click(window, "AddDictionaryWord");
            Assert(window.Engine.Catalog.Any(w => w.Term == "serendipity" && window.Engine.GetProgress(w.Id).InPool), "查词后主动加入个人生词");
            window.ShowImport();
            Find<TextBox>(window, "ImportText")!.Text = "serendipity\nserendipity\nflorblequazz";
            Click(window, "PreviewVocabularyImport");
            WaitUntil(() => Find<Button>(window, "CommitVocabularyImport")?.IsEnabled == true);
            Assert(VisibleText(window).Contains("去重 1 条"), "TXT预览合并重复词");
            Render(window, root, "08-import");
            Click(window, "CommitVocabularyImport");
            Assert(window.Engine.Catalog.Any(w => w.Term == "florblequazz" && w.Meaning == ""), "未匹配生词保留输入，不编造意思");
            Click(window, "UndoAction");
            Assert(!window.Engine.Catalog.Any(w => w.Term == "florblequazz"), "整批导入撤销");
            window.ShowWeek(window.Engine.Today); Render(window, root, "09-week");
            Assert(VisibleText(window).Contains("原目标") && VisibleText(window).Contains("待安排队列"), "计划并列显示原目标与积压");
            window.ShowSettings(); Render(window, root, "10-settings");
            window.ShowProfiles(); Render(window, root, "11-profiles");
            var china = window.Engine.Catalog.Single(w => w.Term == "china" && w.ScopeId == VocabularyImport.CoreScopeId);
            var country = window.Engine.Catalog.Single(w => w.Term == "China" && w.ScopeId == VocabularyImport.CoreScopeId);
            var countryMeaning = country.Meaning;
            window.ShowDictionary("china", china.Id);
            WaitUntil(() => Find<TextBox>(window, "DictionaryMeaning") is not null);
            Find<TextBox>(window, "DictionaryMeaning")!.Text = "n. 瓷器（个人核对）";
            Click(window, "AddDictionaryWord");
            Assert(window.Engine.FindWord(country.Id)!.Meaning == countryMeaning && window.Engine.FindWord(china.Id)!.Meaning.Contains("瓷器"), "词典编辑按稳定身份隔离China/china");
            window.ShowDictionary("CHINA");
            WaitUntil(() => VisibleText(window).Contains("不同学习条目"));
            Assert(Find<Button>(window, "AddDictionaryWord") is null, "大小写含义不明确时先选条目");
            var sense = window.Engine.AddPersonalWord(new VocabularyWord { Id = "ui-sense", Term = word.Term, EntryKind = "Sense", Meaning = "测试独立新义" }, "测试");
            var baseMeaning = window.Engine.FindWord(word.Id)!.Meaning;
            window.ShowDictionary(sense.Term, sense.Id);
            WaitUntil(() => Find<TextBox>(window, "DictionaryMeaning") is not null);
            Find<TextBox>(window, "DictionaryMeaning")!.Text = "测试独立新义的补充"; Click(window, "AddDictionaryWord");
            Assert(window.Engine.FindWord(word.Id)!.Meaning == baseMeaning, "同拼写独立新义保存不覆盖基本义");
            window.ShowLearn();
            foreach (var expander in Descendants((DependencyObject)window.Content).OfType<Expander>()) expander.IsExpanded = true;
            var oldNote = Find<TextBox>(window, "LessonNote");
            if (oldNote is not null) oldNote.Text = "旧草稿不应覆盖恢复记录";
            var restoredState = System.Text.Json.JsonSerializer.Deserialize<UserState>(System.Text.Json.JsonSerializer.Serialize(window.Engine.State))!;
            restoredState.Progress[word.Id].Note = "restored remember B";
            typeof(MainWindow).GetMethod("ApplyRestoredState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [restoredState]);
            window.ShowLearn();
            foreach (var expander in Descendants((DependencyObject)window.Content).OfType<Expander>()) expander.IsExpanded = true;
            Assert(Find<TextBox>(window, "LessonNote")!.Text == "restored remember B", "恢复备份后旧界面草稿已清理");
            using (var blockedFile = new FileStream(Path.Combine(store.Folder, "learning-data.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                window.ShowHome();
                Assert(VisibleText(window).Contains("本机保存失败"), "实际写入被锁时明确提示保存失败");
                window.ShowSettings();
                Assert(VisibleText(window).Contains("本机保存失败"), "保存错误跨页保留，不被导航掩盖");
            }
            window.ShowHome();
            Assert(!VisibleText(window).Contains("本机保存失败"), "写入恢复后才清除保存错误");
            window.Close(); store.Dispose();
            using var reopened = new DataStore(Path.Combine(dataRoot, "Data"), false, dataRoot);
            var saved = new LearningEngine(reopened.LoadCatalog(), reopened.LoadState(), () => now);
            Assert(saved.GetProgress(word.Id).Note.Contains("remember"), "重启保留学习笔记");
            Assert(!saved.GetProgress(word.Id).PermanentlySkipped && saved.GetProgress(word.Id).InPool, "重启保留撤销后的状态");
            Console.WriteLine($"PASS: {_checks} UI behavior checks; screenshots: {root}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new Exception("FAIL: " + message); _checks++; Console.WriteLine("PASS: " + message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    { yield return node; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private static void Layout(Window window, double width = 1200, double height = 820)
    { var root = (FrameworkElement)window.Content; root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
    private static T? Find<T>(Window window, string name, bool layout = true) where T : FrameworkElement
    { if (layout) Layout(window); return Descendants((DependencyObject)window.Content).OfType<T>().FirstOrDefault(e => e.Name == name); }
    private static string VisibleText(Window window)
    {
        Layout(window);
        bool Visible(DependencyObject node) { for (var n = node; n is not null; n = VisualTreeHelper.GetParent(n)) if (n is UIElement u && u.Visibility != Visibility.Visible) return false; return true; }
        return string.Join("\n", Descendants((DependencyObject)window.Content).OfType<TextBlock>().Where(Visible).Select(x => x.Text));
    }
    private static void Click(Window window, string name)
    { var button = Find<Button>(window, name) ?? throw new Exception("Missing button " + name); Assert(button.IsEnabled, "操作可点击: " + name); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new Exception("Timed out waiting for background UI result");
            var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
    private static void Render(Window window, string dir, string file, int width = 1200, int height = 820)
    {
        Layout(window, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render((Visual)window.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, file + ".png")); encoder.Save(stream);
    }
}
