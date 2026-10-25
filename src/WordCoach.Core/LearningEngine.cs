using System.Text.Json;

namespace WordCoach.Core;

/// <summary>Local, explainable study/recall cycle. Intervals are conservative product defaults, not a fitted forgetting curve.</summary>
public sealed partial class LearningEngine
{
    private readonly List<VocabularyWord> _catalog;
    private readonly Dictionary<string, VocabularyWord> _byId;
    private readonly Dictionary<string, VocabularyWord> _byTerm;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<UndoEntry> _undo = [];
    private int _actionDepth;
    private sealed record UndoEntry(string Description, string WordId, DateOnly Date, string StateJson, string CatalogJson);
    private static readonly int[] Intervals = [1, 3, 7, 14, 30, 60, 90];
    public UserState State { get; }
    public IReadOnlyList<VocabularyWord> Catalog => _catalog;
    public DateTimeOffset Now => _clock();
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
    public bool CanUndo => _undo.Count > 0 && _undo[^1].Date == Today;
    public string UndoDescription => CanUndo ? _undo[^1].Description : "";
    public string LastUndoneWordId { get; private set; } = "";

    // Changes made directly by the UI (settings, imports, stories) are outside the
    // transactional learning history and must not be overwritten by an old snapshot.
    public void ClearUndoHistory()
    {
        _undo.Clear();
        LastUndoneWordId = "";
    }

