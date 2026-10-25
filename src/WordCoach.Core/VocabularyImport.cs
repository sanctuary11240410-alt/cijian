using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WordCoach.Core;

public sealed partial class WordProgress
{
    public bool SelfReportedKnown { get; set; }
    public string ImportBatchId { get; set; } = "";
}

public sealed partial class UserState
{
    public List<VocabularyImportBatch> ImportBatches { get; set; } = [];
}

public sealed class VocabularyImportBatch
{
    public string Id { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Source { get; set; } = "";
    public bool CompleteScreen { get; set; }
    public List<string> ScopeIds { get; set; } = [];
    public int UnknownCount { get; set; }
    public int SelfReportedKnownCount { get; set; }
    public int PreservedProgressCount { get; set; }
}

public sealed record VocabularyImportRow(string Input, VocabularyWord Word, string Match, bool Unmatched);

public sealed class VocabularyImportPreview
{
    public string BatchId { get; init; } = "";
    public string Source { get; init; } = "TXT 生词清单";
    public bool CompleteScreen { get; init; }
    public List<string> ScopeIds { get; init; } = [];
    public List<string> ScopedWordIds { get; init; } = [];
    public List<VocabularyImportRow> Rows { get; init; } = [];
    public List<string> KnownWordIds { get; init; } = [];
    public List<string> PreservedProgressIds { get; init; } = [];
    public List<string> PreservedSenseIds { get; init; } = [];
    public List<string> InvalidLines { get; init; } = [];
    public int DuplicateCount { get; init; }
    public bool AlreadyImported { get; init; }
    public int ScopeCount => ScopedWordIds.Count;
    public IReadOnlyList<VocabularyWord> UnknownWords => Rows.Select(r => r.Word).ToList();
}

public sealed record VocabularyImportResult(int UnknownCount, int SelfReportedKnownCount, int PreservedProgressCount, bool AlreadyImported);

public static class VocabularyImport
{
    public const string CoreScopeId = "curriculum-2020-3000";
    public const string ExtensionScopeId = "curated-extension-2026-09";

    public static string Normalize(string input) => Regex.Replace(input.Trim().Trim('\uFEFF')
        .Replace('’', '\'').Replace('‘', '\'').Replace('–', '-').Replace('—', '-'), @"\s+", " ").ToLowerInvariant();

    public static bool HasLearningEvidence(WordProgress progress) => progress.InPool || progress.Introduced ||
        progress.History.Count > 0 || progress.LastLessonAt.HasValue || progress.NextReviewAt.HasValue || progress.Lapses > 0;

    public static VocabularyImportPreview Preview(string text, IReadOnlyList<VocabularyWord> catalog, UserState state,
        bool completeScreen = false, IEnumerable<string>? scopeIds = null, string source = "TXT 生词清单",
        Func<string, VocabularyWord?>? lookup = null)
    {
        if (text.Length > 1_000_000) throw new ArgumentException("清单过大，请每次使用小于 1 MB 的文本。");
        var scopes = (scopeIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Order().ToList();
        if (completeScreen && scopes.Count == 0) throw new ArgumentException("完整排查必须明确覆盖的词表范围。");
        var byTerm = catalog.SelectMany(w => w.Aliases.Prepend(w.Term).Select(term => (Term: Normalize(term), Word: w)))
            .GroupBy(x => x.Term).ToDictionary(g => g.Key, g => g.Select(x => x.Word).DistinctBy(w => w.Id).ToList());
        var lines = text.Split(['\r', '\n', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 10000) throw new ArgumentException("每批最多 10,000 条，请分批导入。");
        var rows = new List<VocabularyImportRow>();
        var invalid = new List<string>();
        var seenEntries = new HashSet<string>();
        var duplicateCount = 0;
        foreach (var line in lines)
        {
            var term = Normalize(line);
            if (term.Length is 0 or > 120 || !Regex.IsMatch(term, @"^[a-z][a-z '\-]*[a-z]$|^[a-z]$"))
            { invalid.Add(line); continue; }
            VocabularyWord? word = null;
            var match = "现有词条";
            if (byTerm.TryGetValue(term, out var entries))
            {
                if (entries.Select(w => w.Term).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    var originalInput = line.Trim().Trim('\uFEFF');
                    var exact = entries.Where(w => w.Term.Equals(originalInput, StringComparison.Ordinal) || w.Aliases.Contains(originalInput, StringComparer.Ordinal)).ToList();
                    if (exact.Count == 0 || exact.Select(w => w.Term).Distinct(StringComparer.Ordinal).Count() > 1)
                    { invalid.Add(line + "（大小写区分义项，请写为 " + string.Join(" / ", entries.Select(w => w.Term).Distinct()) + "）"); continue; }
                    entries = exact;
                    match = "按明确大小写匹配独立义项";
                }
                word = entries.FirstOrDefault(w => w.EntryKind == "Word") ?? entries.FirstOrDefault(w => w.EntryKind == "Phrase") ?? (entries.Count == 1 ? entries[0] : null);
                if (word is not null && Normalize(word.Term) != term) match = $"课标明确别名 / 词形：{line.Trim()} → {word.Term}";
            }
            var unmatched = word is null;
            if (word is null && lookup is not null)
            {
                word = lookup(term);
                if (word is not null)
                {
                    var resolved = Normalize(word.Term);
                    match = resolved == term ? "词典匹配 · 不在所选词表" : $"词典明确词形映射：{term} → {resolved}";
                    if (byTerm.TryGetValue(resolved, out var mapped))
                    {
                        var exactMapped = mapped.Where(w => w.Term == word.Term || w.Aliases.Contains(word.Term, StringComparer.Ordinal)).ToList();
                        if (exactMapped.Count > 0 && exactMapped.Select(w => w.Term).Distinct().Count() == 1)
                        { word = exactMapped.FirstOrDefault(w => w.EntryKind == "Word") ?? exactMapped.First(); unmatched = false; }
                    }
                }
            }
            if (word is null)
            {
                word = new VocabularyWord { Term = term, Source = "个人 TXT 输入（待补充释义）", EntryKind = term.Contains(' ') ? "Phrase" : "Word" };
                match = "未匹配 · 原词保留，释义待补充";
            }
            var identity = word.Id.Length > 0 ? "id:" + word.Id : "term:" + Normalize(word.Term) + ":" + word.EntryKind;
            if (!seenEntries.Add(identity)) { duplicateCount++; continue; }
            rows.Add(new VocabularyImportRow(line.Trim().Trim('\uFEFF'), word, match, unmatched));
        }
        var scoped = completeScreen ? catalog.Where(w => scopes.Contains(w.ScopeId)).Select(w => w.Id).Distinct().Order().ToList() : [];
        if (completeScreen && scoped.Count == 0) throw new ArgumentException("本机没有这个范围的已核验条目，无法把清单未列出的词标为已会。");
        var listed = rows.Select(r => r.Word.Id).ToHashSet();
        var known = new List<string>(); var preserved = new List<string>(); var senses = new List<string>();
        foreach (var word in catalog.Where(w => scoped.Contains(w.Id) && !listed.Contains(w.Id)))
        {
            if (state.Progress.TryGetValue(word.Id, out var p) && HasLearningEvidence(p)) preserved.Add(word.Id);
            else if (word.EntryKind == "Sense") senses.Add(word.Id);
            else known.Add(word.Id);
        }
        var signature = (completeScreen ? "complete" : "incremental") + "\n" + string.Join("\n", scopes) + "\n" +
            string.Join("\n", scoped) + "\n" + string.Join("\n", rows.Select(r => r.Word.Term + ":" + r.Word.EntryKind).Order());
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))).ToLowerInvariant();
        return new VocabularyImportPreview { BatchId = id, Source = source, CompleteScreen = completeScreen, ScopeIds = scopes,
            ScopedWordIds = scoped, Rows = rows, KnownWordIds = known, PreservedProgressIds = preserved, PreservedSenseIds = senses,
            InvalidLines = invalid, DuplicateCount = duplicateCount, AlreadyImported = state.ImportBatches.Any(b => b.Id == id) };
    }
}

