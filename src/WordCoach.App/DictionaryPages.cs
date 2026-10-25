using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WordCoach.App.Services;
using WordCoach.Core;

namespace WordCoach.App;

public partial class MainWindow
{
    private OfflineDictionary? _dictionary;
    private string _dictionaryQuery = "";
    private string _personalSource = "学校";
    private int _dictionarySearchVersion;
    private string _dictionaryEntryId = "";
    private readonly Dictionary<string, (string Meaning, string Context)> _dictionaryDrafts = [];

    public void ShowDictionary(string? initialQuery = null, string? entryId = null)
    {
        Page("pool", "词典 · 添加生词", "先查明白，再决定是否加入间隔学习");
        if (initialQuery is not null) _dictionaryQuery = initialQuery;
        _dictionaryEntryId = entryId ?? "";
        try { _dictionary ??= new OfflineDictionary(); }
        catch (Exception error) { Notify("离线词典暂不可用：" + error.Message + "。仍可查询已有词条或手动添加。"); }
        var query = Input("DictionaryQuery", _dictionaryQuery);
        var source = new ComboBox { Name = "PersonalSource", ItemsSource = new[] { "学校", "单词书", "真题", "个人积累" }, SelectedItem = _personalSource };
        source.SelectionChanged += (_, _) => _personalSource = source.SelectedItem?.ToString() ?? "学校";
        var results = Stack();
        async void Search()
        {
            var version = ++_dictionarySearchVersion;
            var engine = _engine;
            try {
            _dictionaryQuery = query.Text.Trim(); results.Children.Clear();
            if (_dictionaryQuery.Length == 0) { results.Children.Add(Txt("输入英文单词、短语或中文释义。查询不会改变学习进度。", 14, "#697B70")); return; }
            var searched = _dictionaryQuery;
            results.Children.Add(Txt("正在查询本机词典…", 12, "#697B70"));
            var matches = _engine.Catalog.Where(w => w.Term.Equals(_dictionaryQuery, StringComparison.OrdinalIgnoreCase)).ToList();
            var exactCase = matches.Where(w => w.Term.Equals(_dictionaryQuery, StringComparison.Ordinal)).ToList();
            var local = _engine.FindWord(_dictionaryEntryId) ?? (exactCase.Count == 1 ? exactCase[0] : matches.Count == 1 ? matches[0] : null);
            var ambiguous = local is null && matches.Count > 1;
            var dictionary = _dictionary;
            var lookup = await Task.Run(() => (
                Found: dictionary?.Available == true ? dictionary.Search(searched, 24) : Array.Empty<DictionaryEntry>(),
                Exact: dictionary?.Available == true ? dictionary.Lookup(searched) : null));
            if (version != _dictionarySearchVersion || _page != "pool" || !ReferenceEquals(engine, _engine)) return;
            var found = lookup.Found; var exact = lookup.Exact;
            results.Children.Clear();
            if (ambiguous)
            {
                results.Children.Add(Txt("这个拼写有不同学习条目，请选择要查看和补充的一个：", 14, "#63707A"));
                foreach (var candidate in matches)
                    results.Children.Add(Space(Btn(candidate.Term + " · " + Short(candidate.Meaning, 75), () => ShowDictionary(candidate.Term, candidate.Id), secondary: true), 8));
                return;
            }
            if (local is not null) RenderEntry(CloneWord(local), "已有词条 · " + local.Source);
            else if (exact is not null) RenderEntry(FromDictionary(exact), "离线词典 · " + exact.Source);
            else if (found.Count > 0 && found[0].Term.Equals(_dictionaryQuery, StringComparison.OrdinalIgnoreCase)) RenderEntry(FromDictionary(found[0]), "离线词典");
            else if (ValidTerm(_dictionaryQuery))
                RenderEntry(new VocabularyWord { Term = _dictionaryQuery.Trim(), Source = "个人补充（词典未收录）" }, "尚未找到精确词条，可补充后保存");
            else results.Children.Add(Txt("请选择下面的英文词条；也可在批量输入区按行录入生词。", 13, "#697B70"));

            if (found.Count > 0)
            {
                results.Children.Add(Space(Txt("相关词条 · 点击查看", 16, "#254E3A", true), 18));
                foreach (var entry in found.Where(e => !e.Term.Equals(local?.Term ?? exact?.Term, StringComparison.OrdinalIgnoreCase)))
                {
                    var b = Btn("", () => ShowDictionary(entry.Term), secondary: true);
                    b.HorizontalAlignment = HorizontalAlignment.Stretch;
                    b.HorizontalContentAlignment = HorizontalAlignment.Left;
                    b.Content = Stack(Txt(entry.Term, 17, "#254E3A", true), Txt(Short(entry.Meaning, 130), 12, "#697B70"));
                    results.Children.Add(Space(b, 7));
                }
            }
            } catch (Exception error) {
                if (version == _dictionarySearchVersion && _page == "pool") { results.Children.Clear(); Notify("查询未完成：" + error.Message); }
            }
        }
        void RenderEntry(VocabularyWord word, string sourceText)
        {
            var draftKey = word.Id.Length > 0 ? word.Id : "dictionary:" + word.Term;
            var draft = _dictionaryDrafts.GetValueOrDefault(draftKey, (word.Meaning, word.Example));
            var meaning = Input("DictionaryMeaning", draft.Item1, true);
            var context = Input("DictionaryContext", draft.Item2, true);
            meaning.TextChanged += (_, _) => _dictionaryDrafts[draftKey] = (meaning.Text, context.Text);
            context.TextChanged += (_, _) => _dictionaryDrafts[draftKey] = (meaning.Text, context.Text);
            var existing = _engine.FindWord(word.Id);
            var enrolled = existing is not null && _engine.GetProgress(existing.Id).InPool && !_engine.GetProgress(existing.Id).PermanentlySkipped;
            var block = Stack(Txt(word.Term, 35, "#254E3A", true), Txt(Phonetic(word), 16, "#697B70"),
                Space(Txt("释义（可补充课堂里遇到的意思）", 12, "#697B70")), Space(meaning, 5));
            if (word.Phrases.Count > 0) block.Children.Add(Space(Txt(string.Join("\n", word.Phrases), 14)));
            if (!string.IsNullOrWhiteSpace(word.SourceExcerpt)) block.Children.Add(Space(new Expander { Header = "英文释义、词形与来源补充", Content = Txt(word.SourceExcerpt, 13) }));
            block.Children.Add(Space(Txt("例句 / 搭配 / 这次遇到它的语境（可选）", 12, "#697B70")));
            block.Children.Add(Space(context, 5));
            block.Children.Add(Space(Btn(enrolled ? "保存补充 · 保留现有记忆进度" : "加入生词 · 安排间隔学习", () => {
                if (!ValidTerm(word.Term)) { Notify("请输入一个英文单词或短语，长度不超过 100 字符。"); return; }
                word.Meaning = meaning.Text.Trim(); word.Example = context.Text.Trim();
                _undoPage = "pool";
                var saved = _engine.AddPersonalWord(word, _personalSource);
                Save(); ShowDictionary(saved.Term, saved.Id); Notify(AddedMessage(saved));
            }, "AddDictionaryWord")));
            block.Children.Add(Space(Txt(sourceText + "。缺少例句的词条会明确留空，可用你的语境补充。", 11, "#7F8A80")));
            results.Children.Add(Card(block));
        }
        query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Guard(Search); e.Handled = true; } };
        query.TextChanged += (_, _) => { _dictionaryQuery = query.Text; _dictionaryEntryId = ""; _dictionarySearchVersion++; results.Children.Clear(); };
        PageBody.Children.Add(Card(Stack(Txt(_dictionary?.Available == true ? $"本机离线词典 · {_dictionary.EntryCount:N0} 条" : "已有词库查询 · 可手动补充", 20, "#254E3A", true),
            Space(Txt("学校遇到的词、单词书生词和真题短语都从这里加入。已加入的词再次保存只补充资料。", 13, "#697B70"), 8),
            Space(query), Space(Row(Btn("查词", Search, "SearchDictionary"), Txt("生词来源", 13), source)))));
        PageBody.Children.Add(results);
        Search();
        PageBody.Children.Add(Space(Btn("有一份不会词清单？导入 TXT →", ShowImport, "OpenImport", true)));
    }

    private void AddBatchInput()
    {
        var input = Input("BatchTerms", "", true);
        input.MinHeight = 100;
        var preview = Stack();
        var rows = new List<(VocabularyWord Word, CheckBox Selected, TextBox Meaning)>();
        var commit = Btn("将勾选词加入学习", () => {
            var selected = rows.Where(r => r.Selected.IsChecked == true).ToList();
            if (selected.Count == 0) { Notify("先预览并勾选要加入的词。"); return; }
            foreach (var row in selected) row.Word.Meaning = row.Meaning.Text.Trim();
            _undoPage = "pool";
            var saved = _engine.AddPersonalWords(selected.Select(r => r.Word), _personalSource);
            Save(); ShowDictionary(""); Notify($"已保存 {saved.Count} 个不同词条，按个人生词优先安排；未查到的词保留你的输入，可随后补充释义。可撤销整批添加。");
        }, "CommitBatchWords");
        commit.IsEnabled = false;
        void Preview()
        {
            rows.Clear(); preview.Children.Clear(); commit.IsEnabled = false;
            var raw = input.Text.Split(['\n', '\r', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (raw.Length > 200) { Notify("每次最多预览 200 条，请分批输入。"); return; }
            var invalid = new List<string>();
            foreach (var line in raw.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // A tab can separate an English headword from a user-supplied meaning.
                var fields = line.Split('\t', 2, StringSplitOptions.TrimEntries);
                if (fields.Length == 1 && Regex.Match(line, @"^(.+?)\s+([\u3400-\u9fff].*)$") is { Success: true } match)
                    fields = [match.Groups[1].Value, match.Groups[2].Value];
                var term = Regex.Replace(fields[0], @"\s+", " ");
                if (!ValidTerm(term)) { invalid.Add(term); continue; }
                var local = _engine.Catalog.FirstOrDefault(w => w.Term.Equals(term, StringComparison.OrdinalIgnoreCase));
                var entry = _dictionary?.Available == true ? _dictionary.Lookup(term) : null;
                var word = local is not null ? CloneWord(local) : entry is not null ? FromDictionary(entry) : new VocabularyWord { Term = term, Source = "个人补充（词典未收录）" };
                if (fields.Length == 2) word.Meaning = fields[1];
                if (rows.Any(r => r.Word.Term.Equals(word.Term, StringComparison.OrdinalIgnoreCase))) continue;
                var selected = new CheckBox { Content = word.Term + (string.IsNullOrWhiteSpace(word.Meaning) ? " · 未收录，请补充" : ""), IsChecked = true, FontSize = 14, Margin = new Thickness(0, 8, 0, 5) };
                var meaning = Input("", word.Meaning); meaning.MinHeight = 35;
                rows.Add((word, selected, meaning)); preview.Children.Add(selected); preview.Children.Add(meaning);
            }
            preview.Children.Insert(0, Txt($"去重后 {rows.Count} 条 · 已有生词保留记忆进度 · 变形词可归到词典原形", 12, "#697B70"));
            if (invalid.Count > 0) preview.Children.Add(Space(Txt($"以下 {invalid.Count} 条未识别，尚未保存，请修改原输入：{string.Join("；", invalid)}", 12, "#9B643A")));
            commit.IsEnabled = rows.Count > 0;
        }
        PageBody.Children.Add(Card(Stack(Txt("一次加入今天积累的生词", 21, "#254E3A", true),
            Space(Txt("一行一个单词或短语，也可用逗号分隔。整条短语保留空格；可用 Tab 分隔英文与自己的中文释义。", 12, "#697B70"), 6),
            Space(input), Space(Btn("预览并自动查释义", Preview, "PreviewBatchWords", true)), Space(preview), Space(commit))));
    }

    private string AddedMessage(VocabularyWord word)
    {
        var p = _engine.GetProgress(word.Id);
        if (p.Introduced) return $"{word.Term} 的资料已保存，保留已有学习进度与复现时间。";
        if (_engine.State.NextWeekPriority.Contains(word.Id)) return $"{word.Term} 已保存，本周剩余容量不足，列入下周优先队列。";
        var today = _engine.DayFor(Today).Tasks.Any(t => t.WordId == word.Id && t.State == "Pending");
        return today ? $"{word.Term} 已加入今日学习。" : $"{word.Term} 已进入本周优先队列，每天按剩余容量安排。";
    }
    private static bool ValidTerm(string term) => term.Length is > 0 and <= 100 && Regex.IsMatch(term, @"^[A-Za-z\u00c0-\u024f][A-Za-z\u00c0-\u024f\d '\-’./()]*$");
    private static string Short(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static VocabularyWord CloneWord(VocabularyWord word) => JsonSerializer.Deserialize<VocabularyWord>(JsonSerializer.Serialize(word))!;
    private static VocabularyWord FromDictionary(DictionaryEntry entry) => new()
    {
        Term = entry.Term, Phonetic = entry.Phonetic, Meaning = entry.Meaning,
        Source = entry.Source,
        SourceExcerpt = string.Join("\n", new[] { entry.EnglishDefinition, FormatExchange(entry.Exchange) }.Where(x => !string.IsNullOrWhiteSpace(x)))
    };
    private static string FormatExchange(string exchange)
    {
        if (exchange.Any(c => c >= '\u3400' && c <= '\u9fff')) return exchange;
        var labels = new Dictionary<string, string> { ["p"] = "过去式", ["d"] = "过去分词", ["i"] = "现在分词", ["3"] = "第三人称单数", ["r"] = "比较级", ["t"] = "最高级", ["s"] = "复数", ["0"] = "原形" };
        return string.Join("；", exchange.Split('/').Select(part => part.Split(':', 2)).Where(parts => parts.Length == 2 && labels.ContainsKey(parts[0])).Select(parts => labels[parts[0]] + "：" + parts[1]));
    }
}
