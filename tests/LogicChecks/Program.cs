using System.Text.Json;
using WordCoach.Core;

var monday = new DateTimeOffset(2026, 9, 7, 18, 0, 0, TimeSpan.FromHours(8));
var words = Enumerable.Range(1, 150).Select(i => new VocabularyWord {
    Id=$"w{i}",Term=$"lexeme{i}",Meaning="释义",RecommendationEligible=true,
    RecommendationPriority=150-i,ScopeId="test-core",LemmaId=$"lemma{i}"
}).ToList();
int failures=0,checks=0;
void Check(string name,Action test) { checks++;try{test();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex.Message);} }
void Assert(bool condition,string message) { if(!condition)throw new Exception(message); }
LearningEngine Create(Func<DateTimeOffset> clock,int goal=30,int minutes=20) =>
    new(words,new UserState{SchemaVersion=4,WeeklyNewGoal=goal,DailyMinutes=minutes},clock);
void Due(LearningEngine e,string id,int step=2) {
    var p=e.GetProgress(id);p.InPool=true;p.Introduced=true;p.Status="Scheduled";p.MemoryStep=step;
    p.IntervalDays=step==0?0:3;p.NextReviewAt=monday.AddDays(-1);p.LastLessonAt=monday.AddDays(-7);
}
string QueueIds(LearningEngine e)=>string.Join(",",e.TodayQueue().Select(x=>x.Task.Id));