public sealed partial class LearningEngine
{
    public VocabularyImportResult ApplyVocabularyImport(VocabularyImportPreview preview)
    {
        if (preview.InvalidLines.Count > 0) throw new ArgumentException("清单包含无法识别的行，请修改后重新预览。");
        if (State.ImportBatches.Any(b => b.Id == preview.BatchId))
            return new VocabularyImportResult(0, 0, 0, true);
        if (preview.Rows.Count == 0) throw new ArgumentException("请至少列出一个不会的词；空清单不会用来批量改变词表状态。");
        var currentScope = _catalog.Where(w => preview.ScopeIds.Contains(w.ScopeId)).Select(w => w.Id).Distinct().Order().ToList();
        if (preview.CompleteScreen && !currentScope.SequenceEqual(preview.ScopedWordIds.Order()))
            throw new InvalidOperationException("词表范围发生变化，请重新预览后导入。");
        return RunAction("导入生词清单", "", () =>
        {
            var added = AddPersonalWords(preview.UnknownWords, preview.Source);
            foreach (var word in added)
            { var p = GetProgress(word.Id); p.SelfReportedKnown = false; p.ImportBatchId = preview.BatchId; }
            var knownCount = 0; var protectedCount = preview.PreservedProgressIds.Count;
            foreach (var id in preview.KnownWordIds)
            {
                var word = FindWord(id);
                if (!preview.CompleteScreen || word is null || !preview.ScopeIds.Contains(word.ScopeId) || word.EntryKind == "Sense") continue;
                var p = GetProgress(id);
                if (VocabularyImport.HasLearningEvidence(p)) { protectedCount++; continue; }
                p.SelfReportedKnown = true;
                p.ImportBatchId = preview.BatchId;
                p.LastReason = "完整清单未列出：自报已会，免推荐；未计作独立测试或学习完成";
                p.UpdatedAt = Now;
                foreach (var (_, task) in PendingTasks().Where(x => x.Task.WordId == id && x.Task.Kind == "New").ToList())
                { task.State = "Skipped"; task.Result = "完整排查清单：自报已会，未计入学习或测试成绩"; }
                knownCount++;
            }
            State.ImportBatches.Add(new VocabularyImportBatch { Id = preview.BatchId, At = Now, Source = preview.Source,
                CompleteScreen = preview.CompleteScreen, ScopeIds = preview.ScopeIds.ToList(), UnknownCount = added.Count,
                SelfReportedKnownCount = knownCount, PreservedProgressCount = protectedCount });
            State.Events.Add(new ActivityEvent { At = Now, Kind = "Import", Result = $"导入 {added.Count} 个生词；{knownCount} 个自报已会；保留 {protectedCount} 个既有进度" });
            RefreshPlan();
            return new VocabularyImportResult(added.Count, knownCount, protectedCount, false);
        });
    }
}
