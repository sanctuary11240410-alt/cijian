using System.IO.Compression;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WordCoach.Core;

namespace WordCoach.App.Services;

public sealed record WordImportResult(IReadOnlyList<VocabularyWord> Words, int EnrichedCount);

public static partial class WordDocxImporter
{
    public static WordImportResult Import(string path, IReadOnlyList<VocabularyWord> bundled)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到所选文件", path);
        var lines = ReadParagraphs(path);
        return Parse(lines, bundled);
    }

    public static WordImportResult Parse(IEnumerable<string> paragraphs, IReadOnlyList<VocabularyWord> bundled)
    {
        var known = bundled.GroupBy(w => w.Term, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.EntryKind == "Word" ? 0 : w.EntryKind == "Phrase" ? 1 : 2).First(), StringComparer.Ordinal);
        var orderedTerms = known.Keys.OrderByDescending(x => x.Length).ToList();
        var blocks = new List<(string Term, List<string> Lines)>();
        (string Term, List<string> Lines)? current = null;

        foreach (var raw in paragraphs)
        {
            foreach (var candidate in raw.Replace('\u3000', ' ').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var line = Regex.Replace(SpaceRegex().Replace(candidate.Trim(), " "), @"^\s*\d+[.)、]\s*", "");
                if (line.Length == 0) continue;
                var term = FindLeadingTerm(line, orderedTerms) ?? FindNewLeadingTerm(line);
                if (term is not null)
                {
                    if (current is not null) blocks.Add(current.Value);
                    current = (term, [line]);
                }
                else if (current is not null && current.Value.Lines.Count < 12)
                {
                    current.Value.Lines.Add(line);
                }
            }
        }
        if (current is not null) blocks.Add(current.Value);

        var imported = new List<VocabularyWord>();
        var enriched = 0;
        foreach (var group in blocks.GroupBy(b => b.Term, StringComparer.Ordinal))
        {
            var term = group.Key;
            known.TryGetValue(term, out var original);
            var lines = group.SelectMany(x => x.Lines).Distinct().ToList();
            var joined = string.Join("\n", lines);
            var phonetic = ExtractPhonetic(lines[0]);
            var meaning = ExtractMeaning(lines, term, phonetic);
            var phrases = ExtractPhrases(lines, term);
            var example = ExtractExample(lines, term);
            var word = new VocabularyWord
            {
                Id = original?.Id ?? $"import-{Slug(term)}",
                Term = original?.Term ?? term,
                LemmaId = original?.LemmaId ?? term.ToLowerInvariant(),
                ScopeId = original?.ScopeId ?? "",
                EntryKind = original?.EntryKind ?? (term.Contains(' ') ? "Phrase" : "Word"),
                Aliases = original?.Aliases.ToList() ?? [],
                RecommendationEligible = original?.RecommendationEligible ?? false,
                RecommendationReason = original?.RecommendationReason ?? "个人导入资料",
                RecommendationPriority = original?.RecommendationPriority ?? 0,
                Coverage = original?.Coverage ?? "个人资料",
                EvidenceSources = original?.EvidenceSources.ToList() ?? [],
                Phonetic = FirstUseful(phonetic, original?.Phonetic),
                Meaning = FirstUseful(meaning, original?.Meaning, "释义待补充"),
                Phrases = phrases.Count > 0 ? phrases : original?.Phrases.ToList() ?? [],
                Example = FirstUseful(example, original?.Example),
                SourceExcerpt = joined,
                ExamFrequency = original?.ExamFrequency ?? 0,
                NecessityScore = original?.NecessityScore ?? 55,
                Reasons = original?.Reasons.ToList() ?? ["个人词表"],
                Source = "用户导入 Word 词表",
                IsFoundation = original?.IsFoundation ?? false
            };
            if (!string.IsNullOrWhiteSpace(phonetic) || phrases.Count > 0 || !string.IsNullOrWhiteSpace(example)) enriched++;
            imported.Add(word);
        }
        return new WordImportResult(imported, enriched);
    }

    private static IEnumerable<string> ReadParagraphs(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("文件不是有效的 Word 文档");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        foreach (var paragraph in document.Descendants(w + "p"))
        {
            var text = string.Concat(paragraph.Descendants().Where(x => x.Name == w + "t" || x.Name == w + "tab" || x.Name == w + "br")
                .Select(x => x.Name == w + "t" ? x.Value : " ")).Trim();
            if (text.Length > 0) yield return text;
        }
    }

    private static string? FindLeadingTerm(string line, List<string> terms)
    {
        foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        foreach (var term in terms)
        {
            if (!line.StartsWith(term, comparison)) continue;
            if (comparison == StringComparison.OrdinalIgnoreCase && terms.Count(x => x.Equals(term, comparison)) > 1) continue;
            if (line.Length == term.Length) return term;
            if (!char.IsLetter(line[term.Length]) && HeaderDetailRegex().IsMatch(line[term.Length..])) return term;
        }
        return null;
    }

    private static string? FindNewLeadingTerm(string line)
    {
        var match = NewEntryRegex().Match(line);
        if (!match.Success) return null;
        var term = match.Groups[1].Value.Trim();
        return HeaderDetailRegex().IsMatch(line[term.Length..]) ? term : null;
    }

    private static string ExtractPhonetic(string header)
    {
        var match = PhoneticRegex().Match(header);
        return match.Success && !ContainsChinese(match.Groups[1].Value) ? match.Groups[1].Value.Trim() : "";
    }

    private static string ExtractMeaning(List<string> lines, string term, string phonetic)
    {
        var header = lines[0][Math.Min(term.Length, lines[0].Length)..].Trim(' ', '-', '—', ':', '：');
        if (!string.IsNullOrWhiteSpace(phonetic))
            header = header.Replace($"/{phonetic}/", "").Replace($"[{phonetic}]", "").Trim();
        var marked = lines.FirstOrDefault(x => MeaningMarkerRegex().IsMatch(x));
        var value = marked is null ? header : MeaningMarkerRegex().Replace(marked, "").Trim();
        if (!ContainsChinese(value))
            value = lines.Skip(1).FirstOrDefault(x => ContainsChinese(x) && !PhraseMarkerRegex().IsMatch(x) && !ExampleMarkerRegex().IsMatch(x)) ?? "";
        return ContainsChinese(value) ? Limit(value, 360) : "";
    }

    private static List<string> ExtractPhrases(List<string> lines, string term)
    {
        var results = new List<string>();
        foreach (var line in lines)
        {
            var marker = PhraseMarkerRegex().Match(line);
            if (marker.Success)
                results.AddRange(SplitUseful(marker.Groups[1].Value));
            else
                results.AddRange(line.Split(['；', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => x.Contains(term, StringComparison.OrdinalIgnoreCase) && EnglishWordRegex().Matches(x).Count is >= 2 and <= 9 && x.Length <= 100));
        }
        return results.Select(x => Limit(x, 100)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
    }

    private static string ExtractExample(List<string> lines, string term)
    {
        foreach (var line in lines)
        {
            var marker = ExampleMarkerRegex().Match(line);
            if (marker.Success && EnglishWordRegex().Matches(marker.Groups[1].Value).Count >= 4) return Limit(marker.Groups[1].Value.Trim(), 260);
        }
        return lines.Where(x => x.Contains(term, StringComparison.OrdinalIgnoreCase) && EnglishWordRegex().Matches(x).Count >= 6 && x.Length <= 300)
            .OrderByDescending(x => x.Count(c => c is '.' or '?' or '!')).FirstOrDefault() ?? "";
    }

    private static IEnumerable<string> SplitUseful(string value) => value.Split(['；', ';', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length is >= 3 and <= 100);
    private static bool ContainsChinese(string value) => value.Any(c => c is >= '\u4e00' and <= '\u9fff');
    private static string FirstUseful(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static string Slug(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    [GeneratedRegex(@"\s+")] private static partial Regex SpaceRegex();
    [GeneratedRegex(@"^\s*(?:/|\[|(?:n|v|vt|vi|adj|adv|prep|conj|pron|num|a|ad)\.|名词|动词|形容词|副词|[\u4e00-\u9fff])", RegexOptions.IgnoreCase)] private static partial Regex HeaderDetailRegex();
    [GeneratedRegex(@"^([A-Za-z][A-Za-z'-]{1,28})(?:\s+|\s*[/\[]).*(?:[\u4e00-\u9fff]|\b(?:n|v|vt|vi|adj|adv|prep|conj|pron|num)\.)", RegexOptions.IgnoreCase)] private static partial Regex NewEntryRegex();
    [GeneratedRegex(@"(?:/|\[)([^/\]\r\n]{2,60})(?:/|\])")] private static partial Regex PhoneticRegex();
    [GeneratedRegex(@"^(?:释义|意思|词义)\s*[：:]\s*", RegexOptions.IgnoreCase)] private static partial Regex MeaningMarkerRegex();
    [GeneratedRegex(@"(?:词组|短语|搭配|用法|固定搭配)\s*[：:]\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex PhraseMarkerRegex();
    [GeneratedRegex(@"(?:例句|例)\s*[：:]\s*(.+)$", RegexOptions.IgnoreCase)] private static partial Regex ExampleMarkerRegex();
    [GeneratedRegex(@"[A-Za-z]+(?:'[A-Za-z]+)?")] private static partial Regex EnglishWordRegex();
}
