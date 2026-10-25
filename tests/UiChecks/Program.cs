using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using WordCoach.App;
using WordCoach.App.Services;
using WordCoach.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root=Path.GetFullPath(args.Length>0?args[0]:"../../../../qa");
        Directory.CreateDirectory(root);
        try
        {
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            var now=new DateTimeOffset(2026,9,7,18,0,0,TimeSpan.FromHours(8));
            var runDir=Path.Combine(root,"ui-"+Guid.NewGuid().ToString("N"));
            using var store=new DataStore(runDir);
            var engine=new LearningEngine(store.LoadCatalog(),new UserState { SchemaVersion=3,WeeklyNewGoal=7 },()=>now);
            var word=engine.AddSchoolWord("remarkable","adj. 非凡的；显著的","She made remarkable progress by learning a little every day.");
            store.Save(engine.State);
            var window=new MainWindow(store,()=>now);
            Render(window,root,"01-home");
            window.ShowLearn();
            Assert(Find<Button>(window,"CompleteLesson")!=null,"学习页有完成学习按钮");
            Assert(AllText(window).Contains(word.Meaning),"学习页直接显示释义");
            Render(window,root,"02-learning");
            Find<TextBox>(window,"LessonNote")!.Text="remark：评论；值得别人评论的进步";
            Click(window,"CompleteLesson");
            Assert(window.Engine.GetProgress(word.Id).Status=="AwaitingReview","点击学完只进入待检验");
            var learnedCount=window.Engine.GetSession(window.Engine.Today).LearnedWordIds.Count;
            Click(window,"PreviousLesson");
            Assert(AllText(window).Contains(word.Meaning)&&window.Engine.GetSession(window.Engine.Today).LearnedWordIds.Count==learnedCount,"回看上一个词显示完整讲解且不重复计数");
            now=now.AddMinutes(11);
            window.StartReview("Due");
            Assert(!VisibleText(window).Contains(word.Meaning),"独立回忆前不泄露中文答案");
            Assert(!VisibleText(window).Contains(word.Phonetic)&&!VisibleText(window).Contains(word.Example),"独立回忆前不泄露例句音标");
            Render(window,root,"03-recall-hidden");
            Find<TextBox>(window,"RecallAnswer")!.Text="非凡的；显著的";
            Click(window,"SubmitRecall");
            Assert(VisibleText(window).Contains(word.Meaning),"提交后显示正确释义");
            Render(window,root,"04-recall-feedback");
            Click(window,"RecallGood");
            Assert(window.Engine.GetProgress(word.Id).IntervalDays==1,"通过界面延长到一天");
            window.ShowReviewHome();
            Find<CheckBox>(window,"SpellingMode")!.IsChecked=true;
            window.StartReview("All");
            Assert(!VisibleText(window).Contains(word.Term,StringComparison.OrdinalIgnoreCase),"拼写模式包括顶部通知都不泄露英文答案");
            Find<TextBox>(window,"RecallAnswer")!.Text="wrongspelling";
            Click(window,"SubmitRecall");
            Assert(Find<Button>(window,"RecallGood")?.IsEnabled==false,"实际控件中拼错不能评为记得");
            window.ShowReviewHome();
            Find<CheckBox>(window,"SpellingMode")!.IsChecked=false;
            window.StartReview("All");
            Click(window,"HintRecall");
            Assert(Find<Button>(window,"RecallGood")?.IsEnabled==false,"看答案后不能点击独立记得");
            Click(window,"RecallAgain");
            Assert(window.Engine.StudyQueue().Any(x=>x.Word.Id==word.Id&&x.Task.Kind=="Relearn"),"复习页错误真的回学习页");
            window.ShowLearn();Render(window,root,"05-relearning");
            Click(window,"ExcludeCandidate");
            Assert(window.Engine.GetProgress(word.Id).PermanentlySkipped&&Find<Button>(window,"NextLesson")==null,"熟词移出立即切换卡片，无需第二次继续");
            Click(window,"UndoAction");
            Assert(window.Engine.GetProgress(word.Id).Status=="Relearn"&&!window.Engine.GetProgress(word.Id).PermanentlySkipped,"实际撤销按钮恢复学习状态和卡片");
            window.ShowWeek(window.Engine.Today);Render(window,root,"06-week");
            Assert(window.Engine.WeekFor(window.Engine.Today).Days.Values.Where(d=>d.Date>window.Engine.Today).All(d=>!d.Tasks.Any(t=>t.Kind=="New"&&t.State=="Pending")),"周计划未来不预填具体新词");
            Assert(Find<Button>(window,"NavReport")==null,"移除晚间汇报入口");
            window.ShowAllWords();
            Find<TextBox>(window,"CatalogSearch")!.Text=word.Term;
            Click(window,"SelectCatalogPage");Click(window,"ExcludeSelectedWords");
            Assert(window.Engine.GetProgress(word.Id).PermanentlySkipped,"全部词汇页批量移除生词池条目");
            Click(window,"UndoAction");
            Assert(window.Engine.GetProgress(word.Id).Status=="Relearn","整批撤销恢复原有生词循环");
            Render(window,root,"07-all-words");
            window.ShowDictionary("serendipity");
            Assert(Find<Button>(window,"SearchDictionary")!=null&&AllText(window).Contains("本机离线词典"),"词典入口加载实际离线数据库");
            var meaningBox=Find<TextBox>(window,"DictionaryMeaning")!;
            Assert(!string.IsNullOrWhiteSpace(meaningBox.Text),"教材外词查到离线中文释义");
            Click(window,"AddDictionaryWord");
            var dictionaryWord=window.Engine.Catalog.Single(w=>w.Term=="serendipity");
            Assert(window.Engine.GetProgress(dictionaryWord.Id).InPool,"词典查询后主动加入生词学习");
            Click(window,"UndoAction");
            Assert(!window.Engine.State.Progress.Values.Any(p=>p.WordId==dictionaryWord.Id&&p.InPool),"撤销词典添加恢复词库与进度");
            window.ShowDictionary(word.Term);
            var beforeDuplicate=window.Engine.GetProgress(word.Id).Status;
            Find<TextBox>(window,"DictionaryContext")!.Text="The class made remarkable progress.";
            Click(window,"AddDictionaryWord");
            Assert(window.Engine.GetProgress(word.Id).Status==beforeDuplicate,"词典重复添加保留已有记忆状态");
            Click(window,"UndoAction");
            window.ShowDictionary("");
            Find<TextBox>(window,"BatchTerms")!.Text="serendipity\nserendipity\ninteroperability\nflorblequazz";
            Click(window,"PreviewBatchWords");
            Assert(AllText(window).Contains("去重后 3 条"),"批量词条预览去重");
            Click(window,"CommitBatchWords");
            Assert(window.Engine.Catalog.Any(w=>w.Term=="florblequazz"&&w.Meaning==""),"词典未收录的输入保留且不编造释义");
            Click(window,"UndoAction");
            Assert(!window.Engine.State.SchoolWords.Any(w=>w.Term=="florblequazz"),"一次撤销完整批量添加");
            window.ShowDictionary("serendipity");Render(window,root,"08-dictionary");
            window.ShowSettings();Render(window,root,"09-settings");
            window.Close();store.Dispose();
            using var reopened=new DataStore(runDir);
            var restored=new LearningEngine(reopened.LoadCatalog(),reopened.LoadState(),()=>now);
            Assert(restored.GetProgress(word.Id).Status=="Relearn"&&restored.GetProgress(word.Id).Note.Contains("remark"),"重开保留状态与笔记");
            Assert(restored.State.Events.Count(e=>e.Kind=="Recall")==2,"重开保留每次真实复习");
            reopened.Dispose();

            // Atomic backup recovery must preserve the bad original and must never silently reset.
            var backupDir=Path.Combine(root,"backup-"+Guid.NewGuid().ToString("N"));
            using(var data=new DataStore(backupDir))
            { data.Save(new UserState{SchemaVersion=3,WeeklyNewGoal=14});data.Save(new UserState{SchemaVersion=3,WeeklyNewGoal=21}); }
            File.WriteAllText(Path.Combine(backupDir,"learning-data.json"),"broken");
            using(var data=new DataStore(backupDir))
            {
                Assert(data.LoadState().WeeklyNewGoal==14,"主文件损坏时恢复上次自动备份");
                Assert(Directory.GetFiles(backupDir,"损坏原文件*").Length==1,"损坏原始数据保留");
            }
            File.WriteAllText(Path.Combine(backupDir,"learning-data.json"),"broken");
            File.WriteAllText(Path.Combine(backupDir,"learning-data.backup.json"),"broken");
            using(var data=new DataStore(backupDir))
            {
                var rejected=false;try{data.LoadState();}catch(InvalidDataException){rejected=true;}
                Assert(rejected,"主文件及备份都损坏时拒绝静默清空");
            }
            var legacyPath=Path.GetFullPath(Path.Combine(root,"../../../outputs/高中单词计划桌面版/Data/learning-data.json"));
            if(File.Exists(legacyPath))
            {
                using var data=new DataStore(Path.Combine(root,"legacy-check"));
                var state=data.ReadBackup(legacyPath);
                var skipped=state.Progress.Values.Count(p=>p.PermanentlySkipped);
                var migrated=new LearningEngine(data.LoadCatalog(),state,()=>now);migrated.RefreshPlan();
                Assert(state.Progress.Values.Count(p=>p.PermanentlySkipped)==skipped,"真实旧数据的熟词移出数量迁移不变");
            }
            Console.WriteLine("ALL UI AND PERSISTENCE CHECKS PASSED");
            app.Shutdown();return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static void Assert(bool condition,string name)
    { if(!condition)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(node,i)))yield return child;
    }
    private static void Layout(Window window)
    { var root=(FrameworkElement)window.Content;root.Measure(new Size(1340,900));root.Arrange(new Rect(0,0,1340,900));root.UpdateLayout(); }
    private static T? Find<T>(Window window,string name) where T:FrameworkElement
    { Layout(window);return Descendants((DependencyObject)window.Content).OfType<T>().FirstOrDefault(e=>e.Name==name); }
    private static string AllText(Window window)
    { Layout(window);return string.Join("\n",Descendants((DependencyObject)window.Content).OfType<TextBlock>().Select(x=>x.Text)); }
    private static string VisibleText(Window window)
    {
        Layout(window);
        bool Visible(DependencyObject node)
        { for(var n=node;n is not null;n=VisualTreeHelper.GetParent(n))if(n is UIElement u&&u.Visibility!=Visibility.Visible)return false;return true; }
        return string.Join("\n",Descendants((DependencyObject)window.Content).OfType<TextBlock>().Where(Visible).Select(x=>x.Text));
    }
    private static void Click(Window window,string name)
    { var b=Find<Button>(window,name)??throw new Exception("Missing button "+name);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void Render(Window window,string directory,string file)
    {
        Layout(window);
        var bitmap=new RenderTargetBitmap(1340,900,96,96,PixelFormats.Pbgra32);
        bitmap.Render((Visual)window.Content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(directory,file+".png"));encoder.Save(stream);
    }
}
