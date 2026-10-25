using System.Text.Json;
using WordCoach.App.Services;
using WordCoach.Core;

var checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS " + name); }
var now = new DateTimeOffset(2026, 9, 12, 19, 0, 0, TimeSpan.FromHours(8));
VocabularyWord W(string id, string term, string scope = VocabularyImport.CoreScopeId, string kind = "Word") =>
    new() { Id = id, Term = term, ScopeId = scope, EntryKind = kind, Meaning = "测试释义", RecommendationEligible = true };
List<VocabularyWord> Catalog() => [W("retain", "retain"), W("known", "known"), W("weak", "weak"),
    W("extend", "extend", VocabularyImport.ExtensionScopeId), W("charge-sense", "charge", VocabularyImport.ExtensionScopeId, "Sense"),
    W("reference", "reference", "reference-extension-2026-09"), W("China", "China"), W("china", "china"), W("US", "US"), W("us", "us"),
    W("AD", "AD"), new() { Id = "advertisement", Term = "advertisement", ScopeId = VocabularyImport.CoreScopeId, Aliases = ["ad"] }];
LearningEngine Engine() => new(Catalog(), new UserState { SchemaVersion = 4 }, () => now);
var engine = Engine();
engine.AddPersonalWord(W("weak", "weak"), "学校");
var weak = engine.GetProgress("weak"); weak.Introduced = true; weak.MemoryStep = 3; weak.IntervalDays = 7; weak.NextReviewAt = now.AddDays(7); weak.Status = "Scheduled";
var text = "retain\nretain\n" + string.Join("\n", Enumerable.Range(0, 300).Select(i => "newword" + Letters(i)));
var incremental = VocabularyImport.Preview(text, engine.Catalog, engine.State);
Check(incremental.Rows.Count == 301 && incremental.DuplicateCount == 1, "增量清单 300 个新词去重但不丢词");
var applied = engine.ApplyVocabularyImport(incremental);
Check(applied.UnknownCount == 301 && !engine.GetProgress("known").SelfReportedKnown && !engine.GetProgress("extend").SelfReportedKnown,
    "增量导入不推断未列出词已会");
var eventsBeforeRepeat = engine.State.Events.Count;
var repeat = VocabularyImport.Preview(text.ToUpperInvariant(), engine.Catalog, engine.State);
Check(repeat.AlreadyImported && engine.ApplyVocabularyImport(repeat).AlreadyImported && engine.State.Events.Count == eventsBeforeRepeat,
    "新建个人词获得 ID 后重复导入仍幂等");
Check(weak.MemoryStep == 3 && weak.NextReviewAt == now.AddDays(7), "导入保留已有间隔");
Check(engine.UndoLastAction() && engine.State.ImportBatches.Count == 0 && !engine.Catalog.Any(w => w.Term == "newworda"),
    "整批撤销恢复词库和导入记录");

engine = Engine();
engine.AddPersonalWord(W("weak", "weak"), "学校");
var complete = VocabularyImport.Preview("retain", engine.Catalog, engine.State, true, [VocabularyImport.CoreScopeId]);
Check(complete.PreservedProgressIds.Contains("weak") && !complete.KnownWordIds.Contains("weak"), "完整排查预览保护既有弱项");
engine.ApplyVocabularyImport(complete);
Check(engine.GetProgress("known").SelfReportedKnown && !engine.GetProgress("known").Introduced && engine.GetProgress("known").History.Count == 0,
    "自报已会不伪造测试或学习");
Check(!engine.GetProgress("extend").SelfReportedKnown && !engine.GetProgress("reference").SelfReportedKnown, "完整核心清单作用域严格限定");
Check(engine.GetProgress("weak").InPool && !engine.GetProgress("weak").SelfReportedKnown, "完整核心清单提交保留真实学习池");
var allScope = VocabularyImport.Preview("retain", engine.Catalog, engine.State, true, [VocabularyImport.CoreScopeId, VocabularyImport.ExtensionScopeId]);
engine.ApplyVocabularyImport(allScope);
Check(engine.GetProgress("extend").SelfReportedKnown && !engine.GetProgress("charge-sense").SelfReportedKnown, "精选扩展可排查，独立新义不因文本漏列推断已会");
Check(!engine.Candidates(100).Any(w => w.Id == "known"), "自报已会不再自动推荐");

var caseEngine = Engine();
var cases = VocabularyImport.Preview("China\nchina", caseEngine.Catalog, caseEngine.State);
Check(cases.Rows.Count == 2 && cases.Rows.Select(r => r.Word.Id).Distinct().Count() == 2, "China 和 china 分别匹配独立条目");
Check(VocabularyImport.Preview("CHINA", caseEngine.Catalog, caseEngine.State).InvalidLines.Count == 1, "模糊大小写不擅自猜测义项");
var us = VocabularyImport.Preview("US\nus\nAD\nad", caseEngine.Catalog, caseEngine.State);
Check(us.Rows.Count == 4 && us.Rows.Select(r => r.Word.Id).Distinct().Count() == 4 && us.Rows[^1].Word.Id == "advertisement",
    "US/us 及 AD/广告别名 ad 保留独立匹配");
