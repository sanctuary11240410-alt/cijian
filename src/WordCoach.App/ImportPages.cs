using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WordCoach.App.Services;
using WordCoach.Core;

namespace WordCoach.App;

public partial class MainWindow
{
    private string _importText = "";
    private string _importSource = "TXT 生词清单";
    private bool _importComplete;
    private bool _importExtensions;
    private int _importGeneration;

    public void ShowImport()
    {
        Page("import", "导入不会的词", "一个词或短语一行；先预览，再决定如何安排");
        _importGeneration++;
        var input = Input("ImportText", _importText, true);
        input.Height = 145;
        var incremental = new RadioButton { Name = "ImportIncremental", GroupName = "ImportNature", Content = "零散积累（默认）", IsChecked = !_importComplete, Margin = new Thickness(0, 0, 20, 0) };
        var complete = new RadioButton { Name = "ImportComplete", GroupName = "ImportNature", Content = "我已完整排查下列范围，只列出不会的词", IsChecked = _importComplete };
        var extensions = new CheckBox { Name = "ImportExtensions", Content = "同时涵盖本机精选扩展包", IsChecked = _importExtensions, IsEnabled = _importComplete };
        var coreCount = _engine.Catalog.Count(w => w.ScopeId == VocabularyImport.CoreScopeId);
        var extensionCount = _engine.Catalog.Count(w => w.ScopeId == VocabularyImport.ExtensionScopeId);
        var scopeText = Txt("", 12, "#64748B");
        var previewPanel = Stack();
        VocabularyImportPreview? currentPreview = null;
        var commit = Btn("确认导入", () =>
        {
            if (currentPreview is null) return;
            _undoPage = "import";
            var result = _engine.ApplyVocabularyImport(currentPreview);
            if (!Save()) return;
            ShowImport();
            Notify(result.AlreadyImported ? "同一内容和范围已导入过，现有进度保持不变。" :
                $"已导入 {result.UnknownCount} 个生词，{result.SelfReportedKnownCount} 个自报已会，保留 {result.PreservedProgressCount} 个既有学习进度。整批可撤销；每日任务仍受时间预算约束。");
        }, "CommitVocabularyImport");
        commit.IsEnabled = false;
        void ScopeCaption()
        {
            extensions.IsEnabled = _importComplete;
            scopeText.Text = _importComplete
                ? $"完整排查范围：2020 课标词表（本机 {coreCount:N0} 条）" + (_importExtensions ? $" ＋ 本机精选扩展 {extensionCount} 条。" : "。扩展词仍为未评估。") +
                  "本机精选包不是完整的 1200 条扩展词书；不能据此推断其他词全部已会。已有真实进度和独立新义保留。"
                : "只把列出的词加入个人待学池。没有列出的词，全部保留原状态。";
        }
        void Invalidate()
        {
            _importGeneration++; currentPreview = null; commit.IsEnabled = false; previewPanel.Children.Clear(); ScopeCaption();
        }
        input.TextChanged += (_, _) => { _importText = input.Text; Invalidate(); };
        incremental.Checked += (_, _) => { _importComplete = false; Invalidate(); };
        complete.Checked += (_, _) => { _importComplete = true; Invalidate(); };
        extensions.Checked += (_, _) => { _importExtensions = true; Invalidate(); };
        extensions.Unchecked += (_, _) => { _importExtensions = false; Invalidate(); };
        var previewButton = Btn("预览清单", () => { }, "PreviewVocabularyImport");
        previewButton.Click += async (_, _) =>
        {
            var generation = ++_importGeneration;
            previewButton.IsEnabled = false; commit.IsEnabled = false; previewPanel.Children.Clear();
            previewPanel.Children.Add(Txt("正在本机词库中匹配…", 13, "#64748B"));
            try
            {
                var text = input.Text; var completeScreen = _importComplete;
                var scopes = _importExtensions ? new[] { VocabularyImport.CoreScopeId, VocabularyImport.ExtensionScopeId } : [VocabularyImport.CoreScopeId];
                // The worker uses a detached snapshot and its own dictionary connection.
                var catalog = JsonSerializer.Deserialize<List<VocabularyWord>>(JsonSerializer.Serialize(_engine.Catalog))!;
                var state = JsonSerializer.Deserialize<UserState>(JsonSerializer.Serialize(_engine.State))!;
                var source = _importSource;
                var preview = await Task.Run(() =>
                {
                    OfflineDictionary? dictionary = null;
                    try
                    {
                        try { dictionary = new OfflineDictionary(); } catch { /* Exact local words and manual entries remain available. */ }
                        return VocabularyImport.Preview(text, catalog, state, completeScreen, scopes, source,
                            term => dictionary?.Available == true && dictionary.Lookup(term) is { } found ? FromDictionary(found) : null);
                    }
                    finally { dictionary?.Dispose(); }
                });
                if (_page != "import" || generation != _importGeneration) return;
                currentPreview = preview; previewPanel.Children.Clear();
                previewPanel.Children.Add(Txt($"{preview.Rows.Count} 个不同生词 · 去重 {preview.DuplicateCount} 条 · 词表外 / 未匹配 {preview.Rows.Count(r => r.Unmatched)} 条", 17, "#263547", true));
                if (preview.CompleteScreen)
                    previewPanel.Children.Add(Space(Txt($"范围内 {preview.ScopeCount:N0} 条：另有 {preview.KnownWordIds.Count} 个自报已会免推荐，保留 {preview.PreservedProgressIds.Count} 个既有进度、{preview.PreservedSenseIds.Count} 个独立新义。自报已会不计作考试成绩。", 13, "#64748B"), 6));
                if (preview.AlreadyImported) previewPanel.Children.Add(Space(Txt("这一内容和范围已经导入过，重复确认不会重复加任务或重置间隔。", 13, "#64748B"), 6));
                var list = new ListBox { Name = "ImportPreviewRows", MaxHeight = 250, ItemsSource = preview.Rows.Select(r =>
                    r.Input + (r.Input != r.Word.Term ? " → " + r.Word.Term : "") + "    " + r.Match + "\n" +
                    (string.IsNullOrWhiteSpace(r.Word.Meaning) ? "释义待补充，可在词典页编辑" : Short(r.Word.Meaning, 90))).ToList() };
                VirtualizingPanel.SetIsVirtualizing(list, true);
                VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
                previewPanel.Children.Add(Space(list, 10));
                if (preview.InvalidLines.Count > 0)
                    previewPanel.Children.Add(Space(Txt("请修改以下行后重新预览（只输入英文词或短语）：\n" + string.Join("\n", preview.InvalidLines.Take(20)), 13, "#A43D33")));
                commit.IsEnabled = preview.Rows.Count > 0 && preview.InvalidLines.Count == 0;
                Actions(commit);
            }
            catch (Exception error) { if (generation == _importGeneration) { previewPanel.Children.Clear(); Notify("预览未完成：" + error.Message); } }
            finally { previewButton.IsEnabled = true; }
        };
        void PickFile()
        {
            var dialog = new OpenFileDialog { Title = "选择不会词清单", Filter = "文本清单 (*.txt)|*.txt|所有文件 (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 1_000_000) throw new ArgumentException("文本超过 1 MB，请拆分后导入。");
            _importSource = "TXT · " + info.Name;
            input.Text = ReadVocabularyText(dialog.FileName);
        }
        ScopeCaption();
        PageBody.Children.Add(Card(Stack(Row(Btn("选择 TXT 文件", PickFile, "ChooseVocabularyFile", true), Txt("也可直接粘贴下面", 13, "#64748B")),
            Space(input, 5), Space(Row(incremental, complete), 14), Space(extensions, 2), Space(scopeText, 8), Space(previewButton, 14))));
        PageBody.Children.Add(previewPanel);
    }

    internal static string ReadVocabularyText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
    }
}
