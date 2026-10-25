using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WordCoach.App.Services;
using WordCoach.Core;

namespace WordCoach.App;

public partial class MainWindow : Window
{
    private DataStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private LearningEngine _engine;
    private string _page = "";
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _reviewedThisRun = [];
    private string _reviewScope = "Due";
    private bool _spellingMode;
    private List<VocabularyWord> _reviewWords = [];
    private VocabularyWord? _activeReview;
    private readonly List<string> _lessonHistory = [];
    private int _lessonHistoryIndex = -1;
    private bool _todaySession;
    private Action? _primaryAction;
    private readonly Dictionary<string, string> _lessonNotes = [];
    private double _cardActiveSeconds;
    private DateTime _lastInput = DateTime.UtcNow;
    private DateTime _lastTick = DateTime.UtcNow;
    private string _timedWord = "";
    private string _timedKind = "";
    private string _undoPage = "learn";
    private string _saveFailure = "";
    private DateOnly Today => _engine.Today;
    public LearningEngine Engine => _engine;

    public MainWindow() : this(ProfileStore.CreateActiveDataStore(), () => DateTimeOffset.Now) { }
    public MainWindow(DataStore store, Func<DateTimeOffset> clock)
    {
        _store = store; _clock = clock;
        InitializeComponent();
        _engine = new LearningEngine(_store.LoadCatalog(), _store.LoadState(), clock);
        _engine.RefreshPlan();
        Closing += (_, e) => { _timer?.Stop(); if (!Save()) { e.Cancel = true; _timer?.Start(); } };
        Closed += (_, _) => { _dictionary?.Dispose(); _store.Dispose(); };
        PreviewMouseDown += (_, _) => _lastInput = DateTime.UtcNow;
        PreviewKeyDown += (_, e) => {
            _lastInput = DateTime.UtcNow;
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            { Guard(UndoOperation); e.Handled = true; }
            else if ((e.Key == Key.Space || e.Key == Key.Enter) && Keyboard.Modifiers == ModifierKeys.None && _primaryAction is not null)
            { Guard(_primaryAction); e.Handled = true; }
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => {
            var stamp = DateTime.UtcNow;
            if (IsActive && _timedWord.Length > 0 && (stamp - _lastInput).TotalSeconds < 90)
                _cardActiveSeconds += Math.Clamp((stamp - _lastTick).TotalSeconds, 0, 2);
            _lastTick = stamp;
            Header();
        };
        _timer.Start();
        ShowHome();
        if (!string.IsNullOrWhiteSpace(_store.LoadNotice)) Notify(_store.LoadNotice);
    }

    private static SolidColorBrush Brush(string hex)
    {
        hex = hex.ToUpperInvariant() switch {
            "#20372E" or "#254E3A" or "#244936" or "#243E30" => "#253039",
            "#697B70" or "#647767" or "#7C8B79" or "#7F8A80" or "#78876F" => "#78848D",
            "#DCE5DD" or "#D2DFD5" => "#E1E6EA",
            "#EFF3ED" or "#EDF2E7" or "#EEF3E4" or "#E7EDE4" => "#F1F5F6",
            "#E3EDBD" => "#DFEDEF",
            "#34714B" or "#3E714D" or "#40724D" or "#578062" or "#4B7043" => "#287C81",
            _ => hex
        };
        return new((Color)ColorConverter.ConvertFromString(hex));
    }
    private static TextBlock Txt(string text, double size = 14, string color = "#253039", bool bold = false) =>
        new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.55 };
    private static StackPanel Stack(params UIElement[] items)
    { var s = new StackPanel(); foreach (var i in items) s.Children.Add(i); return s; }
    private static T Space<T>(T item, double top = 12, double bottom = 0) where T : FrameworkElement
    { item.Margin = new Thickness(0, top, 0, bottom); return item; }
    private static Border Card(UIElement child, string color = "#FFFFFF", double pad = 20) =>
        new() { Background = Brush(color), BorderBrush = Brush("#DCE5DD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(pad), Child = child, Margin = new Thickness(0, 0, 0, 14) };
    private Button Btn(string label, Action action, string name = "", bool secondary = false)
    {
        var b = new Button { Content = label, Name = name, Margin = new Thickness(0, 0, 10, 0) };
        if (secondary) { b.Background = Brush("#EFF3ED"); b.Foreground = Brush("#254E3A"); }
        b.Click += (_, _) => Guard(action);
        return b;
    }
    private static WrapPanel Row(params UIElement[] items)
    { var panel = new WrapPanel(); foreach (var i in items) { if (i is FrameworkElement e) e.Margin = new Thickness(0, 0, 10, 8); panel.Children.Add(i); } return panel; }
    private static TextBox Input(string name, string value = "", bool multiline = false) =>
        new() { Name = name, Text = value, MinHeight = multiline ? 85 : 42, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private void Guard(Action action)
    { try { action(); } catch (Exception e) { Notify("操作未完成：" + e.Message); } }
    private bool Save()
    {
        try { var recovered = _saveFailure.Length > 0; _store.Save(_engine.State); _saveFailure = ""; if (recovered) Notify("本机保存已恢复，最新记录已写入。"); return true; }
        catch (Exception e) { _saveFailure = "本机保存失败，最新操作暂存在内存。请保持程序开启，在设置中导出备份：" + e.Message; Notify(_saveFailure); return false; }
    }
    private void Notify(string text)
    { NoticeText.Text = _saveFailure.Length > 0 ? _saveFailure : text; NoticeBox.Visibility = Visibility.Visible; }
    private void Header()
    {
        DateLabel.Text = _engine.Now.ToString("M月d日  dddd");
        PoolLabel.Text = $"生词 {_engine.State.Progress.Values.Count(p => p.InPool && !p.PermanentlySkipped)} 条 · 本机保存";
        UndoAction.IsEnabled = _engine.CanUndo;
        UndoCaption.Text = _page == "review" ? "回忆前隐藏资料 · 点错可撤销" : _engine.CanUndo ? "可撤销：" + _engine.UndoDescription : CurrentProfileName + " · 个人生词优先";
        if (_saveFailure.Length > 0) Notify(_saveFailure);
    }
    private void Page(string id, string title, string eyebrow)
    {
        _page = id; _primaryAction = null; _timedWord = ""; _cardActiveSeconds = 0;
        NoticeBox.Visibility = Visibility.Collapsed;
        _engine.RefreshPlan(); Header();
        PageTitle.Text = title; PageEyebrow.Text = eyebrow;
        PageBody.Children.Clear(); PageActions.Children.Clear(); ActionDock.Visibility = Visibility.Collapsed; PageScroll.ScrollToTop();
        foreach (var b in new[] { NavHome, NavWeek, NavPool, NavStory, NavProfiles, NavSettings })
        { b.Background = Brush("#FFFFFF"); b.Foreground = Brush("#63707A"); }
        var active = id switch { "home" or "learn" or "review" => NavHome, "week" => NavWeek, "pool" or "words" or "import" => NavPool, "story" => NavStory, "profiles" => NavProfiles, _ => NavSettings };
        active.Background = Brush("#EAF3F3"); active.Foreground = Brush("#287C81");
        if (id is "pool" or "words" or "import")
            PageBody.Children.Add(Row(Btn("查词与记录", () => ShowDictionary(), "LibraryDictionary", id != "pool"),
                Btn("全部词汇", ShowAllWords, "LibraryWords", id != "words"), Btn("导入不会词 TXT", ShowImport, "LibraryImport", id != "import")));
    }
    private void Actions(params Button[] buttons)
    {
        PageActions.Children.Clear(); ActionDock.Visibility = Visibility.Visible;
        foreach (var button in buttons) { button.Margin = new Thickness(0, 0, 10, 5); PageActions.Children.Add(button); }
    }
    private void TimeCard(VocabularyWord word, string kind)
    { _timedWord = word.Id; _timedKind = kind; _cardActiveSeconds = 0; _lastInput = DateTime.UtcNow; }
    private void RecordCardTime()
    {
        if (_timedWord.Length > 0 && _cardActiveSeconds >= 3)
            _engine.RecordDuration(_timedWord, _timedKind, _cardActiveSeconds);
        _timedWord = ""; _cardActiveSeconds = 0;
    }

    public void ShowHome()
    {
        Page("home", "今天", "一批明确的任务，按你的时间安排");
        _todaySession = false;
        var s = _engine.Summary(Today);
        var day = _engine.DayFor(Today);
        var queue = _engine.TodayQueue();
        var backlog = _engine.Backlog();
        var completed = day.Tasks.Count(t => t.State == "Done");
        var planned = day.Tasks.Count(t => t.State is "Done" or "Pending" or "Skipped" or "Deferred");
        var deferred = day.Tasks.Count(t => t.State == "Deferred");
        var main = Stack(Txt(queue.Count == 0 && day.Started ? deferred > 0 ? "今天的时间用完了，先休息。" : "今天这批，完成了。" : day.Started ? "继续今天的学习" : "准备开始今天的学习", 25, "#253039", true),
            Space(Txt($"新学 {queue.Count(x => x.Task.Kind == "New")} 条  ·  回忆 {queue.Count(x => x.Task.Kind == "Review")} 条  ·  补学 {queue.Count(x => x.Task.Kind == "Relearn")} 条", 15, "#63707A"), 12),
            Space(new ProgressBar { Height = 5, Maximum = Math.Max(1, planned), Value = day.Tasks.Count(t => t.State is "Done" or "Skipped"), Foreground = Brush("#287C81"), Background = Brush("#E8ECEF") }, 20),
            Space(Txt($"已完成 {completed} 项 · 本批预计 {s.EstimatedMinutes} 分钟 · 日预算 {day.MinutesBudget} 分钟", 12, "#78848D"), 8));
        if (queue.Count > 0) main.Children.Add(Space(Btn(day.Started ? "继续今天  →" : "开始今天  →", ShowLearn, "StartLearning"), 22));
        else main.Children.Add(Space(Btn("查看今天的记录", () => ShowWeek(Today), "TodayDetails", true), 22));
        if (deferred > 0) main.Children.Add(Space(Txt($"还有 {deferred} 项因时间预算延期，并未算作完成。", 13, "#8B674D"), 12));
        PageBody.Children.Add(Card(main, "#FFFFFF", 26));
        var columns = new UniformGrid { Columns = 3 };
        void Stat(string label, string value, string hint)
        { var card = Card(Stack(Txt(label, 12, "#78848D"), Space(Txt(value, 26, "#253039", true), 5), Space(Txt(hint, 11, "#78848D"), 5)), pad: 18); card.Margin = new Thickness(0, 0, 10, 14); columns.Children.Add(card); }
        Stat("今天新学", $"{s.NewDone} / {s.NewTarget}", $"原定 {Math.Max(0, day.OriginalNewTarget)} 条，调整有记录");
        Stat("待安排", backlog.Count.ToString(), "到期或个人待学，按容量逐日消化");
        var week = _engine.WeekFor(Today);
        Stat("本周新学", $"{week.Days.Values.Sum(d => _engine.Summary(d.Date).NewDone)} / {week.WeeklyGoal}", "原目标保留，未达成会如实显示");
        PageBody.Children.Add(columns);
        PageBody.Children.Add(Card(Stack(Txt("今天的安排依据", 16, "#253039", true),
            Space(Txt(day.TargetReason, 13, "#63707A"), 8),
            Space(Txt("到期先回忆；记得就结束，忘记才补学。新增词进入待学池，不会无限增加今天这批任务。", 13, "#63707A"), 6))));
        PageBody.Children.Add(Row(Btn("随时自测", ShowReviewHome, "StartReview", true), Btn("查词 / 记录生词", () => ShowDictionary(), secondary: true),
            Btn("主动加练 5 分钟", () => { var added = _engine.AppendToday(5); Save(); ShowLearn(); if (added.Count == 0) Notify("目前没有可追加的到期或补学任务；今日新词仍受设定上限约束。"); }, "AppendToday", true)));
        Save();
    }

    public void ShowLearn()
    { _todaySession = true; _engine.StartToday(); Save(); ShowTodayTask(); }
    private void ShowTodayTask()
    {
        var item = _engine.TodayQueue().FirstOrDefault();
        if (item is null) { ShowHome(); return; }
        if (item.Task.Kind == "Review")
        { _todaySession = true; _spellingMode = false; _reviewScope = "Due"; _reviewedThisRun.Clear(); _reviewWords = [item.Word]; ShowReviewCard(); }
        else ShowLearnWord(item.Word.Id);
    }
    private void ShowLearnWord(string? chosen, bool browsing = false)
    {
        var queue = _engine.TodayQueue();
        var item = chosen is null ? queue.FirstOrDefault() : queue.FirstOrDefault(x => x.Word.Id == chosen);
        if (item?.Task.Kind == "Review" && !browsing) { ShowTodayTask(); return; }
        if (item is null && chosen is not null && _engine.FindWord(chosen) is {} pastWord)
            item = new StudyItem(pastWord, new PlanTask { WordId = chosen, Kind = "New", Reason = "回看资料，不重复计入学习" });
        if (item is null) { ShowHome(); return; }
        Page("learn", browsing ? "回看词卡" : item.Task.Kind == "Relearn" ? "补学这个词" : "认识一个新词", "意思、搭配和语境，连在一起学");
        var w = item.Word; var p = _engine.GetProgress(w.Id);
        if (!browsing && (_lessonHistoryIndex < 0 || _lessonHistory[_lessonHistoryIndex] != w.Id))
        {
            if (_lessonHistoryIndex + 1 < _lessonHistory.Count) _lessonHistory.RemoveRange(_lessonHistoryIndex + 1, _lessonHistory.Count - _lessonHistoryIndex - 1);
            _lessonHistory.Add(w.Id); _lessonHistoryIndex = _lessonHistory.Count - 1;
        }
        var previous = Btn("← 上一个词", () => { _lessonHistoryIndex--; ShowLearnWord(_lessonHistory[_lessonHistoryIndex], true); }, "PreviousLesson", true);
        previous.IsEnabled = _lessonHistoryIndex > 0;
        PageBody.Children.Add(Row(previous, Txt(browsing ? "只回看，不改变记忆记录" : $"本批剩余 {queue.Count} 项 · {LearningEngine.KindLabel(item.Task.Kind)}", 12, "#78848D")));
        var note = Input("LessonNote", _lessonNotes.GetValueOrDefault(w.Id, p.Note), true); note.MinHeight = 65;
        note.TextChanged += (_, _) => _lessonNotes[w.Id] = note.Text;
        var body = Stack(Txt(w.Term, 40, "#253039", true), Space(Txt(Phonetic(w), 15, "#78848D"), 2),
            Space(Txt(Value(w.Meaning, "暂缺释义，可到词典补充"), 20, "#253039", true), 18));
        if (w.Phrases.Count > 0) body.Children.Add(Space(Txt(string.Join("\n", w.Phrases.Take(4)), 15, "#435760"), 16));
        if (!string.IsNullOrWhiteSpace(w.Example)) body.Children.Add(Space(Card(Txt(w.Example, 15, "#435760"), "#F5F7F9", 14), 16));
        var extra = Stack(Txt("我的记忆线索", 12, "#78848D"), Space(note, 5), Space(Txt(w.Coverage + " · " + w.Source, 11, "#78848D")),
            Space(Txt(w.RecommendationReason, 12, "#78848D")), Space(Txt(w.SourceExcerpt, 12, "#78848D")), Space(Txt(string.Join("\n", w.EvidenceSources), 11, "#78848D")));
        body.Children.Add(Space(new Expander { Header = "记笔记 / 查看完整资料与来源", Content = extra }, 12));
        PageBody.Children.Add(Card(body, pad: 26));
        var date = Today;
        void Finish()
        {
            if (Today != date) { ShowHome(); Notify("日期已切换，请开始新一天的任务。"); return; }
            _undoPage = "today"; _engine.CompleteLesson(w.Id, note.Text); RecordCardTime(); Save(); ShowTodayTask();
        }
        var learn = Btn(browsing ? "返回当前任务" : "学好了，继续  ↵", browsing ? ShowLearn : Finish, "CompleteLesson");
        var skip = Btn("已会，移出", () => { _undoPage = "today"; _engine.ExcludeWords([w.Id]); Save(); ShowTodayTask(); Notify("已移出。点错可撤销。"); }, "ExcludeCandidate", true);
        skip.IsEnabled = !p.PermanentlySkipped;
        Actions(learn, skip, Btn("查词", () => ShowDictionary(w.Term, w.Id), "LessonDictionary", true));
        _primaryAction = browsing ? ShowLearn : Finish;
        if (!browsing) TimeCard(w, item.Task.Kind);
    }

    public void ShowReviewHome()
    {
        Page("review", "随时自测", "额外练习不会把提前答对当作新的间隔成绩");
        _todaySession = false;
        var direction = new CheckBox { Name = "SpellingMode", Content = "拼写训练（单独记录，不清空识义进度）", IsChecked = _spellingMode, Margin = new Thickness(0, 12, 0, 0) };
        direction.Checked += (_, _) => _spellingMode = true; direction.Unchecked += (_, _) => _spellingMode = false;
        PageBody.Children.Add(Card(Stack(Txt("检查你想检查的词", 22, "#253039", true),
            Space(Txt("默认先在心里回忆意思，显示答案后自评。忘记时补学；拼写是独立可选训练。", 14, "#63707A")),
            Space(Row(Btn($"今天选定的回忆 · {_engine.TodayQueue().Count(x => x.Task.Kind == "Review")}", ShowLearn, "ReviewDue"),
                Btn($"个人生词自测 · {_engine.ReviewQueue("All").Count}", () => StartReview("All"), "ReviewAll", true),
                Btn("薄弱词", () => StartReview("Weak"), "ReviewWeak", true))), direction)));
        PageBody.Children.Add(Card(Stack(Txt("少量检查系统候选", 17, "#253039", true), Space(Txt("不是按出题频次强制背诵。已会可立即移出，不会才进入个人学习池。", 13, "#78848D")),
            Space(Btn("看 10 个候选", () => StartReview("Candidates"), "ReviewCandidates", true)))));
    }
    public void StartReview(string scope)
    { _todaySession = false; _reviewScope = scope; _reviewedThisRun.Clear(); _reviewWords = _engine.ReviewQueue(scope).ToList(); ShowReviewCard(); }
    private void ShowReviewCard()
    {
        _activeReview = _reviewWords.FirstOrDefault(w => !_reviewedThisRun.Contains(w.Id));
        if (_activeReview is null) { ShowReviewHome(); Notify($"本轮完成 {_reviewedThisRun.Count} 条，记忆记录已保存。"); return; }
        Page("review", _spellingMode ? "拼写训练" : "先回忆一下", _todaySession ? "今天的任务 · 答对即可继续" : "自主练习 · 释义自评，拼写独立记录");
        var w = _activeReview; var p = _engine.GetProgress(w.Id);
        var answer = Input("RecallAnswer"); answer.FontSize = 22;
        var prompt = _spellingMode ? Value(w.Meaning, "暂缺中文释义，请补充后练习").Replace(w.Term, "____", StringComparison.OrdinalIgnoreCase) : w.Term;
        var main = Stack(Txt(_todaySession ? $"今日剩余 {_engine.TodayQueue().Count} 项" : $"{_reviewedThisRun.Count + 1} / {_reviewWords.Count}", 12, "#78848D"),
            Space(Txt(prompt, _spellingMode ? 24 : 42, "#253039", true), 30),
            Space(Txt(_spellingMode ? "输入英文，再对照答案。" : "先在心里说出意思，想好后再显示答案。", 14, "#78848D"), 18));
        if (_spellingMode) main.Children.Add(Space(answer, 18));
        var detail = Stack(); detail.Visibility = Visibility.Collapsed; main.Children.Add(Space(detail, 24));
        PageBody.Children.Add(Card(main, pad: 28));
        var submitted = false; var date = Today;
        void Reveal(bool forgotten)
        {
            if (submitted) return;
            if (_spellingMode && !forgotten && string.IsNullOrWhiteSpace(answer.Text)) { Notify("输入拼写，或选择“没想起来”。"); return; }
            submitted = true; answer.IsReadOnly = true;
            var correct = !_spellingMode || answer.Text.Trim().Equals(w.Term, StringComparison.OrdinalIgnoreCase);
            detail.Visibility = Visibility.Visible;
            detail.Children.Add(Txt(_spellingMode ? w.Term + "  " + Phonetic(w) : Phonetic(w), _spellingMode ? 24 : 15, "#63707A"));
            detail.Children.Add(Space(Txt(w.Meaning, 20, "#253039", true), 10));
            if (w.Phrases.Count > 0) detail.Children.Add(Space(Txt(string.Join("\n", w.Phrases.Take(3)), 14, "#435760"), 12));
            if (w.Example.Length > 0) detail.Children.Add(Space(Txt(w.Example, 14, "#435760"), 12));
            if (_spellingMode) detail.Children.Add(Space(Txt(correct && !forgotten ? "拼写正确。" : "这次补练拼写，识义进度保留。", 13, "#287C81"), 12));
            void Grade(string rating)
            {
                if (Today != date) { ShowHome(); Notify("日期已切换，请开始新一天的任务。"); return; }
                _undoPage = _todaySession ? "today" : "review";
                var result = _engine.RecordRecall(w.Id, rating, forgotten, correct, _spellingMode ? "Spelling" : _reviewScope);
                RecordCardTime(); Save();
                if (_todaySession) ShowTodayTask();
                else { _reviewedThisRun.Add(w.Id); ShowReviewCard(); }
                Notify(result);
            }
            var good = Btn(_spellingMode ? "拼对了，继续  ↵" : "记得，继续  ↵", () => Grade("Good"), "RecallGood"); good.IsEnabled = !forgotten && correct;
            var again = Btn("忘记了", () => Grade("Again"), "RecallAgain", true);
            Actions(again, Btn("有点模糊", () => Grade("Hard"), "RecallHard", true), good);
            _primaryAction = good.IsEnabled ? () => Grade("Good") : () => Grade("Again");
        }
        Actions(Btn("显示答案  ↵", () => Reveal(false), "SubmitRecall"), Btn("没想起来", () => Reveal(true), "HintRecall", true));
        _primaryAction = () => Reveal(false);
        if (!_todaySession && p.NextReviewAt > _engine.Now && !_spellingMode)
            PageBody.Children.Add(Txt($"原复现安排 {p.NextReviewAt:M月d日}。提前答对保留原日期。", 12, "#78848D"));
        TimeCard(w, _spellingMode ? "Spelling" : "Review");
        if (_spellingMode) answer.Focus();
    }

    public void ShowWeek(DateOnly selected)
    {
        Page("week", "每周计划", "固定一周观察，任务按每天的容量确定");
        var week = _engine.WeekFor(selected); var d = _engine.DayFor(selected);
        PageBody.Children.Add(Row(Btn("← 上周", () => ShowWeek(week.Monday.AddDays(-7)), secondary: true),
            Txt($"{week.Monday:M月d日} — {week.Monday.AddDays(6):M月d日}", 20, "#253039", true),
            Btn("下周 →", () => ShowWeek(week.Monday.AddDays(7)), secondary: true), Btn("本周", () => ShowWeek(Today), secondary: true)));
        var actual = week.Days.Values.Sum(day => _engine.Summary(day.Date).NewDone);
        PageBody.Children.Add(Card(Stack(Txt($"原目标 {week.WeeklyGoal} 条 · 已新学 {actual} 条 · 还差 {Math.Max(0, week.WeeklyGoal - actual)} 条", 20, "#253039", true),
            Space(Txt("不把跳过熟词算作新学。每日任务完成与本周原目标分别记录，延期不会抹去。", 13, "#63707A"), 8)), pad: 20));
        if (week.Monday == LearningEngine.MondayOf(Today))
        {
            var remainingCap = Enumerable.Range(0, 7).Select(i => week.Monday.AddDays(i)).Where(date => date >= Today)
                .Sum(date => date.DayOfWeek == DayOfWeek.Sunday ? _engine.State.SundayNewLimit : _engine.State.DailyNewLimit);
            remainingCap = Math.Max(0, remainingCap - _engine.GetSession(Today).LearnedWordIds.Count);
            PageBody.Children.Add(Space(Txt($"按剩余日期与新词上限，本周最多再新学 {remainingCap} 条；复习较多时会更少。首次使用不足一周时，之前日期显示无记录，不补算漏学。", 12, "#63707A"), 0, 12));
        }
        var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 16) };
        var days = new[] { "一", "二", "三", "四", "五", "六", "日" };
        for (var i = 0; i < 7; i++)
        {
            var date = week.Monday.AddDays(i); var day = _engine.DayFor(date); var summary = _engine.Summary(date);
            var finished = day.Tasks.Count(t => t.State == "Done");
            var planned = day.Tasks.Count(t => t.State is "Done" or "Pending" or "Missed" or "Deferred");
            var futureDue = _engine.State.Progress.Values.Count(p => p.InPool && !p.PermanentlySkipped && p.NextReviewAt is {} due && DateOnly.FromDateTime(due.DateTime) == date);
            var label = date > Today ? (i == 6 && _engine.State.SundayNewLimit == 0 ? "复习 / 缓冲" : "当天定量") : !day.Started && planned == 0 ? "暂无记录" : summary.Pending == 0 && day.Tasks.All(t => t.State != "Missed") ? "本批完成" : date < Today ? "有未完成" : "进行中";
            var button = Btn("", () => ShowWeek(date), secondary: true);
            button.Padding = new Thickness(8, 13, 8, 13); button.Margin = new Thickness(0, 0, 6, 0);
            button.Background = Brush(date == selected ? "#DFEDEF" : "#FFFFFF");
            button.Content = Stack(Txt("周" + days[i], 11, "#78848D"), Txt(date.ToString("M/d"), 20, "#253039", true),
                Space(Txt(date > Today ? $"到期预测 {futureDue}" : $"任务 {finished}/{planned}", 11), 12),
                Txt(date > Today ? $"新词上限 {(i == 6 ? _engine.State.SundayNewLimit : _engine.State.DailyNewLimit)}" : $"新学 {summary.NewDone}/{summary.NewTarget}", 11),
                Space(Txt(label, 11, "#287C81", true), 10));
            grid.Children.Add(button);
        }
        PageBody.Children.Add(grid);
        var result = _engine.Summary(selected);
        PageBody.Children.Add(Card(Stack(Txt($"{selected:M月d日} · {(selected > Today ? "容量与预测" : "安排与完成记录")}", 18, "#253039", true),
            Space(Txt(selected > Today ? $"每日预算 {d.MinutesBudget} 分钟。未来只展示当前可知的到期预测，新词当天选择。" : $"原定新学 {Math.Max(0, d.OriginalNewTarget)} → 今日选定 {d.NewTarget} · 实际新学 {result.NewDone}\n本批预计 {result.EstimatedMinutes} 分钟 · 预算 {d.MinutesBudget} 分钟 · 主动加练 {d.AdditionalMinutes} 分钟", 13, "#63707A")),
            Space(Txt(d.TargetReason, 12, "#78848D"), 8))));
        if (selected <= Today) AddDayTasks(selected);
        if (selected == Today)
        {
            var backlog = _engine.Backlog();
            var list = Stack(Txt("这些词还在队列中，未算作今天已选定的任务。", 12, "#78848D"));
            foreach (var item in backlog.Take(80)) list.Children.Add(Space(Txt(item.Word.Term + " · " + LearningEngine.KindLabel(item.Task.Kind) + " · " + item.Task.Reason, 12), 6));
            if (backlog.Count > 80) list.Children.Add(Space(Txt($"另有 {backlog.Count - 80} 条，可在词库查看个人生词。", 12)));
            PageBody.Children.Add(Card(new Expander { Header = $"待安排队列 · {backlog.Count} 条", Content = list }));
        }
        if (selected <= Today)
        {
            var events = _engine.State.Events.Where(e => DateOnly.FromDateTime(e.At.DateTime) == selected).ToList();
            var timeline = Stack();
            foreach (var evt in events.TakeLast(80).Reverse())
                timeline.Children.Add(Space(Txt($"{evt.At:HH:mm}  {_engine.FindWord(evt.WordId)?.Term} · {(evt.Kind == "Lesson" ? LearningEngine.KindLabel(evt.Result) : evt.Result)}", 12, "#63707A"), 6));
            PageBody.Children.Add(Card(new Expander { Header = $"当天操作记录 · {events.Count} 次", Content = timeline }));
        }
        if (d.Changes.Count > 0) PageBody.Children.Add(Card(new Expander { Header = "任务调整记录", Content = Txt(string.Join("\n", d.Changes.Distinct()), 12, "#63707A") }));
        PageBody.Children.Add(Card(new Expander { Header = "每日词量与间隔的研究依据", Content = LearningBasis() }));
    }

    private void AddDayTasks(DateOnly date, int limit=int.MaxValue)
    {
        var day=_engine.DayFor(date);
        var tasks=day.Tasks.Where(t=>t.State!="Superseded")
            .Where(t=>date<=Today || t.Kind!="New"&&t.State=="Pending")
            .OrderBy(t=>t.State=="Pending"?0:t.State=="Done"?1:2).ToList();
        if(tasks.Count==0) { PageBody.Children.Add(Card(Txt(date > Today ? "此日尚无确定的到期词。新词留到当天按容量选择。" : "目前没有已确定的词条安排。可以查词添加自己的生词。",13,"#697B70")));return; }
        var panel=Stack();
        foreach(var t in tasks.Take(limit))
        {
            var w=_engine.FindWord(t.WordId); if(w is null)continue;
            var status=t.State switch{"Done"=>"✓ 完成","Skipped"=>"已会移出","Missed"=>"未完成·顺延","Deferred"=>"预算已满·延期","Replaced"=>"已调整",_=>"待完成"};
            var line=new Grid { Margin=new Thickness(0,7,0,7) };
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(145)});
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(110)});
            var left=Stack(Txt(w.Term,17,"#254E3A",true),Txt(LearningEngine.KindLabel(t.Kind),11,"#7C8B79"));
            var mid=Stack(Txt(t.Reason,12,"#697B70"),Txt(t.CompletedAt is {} at ? $"实际完成 {at:M/d HH:mm} · {t.Result}":t.Result,11,"#9A774F"));
            var right=Txt(status,12,t.State=="Done"?"#34714B":"#917244",true);
            line.Children.Add(left);Grid.SetColumn(mid,1);line.Children.Add(mid);Grid.SetColumn(right,2);line.Children.Add(right);
            panel.Children.Add(line);
        }
        if(tasks.Count>limit) panel.Children.Add(Txt($"另有 {tasks.Count-limit} 项，可在周计划查看全部。",12,"#697B70"));
        PageBody.Children.Add(Card(panel));
    }

    public void ShowPool() => ShowDictionary();

    public void ShowStory()
    {
        Page("story","故事实验室","让薄弱词长进一个有趣的场景");
        var styles=new ComboBox { ItemsSource=new[]{"荒诞搞笑","校园悬疑","科幻反转","温暖日常"},SelectedIndex=0 };
        var prompt=Input("StoryPrompt",_engine.BuildStoryPrompt("荒诞搞笑"),true);prompt.MinHeight=180;prompt.IsReadOnly=true;
        styles.SelectionChanged+=(_,_)=>prompt.Text=_engine.BuildStoryPrompt(styles.SelectedItem?.ToString()??"荒诞搞笑");
        var story=Input("StoryText","",true);story.MinHeight=180;
        PageBody.Children.Add(Card(Stack(Txt("用今天真正薄弱的词写故事",22,"#254E3A",true),
            Space(styles),Space(prompt),Space(Btn("复制到 ChatGPT",()=>{Clipboard.SetText(prompt.Text);Notify("已复制。生成后粘贴到下面保存。");},"CopyStoryPrompt")),
            Space(Txt("粘贴生成的短文与练习",16,"#254E3A",true),22),Space(story),
            Space(Btn("保存到本机",()=>{
                if(story.Text.Trim().Length<30){Notify("请先粘贴短文。");return;}
                _engine.ClearUndoHistory();
                _engine.State.Stories.Add(new SavedStory{Date=Today,Style=styles.SelectedItem?.ToString()??"",Text=story.Text.Trim(),
                    WordIds=_engine.State.Events.Where(e=>DateOnly.FromDateTime(e.At.DateTime)==Today).Select(e=>e.WordId).Where(id=>id.Length>0).Distinct().Take(6).ToList()});
                _engine.DayFor(Today).StoryCompleted=true;Save();ShowStory();Notify("已保存。故事阅读是补充，不会自动给单词增加掌握分。");
            },"SaveStory")))));
        foreach(var saved in _engine.State.Stories.TakeLast(10).Reverse())
            PageBody.Children.Add(Card(new Expander { Header=$"{saved.Date:M月d日} · {saved.Style}",Content=Txt(saved.Text,15) }));
    }


    public void ShowSettings()
    {
        Page("settings", "设置与备份", "调整个人容量，保留原目标与真实记录");
        var weekly = Input("WeeklyGoal", _engine.State.WeeklyNewGoal.ToString());
        var minutes = Input("DailyMinutes", _engine.State.DailyMinutes.ToString());
        var daily = Input("DailyNewLimit", _engine.State.DailyNewLimit.ToString());
        var sunday = Input("SundayNewLimit", _engine.State.SundayNewLimit.ToString());
        var form = new UniformGrid { Columns = 2 };
        void Field(string label, TextBox input) { var field = Stack(Txt(label, 12, "#63707A"), Space(input, 5)); field.Margin = new Thickness(0, 10, 18, 6); form.Children.Add(field); }
        Field("每天可用时间（5–90 分钟）", minutes); Field("周一至周六新词上限（0–30 条）", daily);
        Field("每周新学目标（0–200 条）", weekly); Field("周日新词上限（默认 0，留作复习）", sunday);
        PageBody.Children.Add(Card(Stack(Txt("每天的负担，由时间先决定", 20, "#253039", true), form,
            Space(Txt("上限不等于每天必须背这么多。到期复习与补学先占容量，记忆表现较弱时减少新词。20 分钟、5 条新词是起步设置，不是科学最优数。", 13, "#63707A")),
            Space(Btn("保存设置", () => {
                if (!int.TryParse(weekly.Text, out var w) || w < 0 || w > 200 || !int.TryParse(minutes.Text, out var m) || m < 5 || m > 90 || !int.TryParse(daily.Text, out var n) || n < 0 || n > 30 || !int.TryParse(sunday.Text, out var u) || u < 0 || u > 30) { Notify("请输入说明范围内的整数。"); return; }
                _engine.ClearUndoHistory(); _engine.State.WeeklyNewGoal = w; _engine.State.DailyMinutes = m;
                _engine.State.DailyNewLimit = n; _engine.State.SundayNewLimit = u;
                _engine.ReplanFuture(); Save(); Header();
                Notify("已保存。本周原目标和已经开始的任务保留，新的周目标从下周生效。" + (w > n * 6 + u ? $" 当前新词上限一周最多 {n * 6 + u} 条，低于目标；可调高上限或降低未来周目标。" : ""));
            }, "SaveSettings")))));
        PageBody.Children.Add(Card(Stack(Txt("资料与本地数据", 18, "#253039", true),
            Space(Txt($"当前学习资料 {_engine.Catalog.Count:N0} 条。词典仅用于查询，查过不等于加入学习。", 13, "#63707A")),
            Space(Row(Btn("导入不会词 TXT", ShowImport, "SettingsImport", true), Btn("导入 Word 资料", ImportDocx, "ImportDocx", true),
                Btn("导出本档案", Export, "ExportData", true), Btn("恢复本档案备份", Restore, "RestoreData", true))),
            Space(Txt("当前数据目录：" + _store.Folder, 11, "#78848D")))));
        PageBody.Children.Add(Card(new Expander { Header = "研究依据与算法边界", Content = LearningBasis() }));
    }

    private void ImportDocx()
    {
        var dialog=new OpenFileDialog { Filter="Word 词表|*.docx" };if(dialog.ShowDialog()!=true)return;
        var result=WordDocxImporter.Import(dialog.FileName,_engine.Catalog);
        if(result.Words.Count==0){Notify("没有识别到可靠词条，未修改词库。");return;}
        var preview=string.Join("\n",result.Words.Take(8).Select(w=>$"{w.Term} · {w.Meaning}"));
        if(MessageBox.Show($"识别到 {result.Words.Count} 条。请核对示例是否确实为词条：\n\n{preview}\n\n确认后合并，不会清空进度。","核对导入结果",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        _engine.ClearUndoHistory();
        _engine.State.ImportedWords=_engine.State.ImportedWords.Concat(result.Words).GroupBy(w=>w.Id,StringComparer.Ordinal).Select(g=>g.Last()).ToList();
        _engine=new LearningEngine(_store.LoadCatalog(),_engine.State,_clock);
        _engine.ReplanFuture();Save();ShowSettings();Notify($"已合并 {result.Words.Count} 条。请抽查原文，自动解析不能保证每种 Word 排版都正确。");
    }
    private void Export()
    {
        var d=new SaveFileDialog{Filter="JSON 备份|*.json",FileName=$"词间全部数据-{Today:yyyyMMdd}.json"};if(d.ShowDialog()!=true)return;
        _store.Export(_engine.State,d.FileName);Notify("已导出全部进度、个人词库、周计划、逐次记录和短文。");
    }
    private void Restore()
    {
        var d=new OpenFileDialog{Filter="JSON 备份|*.json"};if(d.ShowDialog()!=true)return;
        var restored=_store.ReadBackup(d.FileName);
        if(MessageBox.Show($"此备份有 {restored.Progress.Count} 条词汇进度，将替换当前数据。当前状态会先备份。","恢复数据",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        ApplyRestoredState(restored);
    }
    private void ApplyRestoredState(UserState restored)
    {
        _store.Export(_engine.State,Path.Combine(_store.Folder,$"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
        _engine=new LearningEngine(_store.LoadCatalog(),restored,_clock); ResetSessionUi(); _engine.RefreshPlan();Save();ShowHome();Notify("已恢复备份，旧版数据会保留并迁移。");
    }

    private void ResetSessionUi()
    {
        _reviewedThisRun.Clear(); _reviewWords.Clear(); _activeReview = null; _spellingMode = false;
        _lessonHistory.Clear(); _lessonHistoryIndex = -1; _todaySession = false; _lessonNotes.Clear();
        _dictionaryDrafts.Clear(); _dictionaryEntryId = ""; _dictionarySearchVersion++;
        _selectedWords.Clear(); _catalogFilter = ""; _catalogPage = 0; _catalogScope = "全部学习词汇";
        _dictionaryQuery = ""; _importText = ""; _importComplete = false; _importExtensions = false;
        _importSource = "TXT 生词清单"; _importGeneration++;
        _timedWord = ""; _cardActiveSeconds = 0; _primaryAction = null;
    }

    private static string Value(string text,string empty)=>string.IsNullOrWhiteSpace(text)?empty:text;
    private static string Phonetic(VocabularyWord w)=>string.IsNullOrWhiteSpace(w.Phonetic)?"音标暂无":"/"+w.Phonetic.Trim('/','[',']')+"/";
    private void Home_Click(object sender,RoutedEventArgs e)=>Guard(ShowHome);
    private void Learn_Click(object sender,RoutedEventArgs e)=>Guard(ShowLearn);
    private void Review_Click(object sender,RoutedEventArgs e)=>Guard(ShowReviewHome);
    private void Week_Click(object sender,RoutedEventArgs e)=>Guard(()=>ShowWeek(Today));
    private void Pool_Click(object sender,RoutedEventArgs e)=>Guard(ShowPool);
    private void Story_Click(object sender,RoutedEventArgs e)=>Guard(ShowStory);
    private void Profiles_Click(object sender,RoutedEventArgs e)=>Guard(ShowProfiles);
    private void Settings_Click(object sender,RoutedEventArgs e)=>Guard(ShowSettings);
}