var invalid = VocabularyImport.Preview("retain\n123\n这是中文", caseEngine.Catalog, caseEngine.State);
var invalidRejected = false;
try { caseEngine.ApplyVocabularyImport(invalid); } catch (ArgumentException) { invalidRejected = true; }
Check(invalidRejected && caseEngine.State.ImportBatches.Count == 0, "无效行阻止提交而非静默丢弃");
var changed = VocabularyImport.Preview("retain", caseEngine.Catalog, caseEngine.State, true, [VocabularyImport.CoreScopeId]);
caseEngine.FindWord("known")!.ScopeId = "changed";
var changedRejected = false;
try { caseEngine.ApplyVocabularyImport(changed); } catch (InvalidOperationException) { changedRejected = true; }
Check(changedRejected, "预览后词表范围变化要求重新核对");

var root = Path.GetFullPath(Path.Combine("qa-v05", "profile-import-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var profiles = new ProfileStore(root);
var alice = profiles.Create("甲同学"); var bob = profiles.Create("乙同学");
Check(profiles.FolderFor("default") == Path.Combine(root, "Data"), "默认档案保留既有 Data 路径");
using (var a = profiles.CreateDataStore(alice.Id))
using (var b = profiles.CreateDataStore(bob.Id))
{
    var sa = a.LoadState(); var sb = b.LoadState();
    sa.DailyMinutes = 17; sa.Progress["a-only"] = new WordProgress { WordId = "a-only", InPool = true }; a.Save(sa);
    sb.DailyMinutes = 29; b.Save(sb);
    Check(b.ReadBackup(Path.Combine(b.Folder, "learning-data.json")).Progress.Count == 0, "两个档案可同时持锁且数据隔离");
    var lockRejected = false;
    try { using var duplicateStore = profiles.CreateDataStore(alice.Id); duplicateStore.LoadState(); } catch (IOException) { lockRejected = true; }
    Check(lockRejected, "同一档案第二写入实例被独占锁阻止");
}
profiles.SetActive(bob.Id);
var reloaded = new ProfileStore(root);
Check(reloaded.Manifest.ActiveProfileId == bob.Id && reloaded.Manifest.Profiles.Count == 3, "重启后当前档案和目录可恢复");
using (var reopened = reloaded.CreateDataStore(alice.Id))
    Check(reopened.LoadState().DailyMinutes == 17, "释放锁后原档案进度仍可读取");
var unsafeId = false;
try { profiles.FolderFor("..\\escape"); } catch (InvalidDataException) { unsafeId = true; }
Check(unsafeId, "档案路径拒绝越界标识");

var migrationParent = Path.Combine(root, "migration");
var old4 = Path.Combine(migrationParent, "词间-课外拓展版", "Data", "learning-data.json");
var old3 = Path.Combine(migrationParent, "词间-循环学习版", "Data", "learning-data.json");
Directory.CreateDirectory(Path.GetDirectoryName(old4)!); Directory.CreateDirectory(Path.GetDirectoryName(old3)!);
File.WriteAllText(old4, JsonSerializer.Serialize(new UserState { SchemaVersion = 3, DailyMinutes = 23 }));
File.WriteAllText(old3, JsonSerializer.Serialize(new UserState { SchemaVersion = 3, DailyMinutes = 31 }));
File.SetLastWriteTimeUtc(old3, DateTime.UtcNow.AddHours(-2)); File.SetLastWriteTimeUtc(old4, DateTime.UtcNow.AddHours(-1));
var original = File.ReadAllBytes(old4);
using (var migrated = new DataStore(appRoot: Path.Combine(migrationParent, "新版")))
{
    Check(migrated.LoadState().DailyMinutes == 23, "新版优先复制最近更新的 0.4 记录");
    Check(File.Exists(Path.Combine(migrated.Folder, "旧版原始记录.json")) && File.ReadAllBytes(old4).SequenceEqual(original), "迁移保留原文件且留存完整原始副本");
    var future = Path.Combine(root, "future.json"); File.WriteAllText(future, JsonSerializer.Serialize(new UserState { SchemaVersion = 5 }));
    var futureRejected = false; try { migrated.ReadBackup(future); } catch (InvalidDataException) { futureRejected = true; }
    Check(futureRejected, "拒绝未来版本记录并保持当前数据");
}
Console.WriteLine($"Profile/import checks passed: {checks}");

static string Letters(int value)
{
    var result = "";
    do { result = (char)('a' + value % 26) + result; value = value / 26 - 1; } while (value >= 0);
    return result;
}
