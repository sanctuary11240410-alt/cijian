using System.Windows;
using System.Windows.Controls;
using WordCoach.Core;

namespace WordCoach.App;

public partial class MainWindow
{
    private readonly HashSet<string> _selectedWords = [];
    private string _catalogFilter = "";
    private string _catalogScope = "全部学习词汇";
    private int _catalogPage;

    public void ShowAllWords()
    {
        Page("words", "全部词汇", "快速排除熟词 · 按词查看学习记录 · 批量操作可撤销");
        var search = Input("CatalogSearch", _catalogFilter);
        var scope = new ComboBox { Name = "CatalogScope", ItemsSource = new[] { "全部学习词汇", "核心课标词", "拓展推荐", "我的生词", "导入词表", "自报已会", "已移除" }, SelectedItem = _catalogScope };
        var counter = Txt("", 12, "#697B70");
        var list = Stack();
        var navigation = Row();
        var selection = Txt("", 12, "#697B70");
        List<VocabularyWord> Filtered()
        {
            var imported = _engine.State.ImportedWords.Concat(_engine.State.SchoolWords).Select(w => w.Id).ToHashSet();
            return _engine.Catalog.Where(w => {
                var p = _engine.GetProgress(w.Id);
                return _catalogScope switch {
                    "拓展推荐" => w.RecommendationEligible && !p.PermanentlySkipped,
                    "核心课标词" => w.ScopeId == VocabularyImport.CoreScopeId,
                    "我的生词" => p.InPool && !p.PermanentlySkipped,
                    "导入词表" => imported.Contains(w.Id),
                    "自报已会" => p.SelfReportedKnown,
                    "已移除" => p.PermanentlySkipped,
                    _ => true
                };
            }).Where(w => w.Term.Contains(_catalogFilter, StringComparison.OrdinalIgnoreCase) || w.Meaning.Contains(_catalogFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(w => w.Term, StringComparer.OrdinalIgnoreCase).ToList();
        }
        void Render()
        {
            list.Children.Clear(); navigation.Children.Clear();
            var words = Filtered();
            var pages = Math.Max(1, (words.Count + 39) / 40);
            _catalogPage = Math.Clamp(_catalogPage, 0, pages - 1);
            counter.Text = $"筛选到 {words.Count:N0} 条 · 第 {_catalogPage + 1}/{pages} 页 · 词典完整词库请在查词页搜索";
            selection.Text = $"已选 {_selectedWords.Count} 条（翻页保留选择）";
            foreach (var w in words.Skip(_catalogPage * 40).Take(40))
            {
                var p = _engine.GetProgress(w.Id);
                var checkbox = new CheckBox { Name = "SelectWord", Tag = w.Id, IsChecked = _selectedWords.Contains(w.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                checkbox.Checked += (_, _) => { _selectedWords.Add(w.Id); selection.Text = $"已选 {_selectedWords.Count} 条（翻页保留选择）"; };
                checkbox.Unchecked += (_, _) => { _selectedWords.Remove(w.Id); selection.Text = $"已选 {_selectedWords.Count} 条（翻页保留选择）"; };
                var detail = Stack(Txt(Phonetic(w), 14, "#697B70"), Space(Txt(w.Meaning, 15), 6), Space(Txt(string.Join("\n", w.Phrases), 13)), Space(Txt(w.Example, 13)));
                detail.Children.Add(Space(Txt(p.NextReviewAt is {} at ? $"下次复现：{at:M月d日 HH:mm}" : p.PermanentlySkipped ? "已移出学习，不再自动推荐。" : p.InPool ? "在个人生词队列中，按容量安排。" : "尚未加入个人生词。", 12, "#8C6B40")));
                if (p.Note.Length > 0) detail.Children.Add(Space(Txt("我的记忆：" + p.Note, 13)));
                detail.Children.Add(Space(Txt("资料：" + w.Source, 11, "#697B70")));
                detail.Children.Add(Txt(w.Coverage, 11, "#697B70"));
                if (w.EvidenceSources.Count > 0) detail.Children.Add(Space(Txt(string.Join("\n", w.EvidenceSources), 11, "#697B70")));
                foreach (var evt in _engine.State.Events.Where(e => e.WordId == w.Id).TakeLast(6).Reverse())
                    detail.Children.Add(Txt($"{evt.At:M/d HH:mm} · {evt.Result}", 11, "#697B70"));
                detail.Children.Add(Space(Row(Btn("词典与补充", () => ShowDictionary(w.Term, w.Id), secondary: true),
                    Btn(p.PermanentlySkipped ? "重新加入学习" : "加入个人生词", () => {
                        _undoPage = "words"; _engine.AddPersonalWord(CloneWord(w), "个人积累"); Save(); ShowAllWords(); Notify(AddedMessage(w));
                    }, secondary: true))));
                var expander = new Expander { Header = Stack(Txt(w.Term + "  ·  " + LearningEngine.StatusLabel(p), 16, "#254E3A", true), Txt(Short(w.Meaning, 85), 12, "#697B70")), Content = detail };
                var row = new DockPanel(); DockPanel.SetDock(checkbox, Dock.Left); row.Children.Add(checkbox); row.Children.Add(expander);
                list.Children.Add(Card(row, "#FFFFFF", 13));
            }
            if (words.Count == 0) list.Children.Add(Card(Txt("没有匹配的词条，可以换一个筛选条件或到词典搜索。", 14, "#697B70")));
            var prev = Btn("← 上一页", () => { _catalogPage--; Render(); PageScroll.ScrollToTop(); }, "PreviousCatalogPage", true); prev.IsEnabled = _catalogPage > 0;
            var next = Btn("下一页 →", () => { _catalogPage++; Render(); PageScroll.ScrollToTop(); }, "NextCatalogPage", true); next.IsEnabled = _catalogPage + 1 < pages;
            navigation.Children.Add(prev); navigation.Children.Add(next);
        }
        search.TextChanged += (_, _) => { _catalogFilter = search.Text.Trim(); _catalogPage = 0; Render(); };
        scope.SelectionChanged += (_, _) => { _catalogScope = scope.SelectedItem?.ToString() ?? "全部学习词汇"; _catalogPage = 0; Render(); };
        PageBody.Children.Add(Card(Stack(Txt("熟词可以整批移出，不必逐个测试", 21, "#254E3A", true),
            Space(search), Space(Row(scope, Btn("选中本页", () => { foreach (var w in Filtered().Skip(_catalogPage * 40).Take(40)) _selectedWords.Add(w.Id); Render(); }, "SelectCatalogPage", true),
                Btn("选中全部筛选结果", () => { foreach (var w in Filtered()) _selectedWords.Add(w.Id); Render(); }, "SelectFilteredWords", true),
                Btn("清空选择", () => { _selectedWords.Clear(); Render(); }, "ClearWordSelection", true))),
            Space(Row(Btn("已掌握 · 移出所选词", () => {
                if (_selectedWords.Count == 0) { Notify("请先勾选要移出的词。"); return; }
                var count = _selectedWords.Count; _undoPage = "words";
                _engine.ExcludeWords(_selectedWords.ToArray()); _selectedWords.Clear(); Save(); ShowAllWords();
                Notify($"已处理 {count} 条选择。移出不计作新学，可用上方按钮撤销整批操作。");
            }, "ExcludeSelectedWords"), selection)), Space(counter, 6))));
        PageBody.Children.Add(list); PageBody.Children.Add(navigation); Render();
    }

    private void UndoOperation()
    {
        if (!_engine.UndoLastAction()) { Notify("当前没有可撤销的学习操作。跨日后请在全部词汇中调整。"); return; }
        var wordId = _engine.LastUndoneWordId;
        _selectedWords.Clear(); Save();
        if (_undoPage == "today") { _todaySession = true; ShowTodayTask(); }
        else if (_undoPage == "import") ShowImport();
        else if (_undoPage == "review" && _engine.FindWord(wordId) is {} w)
        { _reviewedThisRun.Clear(); _reviewWords = [w]; ShowReviewCard(); }
        else if (_undoPage == "pool") ShowDictionary(_dictionaryQuery);
        else if (_undoPage == "words") ShowAllWords();
        else ShowLearnWord(string.IsNullOrEmpty(wordId) ? null : wordId);
        // Do not put an English headword in a recall notice: it can leak the spelling answer.
        Notify("已撤销上一步操作，词汇状态、复现时间和任务计数已恢复。");
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => Guard(UndoOperation);
    private void Words_Click(object sender, RoutedEventArgs e) => Guard(ShowAllWords);
}
