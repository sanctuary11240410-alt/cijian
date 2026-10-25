using System.IO.Compression;
using System.Text;
using WordCoach.App.Services;
using WordCoach.Core;

var bundled = new List<VocabularyWord>
{
    new() { Id = "abandon", Term = "abandon", Meaning = "v. 放弃", Phrases = ["abandon a plan"], NecessityScore = 80 },
    new() { Id = "ability", Term = "ability", Meaning = "n. 能力", NecessityScore = 60 }
};

var parsed = WordDocxImporter.Parse([
    "abandon /əˈbændən/ v. 放弃；抛弃",
    "固定搭配：abandon oneself to 沉溺于；with abandon 放纵地",
    "例句：He abandoned the plan when the storm arrived.",
    "ability [əˈbɪləti] n. 能力"
], bundled);

Check(parsed.Words.Count == 2, "识别两个已知词条");
var abandon = parsed.Words.Single(w => w.Term == "abandon");
Check(abandon.Phonetic == "əˈbændən", "提取音标");
Check(abandon.Meaning.Contains("抛弃"), "提取中文释义");
Check(abandon.Phrases.Any(p => p.Contains("abandon oneself")), "提取搭配");
Check(abandon.Example.StartsWith("He abandoned"), "提取例句");

var temp = Path.Combine(Path.GetTempPath(), $"word-import-{Guid.NewGuid():N}.docx");
try
{
    using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
    {
        var entry = zip.CreateEntry("word/document.xml");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write("""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
              <w:p><w:r><w:t>abandon /əˈbændən/ v. 放弃；抛弃</w:t></w:r></w:p>
              <w:p><w:r><w:t>例句：He abandoned the plan when the storm arrived.</w:t></w:r></w:p>
            </w:body></w:document>
            """);
    }
    var fromFile = WordDocxImporter.Import(temp, bundled);
    Check(fromFile.Words.Count == 1 && fromFile.Words[0].Phonetic.Length > 0, "读取 docx XML");
}
finally
{
    File.Delete(temp);
}

Console.WriteLine("ALL IMPORT CHECKS PASSED");

var guarded = WordDocxImporter.Parse([
    "abandon /əˈbændən/ v. 放弃",
    "abandon a plan 放弃一个计划",
    "ability [əˈbɪləti] n. 能力"
], bundled);
Check(guarded.Words.Count == 2, "搭配句不会被误拆成新词条");
Check(guarded.Words.First(w=>w.Term=="abandon").SourceExcerpt.Contains("abandon a plan"), "导入保留原文供核对");

using (var store = new DataStore(Path.Combine(Path.GetTempPath(), "word-import-catalog-" + Guid.NewGuid().ToString("N"))))
{
    var full = store.LoadCatalog();
    var cases = WordDocxImporter.Parse(["China n. 中国", "china n. 瓷器", "remarkable adj. 显著的"], full);
    Check(cases.Words.Count == 3, "完整词库含同拼写与大小写异义时Word导入不崩溃");
    Check(cases.Words.First(w => w.Term == "China").Id != cases.Words.First(w => w.Term == "china").Id, "Word资料保留大小写异义身份");
    Check(cases.Words.All(w => w.ScopeId == VocabularyImport.CoreScopeId), "Word补充保留核心范围版本");
}

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL {name}");
    Console.WriteLine($"PASS {name}");
}
