using System.Reflection;
using System.Text.Json;
using WordCoach.Core;

namespace WordCoach.App.Services;

public static class ExtensionVocabulary
{
    public const string ScopeId = "curated-extension-2026-09";
    public const string ScopeTitle = "2026-09 精选拓展候选（16 条）";
    public static IReadOnlyList<VocabularyWord> Merge(IReadOnlyList<VocabularyWord> words)
    {
        var result = words.ToList();
        // The historic corpus remains searchable, but its token counts never imply unfamiliarity.
        foreach (var word in result) word.RecommendationEligible = false;
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("extension-vocabulary.json", StringComparison.Ordinal));
        if (resource is null) return result;
        using var stream = assembly.GetManifestResourceStream(resource);
        var extension = JsonSerializer.Deserialize<List<VocabularyWord>>(stream!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        foreach (var item in extension)
        {
            var index = result.FindIndex(w => w.Id == item.Id);
            // A phrase or a new sense cannot inherit a base-word identity merely
            // because its printed text happens to match. Legacy Word entries
            // retain IDs only for the same lexical teaching entry.
            if (index < 0 && item.EntryKind == "Word")
                index = result.FindIndex(w => w.EntryKind == "Word" && w.ScopeId == "legacy-corpus-v1"
                    && string.Equals(w.Term, item.Term, StringComparison.Ordinal));
            if (index >= 0)
            {
                item.Id = result[index].Id;
                if (string.IsNullOrWhiteSpace(item.Phonetic)) item.Phonetic = result[index].Phonetic;
                result[index] = item;
            }
            else result.Add(item);
        }
        return result;
    }
}