Check("新档案初值为20分钟5新词周目标30，周日零新词",()=>{
    var s=new UserState();Assert(s.DailyMinutes==20&&s.DailyNewLimit==5&&s.WeeklyNewGoal==30&&s.SundayNewLimit==0,"默认值不一致");
});
Check("未开始仅预览，连续刷新任务身份稳定",()=>{
    var e=Create(()=>monday);var ids=QueueIds(e);
    Assert(e.TodayQueue().Count==5&&!e.DayFor(e.Today).Started,"预览数量或状态错误");
    for(int i=0;i<10;i++)e.RefreshPlan();
    Assert(QueueIds(e)==ids,"刷新改变任务身份");
});
Check("开始后加入50词不改当前卡和已选任务",()=>{
    var e=Create(()=>monday);e.StartToday();var ids=QueueIds(e);
    e.AddPersonalWords(Enumerable.Range(1,50).Select(i=>new VocabularyWord{Term="personal"+i,Meaning="个人词"}),"学校");
    Assert(QueueIds(e)==ids&&e.Backlog().Count==50,"新增改变了固定批次或丢积压");
});
Check("未开始时个人词优先",()=>{
    var e=Create(()=>monday);e.RefreshPlan();var w=e.AddSchoolWord("priority","优先","");
    Assert(e.TodayQueue()[0].Word.Id==w.Id,"个人词未优先");
});
Check("新词先讲解，默认次日回忆而非强制10分钟",()=>{
    var now=monday;var e=Create(()=>now);var w=e.StartToday()[0].Word;
    e.CompleteLesson(w.Id,"我的笔记");var p=e.GetProgress(w.Id);
    Assert(p.MemoryStep==0&&p.NextReviewAt?.Date==monday.AddDays(1).Date&&p.Note=="我的笔记","首学直接算掌握或日期错误");
    now=now.AddMinutes(11);Assert(e.ReviewQueue("Due").Count==0,"仍强制10分钟回忆");
});
Check("到期先回忆，答对结束不重复讲解",()=>{
    var e=Create(()=>monday);Due(e,"w1");var q=e.StartToday();
    Assert(q[0].Task.Kind=="Review"&&e.StudyQueue().All(x=>x.Word.Id!="w1"),"到期词先看讲解");
    e.RecordRecall("w1","Good",false,true);
    Assert(e.GetProgress("w1").MemoryStep==3&&e.TodayQueue().All(x=>x.Word.Id!="w1"),"答对没有结束");
    Assert(e.GetSession(e.Today).LearnedWordIds.Count==0,"复习算新学");
});
Check("提前反复答对不后移原日期",()=>{
    var now=monday;var e=Create(()=>now);Due(e,"w1");e.StartToday();e.RecordRecall("w1","Good",false,true);
    var due=e.GetProgress("w1").NextReviewAt;for(int i=0;i<8;i++)e.RecordRecall("w1","Good",false,true);
    Assert(e.GetProgress("w1").NextReviewAt==due&&e.GetProgress("w1").MemoryStep==3,"即时重复刷高间隔");
});
Check("成熟词答错只温和缩短，原任务转补学",()=>{
    var e=Create(()=>monday);Due(e,"w1",6);e.StartToday();var id=e.TodayQueue()[0].Task.Id;
    e.RecordRecall("w1","Again",false,true);
    Assert(e.GetProgress("w1").MemoryStep==4&&e.TodayQueue()[0].Task.Id==id&&e.TodayQueue()[0].Task.Kind=="Relearn","错误清空进度或新建额外任务");
    e.CompleteLesson("w1","针对性说明");
    Assert(e.GetProgress("w1").MemoryStep==4&&e.GetProgress("w1").NextReviewAt?.Date==monday.AddDays(1).Date,"补学重置既有间隔");
});
Check("重复答错不冒充完成补学",()=>{
    var e=Create(()=>monday);Due(e,"w1");e.StartToday();
    e.RecordRecall("w1","Again",false,true);e.RecordRecall("w1","Again",false,true);
    Assert(e.Summary(e.Today).RelearnDone==0&&e.Summary(e.Today).RelearnTotal==1,"重复失败制造完成");
});
Check("提示后自评记得仍标识义薄弱",()=>{
    var e=Create(()=>monday);Due(e,"w1");e.StartToday();e.RecordRecall("w1","Good",true,true);
    Assert(e.GetProgress("w1").Status=="Relearn","提示算独立通过");
});
Check("识义与拼写独立，拼错不清空识义日期",()=>{
    var e=Create(()=>monday);Due(e,"w1",5);var due=e.GetProgress("w1").NextReviewAt;
    e.RecordRecall("w1","Again",false,false,"Spelling");
    Assert(e.GetProgress("w1").MemoryStep==5&&e.GetProgress("w1").NextReviewAt==due&&e.GetProgress("w1").Spelling.Lapses==1,"拼写污染识义");
    Assert(e.GetSession(e.Today).WeakCount==0,"拼写污染识义统计");
});
Check("识义模式不会把拼写标志当理解失败",()=>{
    var e=Create(()=>monday);Due(e,"w1");e.StartToday();e.RecordRecall("w1","Good",false,false,"Pool");
    Assert(e.GetProgress("w1").Status=="Scheduled","识义仍绑定拼写");
});
Check("100到期词共同受20分钟预算，其余明确积压",()=>{
    var e=Create(()=>monday);foreach(var w in words.Take(100))Due(e,w.Id);
    var q=e.StartToday();Assert(q.All(x=>x.Task.Kind=="Review"),"积压时加了新词");
    Assert(q.Sum(x=>x.Task.EstimateSeconds)<=1200&&q.Count<100,"预算无效");
    Assert(e.Backlog().Count+q.Count==100,"积压丢失");
});
Check("混合到期与补学也受总预算",()=>{
    var e=Create(()=>monday,30,5);
    foreach(var w in words.Take(20)){Due(e,w.Id);if(int.Parse(w.Id[1..])%2==0)e.GetProgress(w.Id).Status="Relearn";}
    var q=e.StartToday();Assert(q.Sum(x=>x.Task.EstimateSeconds)<=300&&e.Backlog().Count>0,"补学绕过预算");
});
Check("实际用时到预算暂停后续任务，主动追加才继续",()=>{
    var e=Create(()=>monday,30,5);e.StartToday();e.RecordDuration("w1","New",300);
    Assert(e.BudgetReached&&e.TodayQueue().Count==0,"实际用时到上限仍继续");
    e.AppendToday(5);Assert(e.TodayQueue().Count>0&&!e.BudgetReached,"追加不能恢复");
});
Check("完成这批后不自动追新任务，主动追加可扩批",()=>{
    var e=Create(()=>monday);var items=e.StartToday().ToList();
    foreach(var x in items)e.CompleteLesson(x.Word.Id,"");
    Assert(e.Summary(e.Today).GoalMet&&e.TodayQueue().Count==0,"批次完成后自动补新词");
    e.AddSchoolWord("later","后来","");Assert(e.TodayQueue().Count==0,"新增打开已完成批次");
    Assert(e.AppendToday(5).Count==0,"追加突破每日新词上限");
    e.State.DailyNewLimit=6;
    Assert(e.AppendToday(5).Count>0,"明确提高新词上限后追加无效");
});
Check("移出已会词不自动补位，不计新学",()=>{
    var e=Create(()=>monday);var q=e.StartToday();var first=q[0].Word.Id;var count=q.Count;
    e.ExcludeCandidate(first);Assert(e.TodayQueue().Count==count-1&&e.GetSession(e.Today).LearnedWordIds.Count==0,"移除自动加量");
});
Check("周日默认只复习，可明确调整新词上限",()=>{
    var e=Create(()=>monday.AddDays(6));Assert(e.TodayQueue().Count==0,"周日仍默认新学");
    e.State.SundayNewLimit=2;e.ReplanFuture();Assert(e.TodayQueue().Count==2,"周日不能配置");
});
Check("旧用户周目标35原样保留，改设置不伪造本周目标",()=>{
    var e=Create(()=>monday,35);e.StartToday();var q=QueueIds(e);
    e.State.WeeklyNewGoal=14;e.State.DailyNewLimit=1;e.ReplanFuture();
    Assert(e.WeekFor(e.Today).WeeklyGoal==35&&QueueIds(e)==q,"改了本周承诺或本批");
    Assert(e.WeekFor(e.Today.AddDays(7)).WeeklyGoal==14,"新目标不作用未来周");
});
Check("过去漏做历史不被补学或移出改成完成",()=>{
    var now=monday;var e=Create(()=>now);var w=e.StartToday()[0].Word;
    now=now.AddDays(1);e.ExcludeCandidate(w.Id);
    Assert(e.DayFor(e.Today.AddDays(-1)).Tasks.Any(t=>t.WordId==w.Id&&t.State=="Missed"),"历史未完成消失");
    Assert(!e.Summary(e.Today.AddDays(-1)).GoalMet,"篡改过去达标");
});
Check("同拼写独立义项不合并，移出基本义不影响新义",()=>{
    var entries=new[]{new VocabularyWord{Id="address-base",Term="address",EntryKind="Word",Meaning="地址",LemmaId="address"},
        new VocabularyWord{Id="address-sense",Term="address",EntryKind="Sense",Meaning="处理",LemmaId="address"}};
    var e=new LearningEngine(entries,new UserState{SchemaVersion=4},()=>monday);
    Assert(e.Catalog.Count==2,"构造按拼写合并");
    e.ExcludeCandidate("address-base");Assert(!e.GetProgress("address-sense").PermanentlySkipped,"移出跨义项");
    e.AddPersonalWords(entries,"书");Assert(e.State.SchoolWords.Count==2,"个人词再次合并义项");
});
Check("带s词形不因词库存在原形而机械屏蔽",()=>{
    var e=new LearningEngine(new[]{new VocabularyWord{Id="new",Term="new"},new VocabularyWord{Id="news",Term="news",RecommendationEligible=true}},new UserState{SchemaVersion=4},()=>monday);
    Assert(e.Candidates().Any(w=>w.Term=="news"),"仍用去尾猜测");
});
Check("大小写词条和明确ID保留",()=>{
    var e=new LearningEngine(new[]{new VocabularyWord{Id="China",Term="China",ScopeId="core"},new VocabularyWord{Id="china",Term="china"}},new UserState{SchemaVersion=4},()=>monday);
    e.AddPersonalWord(new VocabularyWord{Id="China",Term="China",Meaning="中国"},"书");
    Assert(e.FindWord("China")!.Term=="China"&&e.FindWord("china")!.Term=="china","大小写被改");
});
Check("个人覆盖资料保留官方范围与词族",()=>{
    var custom=new VocabularyWord{Id="x",Term="works",Meaning="我的注解"};
    var builtin=new VocabularyWord{Id="x",Term="works",ScopeId="core",LemmaId="work",Aliases=["works"]};
    var e=new LearningEngine([builtin],new UserState{SchemaVersion=4,SchoolWords=[custom]},()=>monday);
    Assert(e.FindWord("x")!.ScopeId=="core"&&e.FindWord("x")!.LemmaId=="work"&&e.FindWord("x")!.Aliases.Count==1,"个性资料遮蔽范围");
});
Check("重复个人词保留既有记忆进度",()=>{
    var e=Create(()=>monday);Due(e,"w1",4);var due=e.GetProgress("w1").NextReviewAt;
    e.AddPersonalWords([new VocabularyWord{Id="w1",Term="lexeme1",Meaning="更新"},new VocabularyWord{Id="w1",Term="lexeme1",Meaning="更新"}],"书");
    Assert(e.GetProgress("w1").NextReviewAt==due&&e.GetProgress("w1").MemoryStep==4&&e.State.SchoolWords.Count==1,"重复清空进度或资料");
});
Check("十次延迟回忆差时保守降新量，即时练习不影响",()=>{
    var e=Create(()=>monday);
    for(int i=0;i<10;i++)e.GetProgress("w1").History.Add(new ReviewRecord{Date=e.Today,DelayDays=2,Rating="Again"});
    Assert(e.EffectiveNewLimit()==3,"差表现未降量");
    e.GetProgress("w1").History.Clear();
    for(int i=0;i<30;i++)e.GetProgress("w1").History.Add(new ReviewRecord{Date=e.Today,DelayDays=0,Rating="Again"});
    Assert(e.EffectiveNewLimit()==5,"即时练习错误被当延迟表现");
});
Check("高成功率只恢复上限，不自动加量",()=>{
    var e=Create(()=>monday);for(int i=0;i<20;i++)e.GetProgress("w1").History.Add(new ReviewRecord{Date=e.Today,DelayDays=2,Rating="Good"});
    Assert(e.EffectiveNewLimit()==5,"自动突破选择上限");
});
Check("耗时校准抗单次离群，超长操作不作为校准样本",()=>{
    var e=Create(()=>monday);Assert(e.EstimateSeconds("Review")==45,"初值错误");
    for(int i=0;i<10;i++)e.RecordDuration("w1","Review",20);
    e.RecordDuration("w1","Review",299);e.RecordDuration("w1","Review",3600);
    Assert(e.EstimateSeconds("Review")<45&&e.GetSession(e.Today).DurationSamples.Count==11,"样本未校准或闲置计入");
});
Check("撤销操作一并还原耗时样本和事件，不多一次撤销",()=>{
    var e=Create(()=>monday);var w=e.StartToday()[0].Word;var before=e.State.Events.Count;
    e.CompleteLesson(w.Id,"笔记");e.RecordDuration(w.Id,"New",80);e.UndoLastAction();
    Assert(e.GetSession(e.Today).ActiveSeconds==0&&e.GetSession(e.Today).DurationSamples.Count==0&&e.State.Events.Count==before,"撤销遗漏耗时");
    Assert(e.TodayQueue().Any(x=>x.Word.Id==w.Id)&&!e.GetProgress(w.Id).InPool,"撤销未恢复任务");
});
Check("评分和批量移除撤销恢复日期、成员、识义与拼写",()=>{
    var e=Create(()=>monday);Due(e,"w1");e.StartToday();var due=e.GetProgress("w1").NextReviewAt;
    e.RecordRecall("w1","Good",false,true);e.UndoLastAction();
    Assert(e.GetProgress("w1").NextReviewAt==due&&e.GetProgress("w1").History.Count==0,"评分撤销失败");
    e.ExcludeWords(["w1","w2"]);e.UndoLastAction();
    Assert(e.GetProgress("w1").InPool&&!e.GetProgress("w2").PermanentlySkipped,"批量撤销失败");
});
Check("序列化重启保留固定批次和事件，刷新幂等",()=>{
    var e=Create(()=>monday);var w=e.StartToday()[0].Word;e.CompleteLesson(w.Id,"重启笔记");var ids=QueueIds(e);
    var copy=JsonSerializer.Deserialize<UserState>(JsonSerializer.Serialize(e.State))!;
    var restored=new LearningEngine(words,copy,()=>monday);
    for(int i=0;i<5;i++)restored.RefreshPlan();
    Assert(QueueIds(restored)==ids&&restored.GetProgress(w.Id).Note=="重启笔记","重启改变批次");
});
Check("跨日不提供过时撤销，旧熟词与到期日迁移至4",()=>{
    var now=monday;var e=Create(()=>now);e.ExcludeCandidate("w1");now=now.AddDays(1);Assert(!e.CanUndo,"允许跨日撤销");
    var state=new UserState{SchemaVersion=1,Progress=new(){["w1"]=new WordProgress{WordId="w1",PermanentlySkipped=true},
        ["w2"]=new WordProgress{WordId="w2",Status="Learning",Introduced=true,DueDate=new DateOnly(2026,9,9)}}};
    var migrated=new LearningEngine(words,state,()=>monday);
    Assert(state.SchemaVersion==4&&migrated.GetProgress("w1").PermanentlySkipped&&migrated.GetProgress("w2").DueDate==new DateOnly(2026,9,9),"迁移改坏旧记录");
});
Check("自报已会免推荐，主动重新加入能恢复",()=>{
    var e=Create(()=>monday);e.GetProgress("w1").SelfReportedKnown=true;
    Assert(e.Candidates(200).All(w=>w.Id!="w1"),"自报已会仍推荐");
    e.AddPersonalWord(new VocabularyWord{Id="w1",Term="lexeme1"},"学校");
    Assert(!e.GetProgress("w1").SelfReportedKnown&&e.GetProgress("w1").InPool,"无法召回自报词");
});
Check("设置故事外部保存清撤销避免旧快照覆盖",()=>{
    var e=Create(()=>monday);e.ExcludeCandidate("w1");e.State.Stories.Add(new SavedStory{Text="新故事"});e.ClearUndoHistory();
    Assert(!e.UndoLastAction()&&e.State.Stories.Count==1,"外部保存被旧快照覆盖");
});
Check("少量周目标按剩余学习日分配，不前两天吃完",()=>{
    var e=Create(()=>monday,6);Assert(e.TodayQueue().Count==1&&e.DayFor(e.Today).OriginalNewTarget==1,"没有均摊周量");
});
Check("模糊与忘记保守不同处理，重复错误不连续降阶",()=>{
    var e=Create(()=>monday);Due(e,"w1",6);e.StartToday();e.RecordRecall("w1","Hard",false,true);
    Assert(e.GetProgress("w1").MemoryStep==5,"模糊减阶错误");
    e.RecordRecall("w1","Again",false,true);Assert(e.GetProgress("w1").MemoryStep==5,"同轮重复错误不断归零");
});
Check("补学中自行回忆已会可结束补学，不刷高间隔",()=>{
    var e=Create(()=>monday);Due(e,"w1",6);e.StartToday();e.RecordRecall("w1","Again",false,true);
    e.RecordRecall("w1","Good",false,true);var p=e.GetProgress("w1");var due=p.NextReviewAt;
    Assert(p.Status=="Scheduled"&&p.MemoryStep==4&&e.TodayQueue().All(x=>x.Word.Id!="w1"),"不能通过回忆结束补学");
    e.RecordRecall("w1","Good",false,true);Assert(p.NextReviewAt==due&&p.MemoryStep==4,"补学回忆刷大间隔");
});
Check("延期词主动移出不留下假欠账",()=>{
    var e=Create(()=>monday,30,5);e.StartToday();e.RecordDuration("w1","New",300);
    var ids=e.DayFor(e.Today).Tasks.Where(t=>t.State=="Deferred").Select(t=>t.WordId).ToList();
    e.ExcludeWords(ids);Assert(e.Summary(e.Today).Pending==0,"移出延期仍算欠账");
});
Check("周目标200及独立周日上限保存不被静默缩小",()=>{
    var e=new LearningEngine(words,new UserState{SchemaVersion=4,WeeklyNewGoal=200,DailyNewLimit=0,SundayNewLimit=4},()=>monday);
    Assert(e.State.WeeklyNewGoal==200&&e.State.SundayNewLimit==4,"设置被静默改小");
});
Check("迁移旧版过量本日任务时保留历史并延期超量",()=>{
    var state=new UserState{SchemaVersion=3,DailyMinutes=5};var day=new DayPlan{Date=new DateOnly(2026,9,7),MinutesBudget=5};
    day.Tasks.Add(new PlanTask{WordId="w150",Kind="New",State="Done",EstimateSeconds=120});
    foreach(var w in words.Take(30)){day.Tasks.Add(new PlanTask{WordId=w.Id,Kind="Consolidate",EstimateSeconds=45});state.Progress[w.Id]=new WordProgress{WordId=w.Id,InPool=true,Introduced=true,Status="Scheduled",NextReviewAt=monday.AddDays(-1)};}
    state.CurrentWeek=new WeekPlan{Monday=day.Date,WeeklyGoal=35,Days=new(){["2026-09-07"]=day}};
    var e=new LearningEngine(words,state,()=>monday);
    Assert(day.Tasks.Where(t=>t.State is "Done" or "Pending").Sum(t=>t.EstimateSeconds)<=300&&day.Tasks.Any(t=>t.State=="Deferred"),"迁移保留了超预算必做");
    Assert(state.CurrentWeek.WeeklyGoal==35&&day.Tasks[0].State=="Done","迁移篡改历史");
});
Check("加练只能增加时间不能绕过每日新词上限，移出不复活",()=>{
    var now=monday;var e=Create(()=>now,6);e.State.DailyNewLimit=1;
    var first=e.StartToday()[0].Word;e.CompleteLesson(first.Id,"");
    now=now.AddDays(1);e.StartToday();e.RecordRecall(first.Id,"Good",false,true);
    var next=e.TodayQueue().First(x=>x.Task.Kind=="New").Word;e.CompleteLesson(next.Id,"");
    e.AppendToday(5);
    Assert(e.GetSession(e.Today).LearnedWordIds.Count==1&&e.TodayQueue().All(x=>x.Task.Kind!="New")&&e.DayFor(e.Today).NewTarget==1,"加练绕过上限");
    var candidate=e.Candidates()[0];e.ExcludeCandidate(candidate.Id);e.AppendToday(5);
    Assert(e.TodayQueue().All(x=>x.Word.Id!=candidate.Id),"移出词同日复活");
});
Console.WriteLine($"RESULT {checks-failures}/{checks} checks passed");
Environment.ExitCode=failures>0?1:0;
