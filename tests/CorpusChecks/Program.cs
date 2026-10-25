using System.Reflection;
using System.Text.Json;
using WordCoach.App.Services;
using WordCoach.Core;

var assembly = Assembly.GetExecutingAssembly();
using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(".vocabulary.json")));
var original = JsonSerializer.Deserialize<List<VocabularyWord>>(stream!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var merged = ExtensionVocabulary.Merge(CoreVocabulary.Merge(original));
var core = merged.Where(w => w.ScopeId == CoreVocabulary.ScopeId).ToList();
var selected = merged.Where(w => w.ScopeId == ExtensionVocabulary.ScopeId).ToList();
var reference = merged.Where(w => w.ScopeId == "reference-extension-2026-09").ToList();
int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine("PASS " + description);
}

Check(core.Count == 3000, "The merged catalog retains exactly 3000 official curriculum entries.");
Check(merged.Select(w => w.Id).Distinct().Count() == merged.Count, "Every learning entry has a unique stable ID.");
Check(core.Select(w => w.Term).Distinct().Count() == 3000, "Official printed entries remain distinct.");
Check(core.Select(w => w.Term.ToLowerInvariant()).Distinct().Count() == 2995, "Five case homographs are counted transparently.");
Check(core.Sum(w => w.Aliases.Count) == 64, "Officially printed alternate forms are retained.");
Check(core.All(w => !string.IsNullOrWhiteSpace(w.Meaning)), "Every core entry has a dictionary meaning.");
Check(core.All(w => !string.IsNullOrWhiteSpace(w.Phonetic)), "Core IPA is present; dictionary gaps were checked separately.");
Check(selected.Count == 16 && selected.All(w => w.RecommendationEligible), "The selected extension scope is only the 16 reviewed candidates.");
Check(reference.Count == 34 && reference.All(w => !w.RecommendationEligible), "The 34 reference entries are outside the selected scope.");
Check(merged.Where(w => w.RecommendationEligible).All(w => w.ScopeId == ExtensionVocabulary.ScopeId), "Legacy frequencies and core membership do not auto-enroll unknown words.");
Check(reference.Any(w => w.Term == "declutter") && reference.Any(w => w.Term == "reschedule"), "A meaning-inference question or a single option alone cannot make a candidate.");
Check(selected.All(w => w.RecommendationPriority == 60), "There is no question-type score bonus.");
foreach (var pair in new[] { ("China", "china"), ("March", "march"), ("May", "may"), ("Miss", "miss"), ("US", "us") })
{
    var upper = core.Single(w => w.Term == pair.Item1);
    var lower = core.Single(w => w.Term == pair.Item2);
    Check(upper.Id != lower.Id && upper.Meaning != lower.Meaning, pair.Item1 + "/" + pair.Item2 + " preserve separate meanings and progress IDs.");
}
Check(original.All(w => merged.Any(n => n.Id == w.Id)), "All original catalog IDs remain available for existing progress.");
Check(selected.Single(w => w.Term == "down the road").EntryKind == "Sense", "The figurative future use remains an independent sense.");
Console.WriteLine($"Corpus checks passed: {checks}; catalog {merged.Count}, core {core.Count}, selected {selected.Count}, reference {reference.Count}.");
