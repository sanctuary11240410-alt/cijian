using System.IO;
using System.Reflection;
using System.Text.Json;
using WordCoach.Core;

namespace WordCoach.App.Services;

public static class CoreVocabulary
{
    public const string ScopeId = "curriculum-2020-3000";
    public const string ScopeTitle = "2020 修订版课标 3000 条";

    public static IReadOnlyList<VocabularyWord> Merge(IReadOnlyList<VocabularyWord> legacy)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().SingleOrDefault(x => x.EndsWith(".core-vocabulary.json", StringComparison.Ordinal));
        if (resource is null) throw new InvalidDataException("缺少有版本的核心词库，请使用完整程序文件夹。");
        using var stream = assembly.GetManifestResourceStream(resource);
        var core = JsonSerializer.Deserialize<List<VocabularyWord>>(stream!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (core.Count != 3000 || core.Select(w => w.Id).Distinct().Count() != 3000)
            throw new InvalidDataException("核心词库范围检查失败：应有 3000 个独立条目。");
        // IDs in the generated core catalog retain existing identities only when
        // they represent the same lexical entry. Case homographs remain separate.
        var coreIds = core.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var retained = legacy.Where(w => !coreIds.Contains(w.Id)).ToList();
        foreach (var word in retained)
        {
            word.ScopeId = "legacy-corpus-v1";
            word.LemmaId = "lemma-" + word.Term.Trim().ToLowerInvariant();
            word.RecommendationEligible = false;
            word.RecommendationReason = "旧资料条目仅供查阅；未因出现次数自动纳入背诵范围。";
        }
        return core.Concat(retained).ToList();
    }
}