    public LearningEngine(IEnumerable<VocabularyWord> catalog, UserState state, Func<DateTimeOffset>? clock = null)
    {
        State = state;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _catalog = state.SchoolWords.Concat(state.ImportedWords).Concat(catalog)
            .Select(PrepareEntry).GroupBy(w => w.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        _byId = _catalog.GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
        _byTerm = _catalog.OrderBy(w => w.EntryKind == "Word" ? 0 : 1).GroupBy(w => w.Term, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var reference in catalog)
        {
            if (!_byId.TryGetValue(reference.Id, out var merged)) continue;
            if (merged.ScopeId.Length == 0) merged.ScopeId = reference.ScopeId;
            if (reference.LemmaId.Length > 0) merged.LemmaId = reference.LemmaId;
            merged.Aliases = merged.Aliases.Concat(reference.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        Migrate();
    }

    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    private static string Key(DateOnly date) => date.ToString("yyyy-MM-dd");
    public VocabularyWord? FindWord(string id) => _byId.GetValueOrDefault(id);
    public WordProgress GetProgress(string id)
    {
        if (!State.Progress.TryGetValue(id, out var p)) State.Progress[id] = p = new WordProgress { WordId = id };
        return p;
    }
    public DailySession GetSession(DateOnly date)
    {
        if (!State.Sessions.TryGetValue(Key(date), out var s)) State.Sessions[Key(date)] = s = new DailySession { Date = date };
        return s;
    }

    public CandidateEvidence Evidence(VocabularyWord word)
    {
        var p = State.Progress.GetValueOrDefault(word.Id);
        if (p?.IsSchoolWord == true) return new(200, "school", $"你亲自记录的生词（{(p.SourceTag.Length > 0 ? p.SourceTag : "学校")}），优先学习");
        if (p?.InPool == true) return new(180 + p.Lapses, "personal", "你已确认生疏，优先学习");
        // Recommendation value and the learner's knowledge are separate facts. Neither raw
        // token frequency nor inverse frequency can establish that this learner needs a word.
        var band = word.EntryKind == "Phrase" ? "phrase" : word.EntryKind == "Sense" ? "sense" : "extension";
        var reason = word.RecommendationReason.Length > 0 ? word.RecommendationReason : "拓展候选，掌握情况待你确认";
        if (!word.RecommendationEligible) return new(0, "reference", "参考词条；不自动安排教材基础词");
        return new(50 + Math.Clamp(word.RecommendationPriority, 0, 99), band,
            reason + $"；{word.Coverage}（推荐价值不代表你不会）");
    }

    public IReadOnlyList<VocabularyWord> Candidates(int count = 10)
    {
        return _catalog.Where(w => w.RecommendationEligible)
            .Where(w => GetProgress(w.Id).Status == "New" && !GetProgress(w.Id).PermanentlySkipped && !GetProgress(w.Id).SelfReportedKnown)
            .Where(w => w.Term is not ("toillustrate" or "topersuade"))
            .Where(w => !w.IsFoundation || w.EntryKind is "Phrase" or "Sense")
            .OrderByDescending(w => Evidence(w).Score).ThenBy(w => w.Term, StringComparer.Ordinal)
            .Take(Math.Max(0, count)).ToList();
    }

    private static VocabularyWord PrepareEntry(VocabularyWord w)
    {
        w.Term = w.Term.Trim();
        if (w.LemmaId.Length == 0) w.LemmaId = w.Term.ToLowerInvariant();
        if (w.Id.Length == 0)
        {
            var identity = w.Term + "|" + w.EntryKind + (w.EntryKind == "Sense" ? "|" + w.Meaning : "");
            w.Id = "entry-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..20].ToLowerInvariant();
        }
        return w;
    }

    public bool BudgetReached => GetSession(Today).ActiveSeconds >= TotalBudgetSeconds(DayFor(Today));
    public int RemainingSeconds => Math.Max(0, TotalBudgetSeconds(DayFor(Today)) - GetSession(Today).ActiveSeconds);
    public WeekPlan WeekFor(DateOnly date)
    {
        var monday = MondayOf(date);
        if (State.Weeks.TryGetValue(Key(monday), out var week)) return week;
        week = new WeekPlan { Monday = monday, WeeklyGoal = State.WeeklyNewGoal, ReserveSlots = 0 };
        for (var i = 0; i < 7; i++)
        {
            var d = monday.AddDays(i);
            week.Days[Key(d)] = new DayPlan { Date = d, MinutesBudget = State.DailyMinutes,
                NewTarget = i == 6 ? State.SundayNewLimit : Math.Min(State.DailyNewLimit, (int)Math.Ceiling(State.WeeklyNewGoal / 6d)),
                ReservedMinutes = ReserveMinutes(State.DailyMinutes),
                TargetReason = i == 6 ? "周日缓冲，默认不加新词" : "预估容量；当天开始后固定任务" };
        }
        State.Weeks[Key(monday)] = week; return week;
    }
    public DayPlan DayFor(DateOnly date) => WeekFor(date).Days[Key(date)];
    public void RefreshPlan()
    {
        State.CurrentWeek = WeekFor(Today);
        foreach (var d in State.Weeks.Values.SelectMany(w => w.Days.Values).ToList())
        {
            if (d.Date < Today)
                foreach (var t in d.Tasks.Where(t => t.State is "Pending" or "Deferred"))
                { t.State = "Missed"; t.Result = "当日未完成，仍在待安排池；保留原记录"; }
            else if (d.Date > Today)
                foreach (var t in d.Tasks.Where(t => t.State == "Pending"))
                { t.State = "Replaced"; t.Result = "未来保留到期日期，具体任务当天选择"; }
        }
        var day = DayFor(Today);
        if (!day.Started) SelectTasks(day, false);
        var week = WeekFor(Today);
        var personal = Obligations().Where(x => x.Task.Kind == "New").Select(x => x.Word.Id).ToList();
        var room = Math.Max(0, week.WeeklyGoal - week.Days.Values.Sum(d => GetSession(d.Date).LearnedWordIds.Count));
        week.PriorityWordIds = personal.Take(room).ToList(); State.NextWeekPriority = personal.Skip(room).ToList();
        State.SchedulerVersion = 2;
    }
    private List<StudyItem> Obligations()
    {
        var result = new List<StudyItem>();
        foreach (var p in State.Progress.Values.Where(p => p.InPool && !p.PermanentlySkipped && !p.SelfReportedKnown).ToList())
        {
            var word = FindWord(p.WordId); if (word is null) continue;
            string? kind = !p.Introduced ? "New" : p.Status == "Relearn" ? "Relearn" :
                p.NextReviewAt is { } due && due <= Now ? "Review" : null;
            if (kind is not null) result.Add(new(word, new PlanTask { WordId = p.WordId, Kind = kind,
                DueAt = p.NextReviewAt, EstimateSeconds = EstimateSeconds(kind), Reason = p.LastReason }));
        }
        return result.OrderBy(x => x.Task.Kind == "Review" ? 0 : x.Task.Kind == "Relearn" ? 1 : 2)
            .ThenBy(x => x.Task.DueAt ?? DateTimeOffset.MinValue).ThenByDescending(x => GetProgress(x.Word.Id).IsSchoolWord)
            .ThenBy(x => GetProgress(x.Word.Id).AddedOn).ToList();
    }
    public IReadOnlyList<StudyItem> Backlog()
    {
        RefreshPlan();
        var selected = DayFor(Today).Tasks.Where(t => t.State == "Pending").Select(t => t.WordId).ToHashSet();
        return Obligations().Where(x => !selected.Contains(x.Word.Id)).ToList();
    }
    public IReadOnlyList<StudyItem> TodayQueue()
    {
        RefreshPlan();
        return DayFor(Today).Tasks.Where(t => t.State == "Pending" && FindWord(t.WordId) is not null &&
            !GetProgress(t.WordId).PermanentlySkipped).Select(t => new StudyItem(FindWord(t.WordId)!, t)).ToList();
    }
    public IReadOnlyList<StudyItem> StartToday()
    {
        RefreshPlan(); var day = DayFor(Today);
        if (!day.Started) { day.Started = true; day.Opened = true; day.StartedAt = Now; day.BatchCount = 1; }
        return TodayQueue();
    }
    public IReadOnlyList<StudyItem> AppendToday(int extraMinutes = 5) => RunAction("主动追加今日学习", "", () =>
    {
        StartToday(); var d = DayFor(Today); var extra = Math.Clamp(extraMinutes, 1, 30);
        d.AdditionalMinutes += extra; d.BatchCount++; d.Completed = false;
        d.Changes.Add($"主动追加 {extra} 分钟；本周原目标不变");
        SelectTasks(d, true); return TodayQueue();
    });
    public IReadOnlyList<StudyItem> StudyQueue() => TodayQueue().Where(x => x.Task.Kind is "New" or "Relearn").ToList();
    public IReadOnlyList<VocabularyWord> ReviewQueue(string scope)
    {
        RefreshPlan(); if (scope == "Candidates") return Candidates(10);
        return _catalog.Where(w => GetProgress(w.Id).InPool && !GetProgress(w.Id).PermanentlySkipped)
            .Where(w => scope != "Due" || GetProgress(w.Id).NextReviewAt <= Now)
            .Where(w => scope != "Weak" || GetProgress(w.Id).Status is "Relearn" or "NeedsLearning" || GetProgress(w.Id).Lapses > 0)
            .OrderBy(w => GetProgress(w.Id).NextReviewAt ?? DateTimeOffset.MinValue).ToList();
    }
    private void SelectTasks(DayPlan day, bool append)
    {
        var obligations = Obligations(); var done = GetSession(Today).LearnedWordIds.Count;
        var learnedWeek = WeekFor(Today).Days.Values.Sum(d => GetSession(d.Date).LearnedWordIds.Count);
        var remainingGoal = Math.Max(0, WeekFor(Today).WeeklyGoal - learnedWeek);
        var remainingDays = WeekFor(Today).Days.Values.Count(d => d.Date >= Today &&
            (d.Date.DayOfWeek == DayOfWeek.Sunday ? State.SundayNewLimit : State.DailyNewLimit) > 0);
        var fairShare = remainingDays > 0 ? (int)Math.Ceiling((remainingGoal + done) / (double)remainingDays) : 0;
        var normalLimit = Today.DayOfWeek == DayOfWeek.Sunday ? State.SundayNewLimit : State.DailyNewLimit;
        if (day.OriginalNewTarget < 0) day.OriginalNewTarget = Math.Min(normalLimit, fairShare);
        var maxNew = Math.Min(Today.DayOfWeek == DayOfWeek.Sunday ? State.SundayNewLimit : EffectiveNewLimit(), fairShare);
        if (append) maxNew = Math.Min(Today.DayOfWeek == DayOfWeek.Sunday ? State.SundayNewLimit : EffectiveNewLimit(),
            maxNew + Math.Max(1, day.AdditionalMinutes * 60 / EstimateSeconds("New")));
        maxNew = Math.Min(maxNew, remainingGoal + done);
        var existing = day.Tasks.Where(t => t.State == "Pending" || append && t.State == "Deferred").ToList();
        var reserved = day.Tasks.Where(t => t.State == "Done" || append && t.State == "Pending").Sum(t => t.EstimateSeconds);
        var completedEstimate = day.Tasks.Where(t => t.State == "Done").Sum(t => t.EstimateSeconds);
        var available = Math.Max(0, TotalBudgetSeconds(day) - ReserveMinutes(day.MinutesBudget) * 60 - reserved
            - Math.Max(0, GetSession(Today).ActiveSeconds - completedEstimate));
        var chosen = append ? existing.Where(t => t.State == "Pending").ToList() : new List<PlanTask>();
        var selectedIds = chosen.Select(t => t.WordId).ToHashSet();
        var newSlots = Math.Max(0, maxNew - done - chosen.Count(t => t.Kind == "New"));
        var duePool = obligations.Where(x => x.Task.Kind != "New").ToList();
        var items = duePool.Concat(obligations.Where(x => x.Task.Kind == "New")).Concat(Candidates(_catalog.Count).Select(w => new StudyItem(w,
            new PlanTask { WordId = w.Id, Kind = "New", EstimateSeconds = EstimateSeconds("New"), Reason = Evidence(w).Reason })));
        foreach (var item in items)
        {
            if (selectedIds.Contains(item.Word.Id)) continue;
            if (item.Task.Kind == "New" && (newSlots <= 0 || duePool.Any(x => !selectedIds.Contains(x.Word.Id)))) continue;
            if (item.Task.EstimateSeconds > available) continue;
            var task = existing.FirstOrDefault(t => t.WordId == item.Word.Id && t.Kind == item.Task.Kind) ?? item.Task;
            task.State = "Pending";
            chosen.Add(task); selectedIds.Add(item.Word.Id); available -= task.EstimateSeconds;
            if (task.Kind == "New") newSlots--;
        }
        if (!append) day.Tasks.RemoveAll(t => t.State == "Pending");
        foreach (var t in chosen) if (!day.Tasks.Contains(t)) day.Tasks.Add(t);
        day.NewTarget = done + chosen.Count(t => t.Kind == "New");
        day.NewWordIds = day.Tasks.Where(t => t.Kind == "New" && t.State is "Pending" or "Done").Select(t => t.WordId).Distinct().ToList();
        if (day.OriginalWordIds.Count == 0) day.OriginalWordIds = day.NewWordIds.ToList();
        day.TargetReason = duePool.Any(x => !selectedIds.Contains(x.Word.Id)) ? "到期任务超过今日容量，暂停新词；剩余保留为积压" :
            Today.DayOfWeek == DayOfWeek.Sunday ? "周日缓冲；新词上限可在设置调整" : "依据时间、新词上限和近七天回忆选定；开始后固定本批";
    }
    public int EffectiveNewLimit()
    {
        var baseline = Math.Clamp(State.DailyNewLimit, 0, 30);
        var delayed = State.Progress.Values.SelectMany(p => p.History)
            .Where(h => h.Target != "Spelling" && h.Date >= Today.AddDays(-6) && h.DelayDays >= 1).ToList();
        if (delayed.Count < 10) return baseline;
        var rate = delayed.Count(h => h.Rating == "Good" && !h.UsedHint) / (double)delayed.Count;
        if (rate < .6) return Math.Max(0, baseline - 2);
        if (rate < .8) return Math.Max(0, baseline - 1);
        return baseline; // Good performance restores the chosen cap; it never raises it automatically.
    }
    private static int ReserveMinutes(int minutes) => Math.Max(1, (int)Math.Ceiling(minutes * .2));
    private static int TotalBudgetSeconds(DayPlan day) => (day.MinutesBudget + day.AdditionalMinutes) * 60;
    public int EstimateSeconds(string kind)
    {
        var initial = kind == "New" ? 120 : kind == "Relearn" ? 90 : 45;
        var samples = State.Sessions.Values.SelectMany(s => s.DurationSamples).Where(s => s.Kind == kind && s.Seconds >= 3 && s.Seconds <= 300)
            .OrderByDescending(s => s.At).Take(40).Select(s => s.Seconds).Order().ToList();
        if (samples.Count < 5) return initial;
        var median = samples[samples.Count / 2];
        return (int)Math.Clamp(Math.Ceiling(samples.Count < 20 ? (initial + median) / 2 : median), kind == "Review" ? 15 : 30, 240);
    }
    // Duration is part of the preceding action's transaction, not a new undo step.
    public void RecordDuration(string wordId, string kind, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        var s = GetSession(Today); s.ActiveSeconds += (int)Math.Min(86400, Math.Ceiling(seconds));
        if (seconds <= 600)
            s.DurationSamples.Add(new DurationSample { WordId = wordId, Kind = kind, Seconds = Math.Clamp(seconds, 3, 300), At = Now });
        var day = DayFor(Today); var task = day.Tasks.LastOrDefault(t => t.WordId == wordId && t.State is "Pending" or "Done");
        if (task is not null) task.ActualSeconds += seconds;
        if (BudgetReached)
            foreach (var pending in day.Tasks.Where(t => t.State == "Pending"))
            { pending.State = "Deferred"; pending.Result = "实际用时已到预算，留待之后；可主动追加"; }
    }
    private IEnumerable<(DayPlan Day, PlanTask Task)> PendingTasks() =>
        State.Weeks.Values.SelectMany(w => w.Days.Values).SelectMany(d => d.Tasks.Where(t => t.State == "Pending").Select(t => (d, t)));
    private PlanTask AddTask(DayPlan day, string id, string kind, string reason, DateOnly? from = null)
    {
        var existing = day.Tasks.FirstOrDefault(t => t.WordId == id && t.Kind == kind && t.State == "Pending");
        if (existing is not null) return existing;
        var task = new PlanTask { WordId = id, Kind = kind, Reason = reason, CarriedFrom = from, EstimateSeconds = EstimateSeconds(kind) };
        day.Tasks.Add(task); return task;
    }
    public void CompleteLesson(string wordId, string note) => RunAction("完成学习", wordId, () => CompleteLessonCore(wordId, note));
    private void CompleteLessonCore(string wordId, string note)
    {
        StartToday(); var p = GetProgress(wordId); var day = DayFor(Today);
        var task = day.Tasks.FirstOrDefault(t => t.WordId == wordId && t.State == "Pending" && t.Kind is "New" or "Relearn");
        if (task is null) return;
        var kind = task.Kind; if (!p.InPool) Feedback(wordId, true);
        p.InPool = true; p.PermanentlySkipped = false; p.SelfReportedKnown = false; p.Introduced = true;
        p.Note = note.Trim(); p.LastLessonAt = Now; p.UpdatedAt = Now; p.AddedOn ??= Today;
        p.Status = "AwaitingReview"; p.RelearnDate = null;
        p.NextReviewAt = AtDate(Today.AddDays(1)); p.DueDate = Today.AddDays(1);
        p.LastReason = "已理解；默认次日独立回忆，提前练习不推迟原日期";
        task.State = "Done"; task.CompletedAt = Now; task.Result = "已完成讲解，次日回忆";
        var ids = kind == "New" ? GetSession(Today).LearnedWordIds : GetSession(Today).RelearnedWordIds;
        if (!ids.Contains(wordId)) ids.Add(wordId);
        Log(wordId, "Lesson", kind); RefreshPlan();
    }

    public void ExcludeCandidate(string id) => ExcludeWords([id]);

    public void ExcludeWords(IEnumerable<string> ids)
    {
        var selected = ids.Distinct().Where(id => FindWord(id) is not null && !GetProgress(id).PermanentlySkipped).ToList();
        if (selected.Count == 0) return;
        RunAction(selected.Count == 1 ? "移出已会词" : $"批量移出 {selected.Count} 个已会词", selected[0], () =>
        {
            RefreshPlan();
            foreach (var id in selected)
            {
                var p = GetProgress(id);
                if (!p.InPool) Feedback(id, false);
                p.Status = "Excluded"; p.PermanentlySkipped = true; p.InPool = false;
                p.NextReviewAt = null; p.DueDate = null; p.RelearnDate = null;
                p.LastReason = "你主动标记已会，停止安排；可撤销或在词典中重新加入";
                p.UpdatedAt = Now;
                foreach (var task in State.Weeks.Values.SelectMany(w => w.Days.Values).Where(d => d.Date >= Today)
                    .SelectMany(d => d.Tasks).Where(t => t.WordId == id && t.State is "Pending" or "Deferred").ToList())
                { task.State = "Skipped"; task.Result = "主动标记已会并移出；不计作新学或独立检验"; }
                Log(id, "Excluded", "主动移出，保留历史，不计入新学");
            }
            RefreshPlan();
        });
    }

    public string RecordRecall(string id, string rating, bool usedHint, bool spellingCorrect, string mode = "Pool") =>
        RunAction("复习评分", id, () => RecordRecallCore(id, rating, usedHint, spellingCorrect, mode));

    private string RecordRecallCore(string id, string rating, bool usedHint, bool spellingCorrect, string mode)
    {
        RefreshPlan(); var p = GetProgress(id);
        if (mode.Equals("Spelling", StringComparison.OrdinalIgnoreCase)) return RecordSpelling(p, rating, usedHint, spellingCorrect);
        var independent = !usedHint && rating == "Good";
        var candidate = !p.InPool && p.Status == "New";
        if (candidate && independent) { ExcludeCandidate(id); Log(id, "Recall", "候选自报已会", true); return "本条已会，已移出候选；不计新学。"; }
        if (candidate) Feedback(id, true);
        var wasInPool = p.InPool;
        var alreadyRelearning = p.Status == "Relearn";
        var last = p.History.LastOrDefault(h => h.Target != "Spelling" && h.At.HasValue)?.At ?? p.LastLessonAt;
        var delay = last.HasValue ? Math.Max(0, (Now - last.Value).TotalDays) : 0;
        var eligible = p.NextReviewAt is { } due && Now >= due && p.LastIntervalAdvance != Today;
        p.InPool = true; p.PermanentlySkipped = false; p.SelfReportedKnown = false; p.AddedOn ??= Today; p.UpdatedAt = Now;
        var advanced = false; string result;
        if (!independent)
        {
            p.Status = p.Introduced ? "Relearn" : "NeedsLearning"; p.Lapses++;
            if (!alreadyRelearning) p.MemoryStep = Math.Max(0, p.MemoryStep - (rating == "Hard" ? 1 : 2));
            p.IntervalDays = p.MemoryStep == 0 ? 0 : Intervals[p.MemoryStep - 1];
            p.NextReviewAt = null; p.DueDate = null; p.RelearnDate = Today;
            p.LastReason = "本次回忆模糊或依赖提示，针对本义补学；保留此前学习经历";
            var day = DayFor(Today);
            var task = day.Tasks.FirstOrDefault(t => t.WordId == id && t.State == "Pending" && t.Kind == "Review");
            if (task is not null)
            {
                var estimate = EstimateSeconds("Relearn");
                var committed = day.Tasks.Where(t => t.State is "Done" or "Pending").Sum(t => t.EstimateSeconds);
                if (committed - task.EstimateSeconds + estimate <= TotalBudgetSeconds(day))
                { task.Kind = "Relearn"; task.EstimateSeconds = estimate; task.Reason = p.LastReason; }
                else { task.State = "Done"; task.CompletedAt = Now; task.Result = "发现薄弱；补学留在待安排池"; }
            }
            result = "先看针对本义的讲解；需要补学的词保留在待学池，后续再回忆。";
        }
        else if (eligible || (!p.Introduced && wasInPool) || alreadyRelearning)
        {
            var recoveringEarly = alreadyRelearning && !eligible && p.Introduced;
            p.Introduced = true;
            if (!recoveringEarly) p.MemoryStep = Math.Min(Intervals.Length, p.MemoryStep + 1);
            p.IntervalDays = Intervals[Math.Max(0, p.MemoryStep - 1)]; p.NextReviewAt = AtDate(Today.AddDays(p.IntervalDays));
            p.DueDate = Today.AddDays(p.IntervalDays); p.LastIntervalAdvance = Today;
            p.Status = p.MemoryStep >= 5 ? "Stable" : "Scheduled"; p.RelearnDate = null;
            p.LastReason = $"独立回忆通过，{p.IntervalDays} 天后再回忆"; advanced = !recoveringEarly;
            foreach (var task in DayFor(Today).Tasks.Where(t => t.WordId == id && t.State == "Pending"))
            { task.State = task.Kind == "New" ? "Superseded" : "Done"; task.CompletedAt = Now; task.Result = "独立回忆通过，不必重读讲解"; }
            result = $"已完成这次回忆。{p.NextReviewAt:M月d日} 再出现，无需重读讲解。";
        }
        else result = "提前练习已记录；原回忆日期与间隔不后移。";
        p.History.Add(new ReviewRecord { Date = Today, At = Now, Rating = rating, UsedHint = usedHint, SpellingCorrect = true,
            NextIntervalDays = p.IntervalDays, IntervalAdvanced = advanced, Mode = mode, Target = "Meaning", DelayDays = delay });
        var session = GetSession(Today); if (!session.ReviewedWordIds.Contains(id)) session.ReviewedWordIds.Add(id);
        if (independent) session.IndependentCorrect++; else session.WeakCount++;
        Log(id, "Recall", independent ? "识义记得" : "识义需补学", independent, advanced); RefreshPlan(); return result;
    }
    private string RecordSpelling(WordProgress p, string rating, bool hint, bool correct)
    {
        var m = p.Spelling; var good = rating == "Good" && !hint && correct; var advanced = false;
        if (!good)
        { m.Lapses++; m.MemoryStep = Math.Max(0, m.MemoryStep - 1); m.IntervalDays = 1; m.NextReviewAt = AtDate(Today.AddDays(1)); }
        else if ((m.NextReviewAt is null || m.NextReviewAt <= Now) && m.LastIntervalAdvance != Today)
        { m.MemoryStep = Math.Min(Intervals.Length, m.MemoryStep + 1); m.IntervalDays = Intervals[m.MemoryStep - 1];
          m.NextReviewAt = AtDate(Today.AddDays(m.IntervalDays)); m.LastIntervalAdvance = Today; advanced = true; }
        p.History.Add(new ReviewRecord { Date = Today, At = Now, Rating = rating, UsedHint = hint, SpellingCorrect = correct,
            Target = "Spelling", Mode = "Spelling", IntervalAdvanced = advanced, NextIntervalDays = m.IntervalDays });
        Log(p.WordId, "Spelling", good ? "拼写正确" : "拼写需练习", good, advanced);
        return good ? "拼写练习已记录；识义进度不变。" : "已记录拼写薄弱；识义进度和日期保持不变。";
    }

    public VocabularyWord AddSchoolWord(string term, string meaning, string example)
        => AddPersonalWord(new VocabularyWord { Term = term, Meaning = meaning, Example = example }, "学校");

    public VocabularyWord AddPersonalWord(VocabularyWord word, string sourceTag)
        => AddPersonalWords([word], sourceTag)[0];

    public IReadOnlyList<VocabularyWord> AddPersonalWords(IEnumerable<VocabularyWord> words, string sourceTag)
    {
        var input = words.ToList();
        if (input.Any(w => w.Term.Trim().Length is 0 or > 120))
            throw new ArgumentException("请输入 1 至 120 字符的英文词或短语。");
        if (input.Count == 0) return [];
        return RunAction(input.Count == 1 ? "加入个人生词" : $"批量加入 {input.Count} 个个人词", input[0].Id, () =>
        {
            RefreshPlan();
            var added = new List<VocabularyWord>();
            foreach (var incoming in input)
            {
                var term = incoming.Term.Trim();
                var word = incoming.Id.Length > 0 ? FindWord(incoming.Id) : null;
                word ??= _catalog.FirstOrDefault(w => w.Term.Equals(term, StringComparison.Ordinal) && w.EntryKind == incoming.EntryKind &&
                    (incoming.EntryKind != "Sense" || incoming.Id.Length == 0 && w.Meaning == incoming.Meaning));
                if (word is null)
                {
                    word = JsonSerializer.Deserialize<VocabularyWord>(JsonSerializer.Serialize(incoming))!;
                    word.Term = term;
                    PrepareEntry(word);
                    _catalog.Add(word); _byId[word.Id] = word; _byTerm[word.Term] = word;
                }
                else
                {
                    if (incoming.Meaning.Trim().Length > 0) word.Meaning = incoming.Meaning.Trim();
                    if (incoming.Example.Trim().Length > 0) word.Example = incoming.Example.Trim();
                    if (incoming.Phonetic.Trim().Length > 0) word.Phonetic = incoming.Phonetic.Trim();
                    if (incoming.Phrases.Count > 0) word.Phrases = incoming.Phrases.Distinct().ToList();
                    if (incoming.SourceExcerpt.Length > 0) word.SourceExcerpt = incoming.SourceExcerpt;
                    if (incoming.Source.Length > 0) word.Source = incoming.Source;
                }
                State.SchoolWords.RemoveAll(w => w.Id == word.Id);
                State.SchoolWords.Add(word);
                var p = GetProgress(word.Id);
                var alreadyEnrolled = p.InPool && !p.PermanentlySkipped;
                var isFirstPersonal = !p.IsSchoolWord;
                p.IsSchoolWord = true;
                p.SelfReportedKnown = false;
                p.SourceTag = string.IsNullOrWhiteSpace(sourceTag) ? "个人" : sourceTag.Trim();
                if (!alreadyEnrolled)
                {
                    p.InPool = true; p.PermanentlySkipped = false;
                    p.Status = p.Introduced ? "Relearn" : "NeedsLearning";
                    p.RelearnDate = p.Introduced ? Today : null;
                    p.NextReviewAt = null; p.DueDate = null; p.AddedOn ??= Today;
                    p.LastReason = $"你从{p.SourceTag}记录的生词，优先安排学习";
                    p.UpdatedAt = Now;
                    FinishPending(word.Id, "个人重新标记", includeNew: true, markDone: false);
                }
                if (isFirstPersonal) GetSession(Today).SchoolWordsAdded++;
                Log(word.Id, "Personal", alreadyEnrolled ? "更新词条，保留记忆进度与日期" : "加入个人生词");
                if (!added.Any(w => w.Id == word.Id)) added.Add(word);
            }
            RefreshPlan();
            return (IReadOnlyList<VocabularyWord>)added;
        });
    }

    public void ReplanFuture()
    {
        State.DailyNewLimit = Math.Clamp(State.DailyNewLimit, 0, 30);
        State.SundayNewLimit = Math.Clamp(State.SundayNewLimit, 0, 30);
        foreach (var week in State.Weeks.Values.Where(w => w.Monday > MondayOf(Today))) week.WeeklyGoal = State.WeeklyNewGoal;
        foreach (var d in State.Weeks.Values.SelectMany(w => w.Days.Values).Where(d => d.Date > Today || d.Date == Today && !d.Started))
        { d.MinutesBudget = State.DailyMinutes; d.ReservedMinutes = ReserveMinutes(State.DailyMinutes); d.TargetReason = "尚未开始，按新设置预估"; }
        RefreshPlan();
    }

    public DaySummary Summary(DateOnly date)
    {
        var day = DayFor(date); var tasks = day.Tasks.Where(t => t.State is "Pending" or "Done" or "Missed" or "Deferred").ToList();
        int Total(string kind) => tasks.Count(t => t.Kind == kind);
        int Done(string kind) => tasks.Count(t => t.Kind == kind && t.State == "Done");
        var pending = tasks.Count(t => t.State is "Pending" or "Missed" or "Deferred");
        return new(GetSession(date).LearnedWordIds.Count, day.NewTarget, Done("Relearn"), Total("Relearn"), 0, 0,
            Done("Review"), Total("Review"), day.Tasks.Count(t => t.State == "Skipped"), pending,
            (int)Math.Ceiling(tasks.Sum(t => t.EstimateSeconds) / 60d), day.Started && pending == 0);
    }

    public void CloseDay(string feeling)
    {
        RefreshPlan();
        GetSession(Today).Feeling = feeling.Trim();
        var day = DayFor(Today);
        day.ClosedGoalMet = Summary(Today).GoalMet;
        day.CloseSummary = Report(Today);
        day.Completed = true;
        Log("", "CloseDay", day.ClosedGoalMet ? "达标" : "已收工，未达标");
    }

    public string Report(DateOnly date)
    {
        var s = Summary(date); var day = DayFor(date); var session = GetSession(date); var week = WeekFor(date);
        var learnedWeek = week.Days.Values.Sum(d => GetSession(d.Date).LearnedWordIds.Count);
        var recalls = State.Events.Where(e => DateOnly.FromDateTime(e.At.DateTime) == date && e.Kind == "Recall").ToList();
        var missed = day.Tasks.Where(t => t.State is "Missed" or "Deferred" or "Pending").Select(t => FindWord(t.WordId)?.Term);
        return $"{date:yyyy-MM-dd} 学习记录\n今日这批：{(s.GoalMet ? "完成" : "尚未完成")}\n" +
            $"新学 {s.NewDone}/{s.NewTarget}；补学 {s.RelearnDone}/{s.RelearnTotal}；回忆 {s.ReviewDone}/{s.ReviewTotal}\n" +
            $"本周原目标 {week.WeeklyGoal}，实际新学 {learnedWeek}，尚差 {Math.Max(0, week.WeeklyGoal - learnedWeek)}\n" +
            $"独立复习 {recalls.Count} 次，答对 {recalls.Count(r => r.Independent)} 次\n" +
            $"已记录用时 {session.ActiveSeconds / 60d:F1} 分钟；{day.TargetReason}\n" +
            $"新学词：{string.Join("、", session.LearnedWordIds.Select(id => FindWord(id)?.Term))}\n" +
            $"本批未完成：{string.Join("、", missed)}\n";
    }

    public string BuildStoryPrompt(string style)
    {
        var ids = State.Events.Where(e => DateOnly.FromDateTime(e.At.DateTime) == Today && e.Kind is "Lesson" or "Recall")
            .OrderByDescending(e => e.Kind == "Recall" && !e.Independent).Select(e => e.WordId)
            .Concat(State.Progress.Values.Where(p => p.InPool && p.Status == "Relearn").Select(p => p.WordId)).Distinct().Take(6);
        var words = ids.Select(FindWord).Where(w => w is not null).Cast<VocabularyWord>().ToList();
        if (words.Count == 0) return "先学习几个生词，或完成一次生词复习，再生成提示词。";
        return $"为中国高中生写 130—180 词的英语短文，风格：{style}，有情节和反转。自然用完并加粗下列目标词，其余用词尽量简单。\n" +
            string.Join("\n", words.Select(w => $"{w.Term}：{w.Meaning}；搭配：{string.Join("；", w.Phrases.Take(2))}")) +
            "\n随后给中文梗概、逐词用法说明、5 道填空题。答案集中放在最后，不紧挨题目。";
    }

    public static string KindLabel(string kind) => kind switch { "New" => "新词学习", "Relearn" => "错词重学", "Consolidate" => "到期回忆", "Review" => "到期回忆", _ => kind };
    public static string StatusLabel(WordProgress p) => p.SelfReportedKnown ? "自报已会·免推荐" : p.PermanentlySkipped ? "已会移出" : p.Status switch
    { "New" => "可能生词", "NeedsLearning" => "待学习", "Relearn" => "需重学", "AwaitingReview" => "已学待回忆", "Scheduled" => "记得·待复现", "Stable" => "稳定·仍会复现", _ => p.Status };

    private DateTimeOffset AtDate(DateOnly date) => new(date.ToDateTime(new TimeOnly(7, 0)), Now.Offset);
    private void ReopenToday()
    {
        var day = DayFor(Today);
        if (!day.Completed) return;
        day.Completed = false;
        day.Changes.Add("收工后继续操作，重新核对今天的达标状态");
    }
    private void Feedback(string id, bool unknown)
    {
        var word = FindWord(id)!;
        State.CandidateFeedback.RemoveAll(f => f.WordId == id);
        State.CandidateFeedback.Add(new CandidateFeedback { WordId = id, Band = Evidence(word).Band, WasUnknown = unknown });
    }
    private void FinishPending(string id, string result, bool includeNew, bool markDone = true)
    {
        foreach (var (day, task) in PendingTasks().Where(x => x.Task.WordId == id && (includeNew || x.Task.Kind != "New")).ToList())
        {
            task.State = markDone && day.Date <= Today ? "Done" : "Superseded";
            task.CompletedAt = task.State == "Done" ? Now : null; task.Result = result;
        }
    }
    private void Log(string id, string kind, string result, bool independent = false, bool advanced = false) =>
        State.Events.Add(new ActivityEvent { At = Now, WordId = id, Kind = kind, Result = result, Independent = independent, IntervalAdvanced = advanced, NextAt = State.Progress.GetValueOrDefault(id)?.NextReviewAt });

    private void RunAction(string description, string wordId, Action action) =>
        RunAction(description, wordId, () => { action(); return true; });

    private T RunAction<T>(string description, string wordId, Func<T> action)
    {
        if (_actionDepth > 0) return action();
        if (_undo.Count > 0 && _undo[^1].Date != Today) _undo.Clear();
        var snapshot = new UndoEntry(description, wordId, Today, JsonSerializer.Serialize(State), JsonSerializer.Serialize(_catalog));
        _actionDepth++;
        try
        {
            var result = action();
            if (snapshot.StateJson != JsonSerializer.Serialize(State))
            {
                _undo.Add(snapshot);
                if (_undo.Count > 30) _undo.RemoveAt(0);
            }
            return result;
        }
        catch
        {
            RestoreSnapshot(snapshot);
            throw;
        }
        finally { _actionDepth--; }
    }

    public bool UndoLastAction()
    {
        if (!CanUndo) return false;
        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreSnapshot(snapshot);
        LastUndoneWordId = snapshot.WordId;
        RefreshPlan();
        return true;
    }

    private void RestoreSnapshot(UndoEntry snapshot)
    {
        var restored = JsonSerializer.Deserialize<UserState>(snapshot.StateJson)!;
        // The UI and persistence service hold the UserState instance; replace its values,
        // rather than swapping the object and silently leaving them attached to stale data.
        foreach (var property in typeof(UserState).GetProperties().Where(p => p.CanWrite))
            property.SetValue(State, property.GetValue(restored));
        _catalog.Clear();
        _catalog.AddRange(JsonSerializer.Deserialize<List<VocabularyWord>>(snapshot.CatalogJson)!);
        _byId.Clear(); _byTerm.Clear();
        foreach (var word in _catalog)
        { _byId.TryAdd(word.Id, word); _byTerm.TryAdd(word.Term, word); }
        // Reconnect persisted custom entries to catalog objects so later edits are saved.
        State.SchoolWords = State.SchoolWords.Select(w => _byId.GetValueOrDefault(w.Id) ?? w).ToList();
        State.ImportedWords = State.ImportedWords.Select(w => _byId.GetValueOrDefault(w.Id) ?? w).ToList();
        State.CurrentWeek = State.Weeks.GetValueOrDefault(Key(MondayOf(Today)));
    }

    private void Migrate()
    {
        State.WeeklyNewGoal = Math.Clamp(State.WeeklyNewGoal, 0, 200); State.DailyMinutes = Math.Clamp(State.DailyMinutes, 5, 90);
        State.DailyNewLimit = Math.Clamp(State.DailyNewLimit, 0, 30); State.SundayNewLimit = Math.Clamp(State.SundayNewLimit, 0, 30);
        if (State.CurrentWeek is { } old) State.Weeks.TryAdd(Key(old.Monday), old);
        foreach (var p in State.Progress.Values)
        {
            p.Spelling ??= new();
            if (State.SchemaVersion < 3)
            {
                if (p.PermanentlySkipped) { p.Status = "Excluded"; continue; }
                p.InPool = p.Status != "New";
                if (p.InPool && p.Introduced) { p.Status = "Scheduled"; p.NextReviewAt = AtDate(p.DueDate ?? Today); }
                else if (p.InPool) p.Status = "NeedsLearning";
            }
            if (p.IsSchoolWord && p.SourceTag.Length == 0) p.SourceTag = "学校";
        }
        foreach (var d in State.Weeks.Values.SelectMany(w => w.Days.Values))
        {
            if (d.MinutesBudget <= 0) d.MinutesBudget = State.DailyMinutes;
            if (d.ReservedMinutes <= 0) d.ReservedMinutes = ReserveMinutes(d.MinutesBudget);
            if (d.OriginalNewTarget < 0) d.OriginalNewTarget = d.NewTarget;
            if (State.SchemaVersion < 4)
            {
                foreach (var t in d.Tasks.Where(t => t.Kind == "Consolidate" && t.State == "Pending")) t.Kind = "Review";
                if (d.Tasks.Any(t => t.State == "Done")) { d.Started = true; d.StartedAt ??= Now; d.BatchCount = Math.Max(1, d.BatchCount); }
                if (d.Date == Today && d.Started)
                {
                    var remaining = Math.Max(0, TotalBudgetSeconds(d) - Math.Max(GetSession(Today).ActiveSeconds,
                        d.Tasks.Where(t => t.State == "Done").Sum(t => t.EstimateSeconds)));
                    foreach (var task in d.Tasks.Where(t => t.State == "Pending"))
                    {
                        if (task.EstimateSeconds <= remaining) remaining -= task.EstimateSeconds;
                        else { task.State = "Deferred"; task.Result = "旧版任务超过总时间预算，保留未完成事实并延期"; }
                    }
                }
            }
        }
        State.SchemaVersion = 4; State.MigrationCompleted = true;
    }
}
