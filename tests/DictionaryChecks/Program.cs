using System.Diagnostics;
using WordCoach.App.Services;

var path = args.Length > 0 ? args[0] : Path.GetFullPath("src/WordCoach.App/Assets/dictionary.db");
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    passed++; Console.WriteLine("PASS " + name);
}
using (var absent = new OfflineDictionary(path + ".missing"))
    Check(!absent.Available && absent.Search("test").Count == 0, "missing dictionary degrades without crash");
using var dictionary = new OfflineDictionary(path);
Check(dictionary.Available && dictionary.EntryCount > 500000, "substantial offline dictionary opens through native Windows SQLite");
var apple = dictionary.Lookup("  APPLE  ");
Check(apple is not null && apple.Term.Equals("apple", StringComparison.OrdinalIgnoreCase) && apple.Meaning.Contains("苹果"), "case-insensitive exact lookup returns Chinese meaning");
Check(dictionary.Lookup("photosynthesis")?.Meaning.Length > 0 && dictionary.Lookup("serendipity")?.Meaning.Length > 0, "terms outside the supplied school catalog are available");
var inflection = dictionary.Lookup("actioned");
Check(inflection?.Term.Equals("action", StringComparison.OrdinalIgnoreCase) == true && inflection.Exchange.Contains("过去式"),
    "a recorded inflection without an exact entry resolves to its base form with readable morphology");
var prefix = dictionary.Search("photosyn", 8);
Check(prefix.Count is > 0 and <= 8 && prefix.Any(e => e.Term.Equals("photosynthesis", StringComparison.OrdinalIgnoreCase)), "English prefix lookup is indexed and bounded");
var time = Stopwatch.StartNew();
var chinese = dictionary.Search("光合作用", 10);
Console.WriteLine("Chinese search elapsed ms=" + time.ElapsedMilliseconds);
Check(chinese.Count > 0 && chinese.All(e => e.Meaning.Contains("光合作用")), "Chinese definitions are searchable");
Check(dictionary.Lookup("zzzznotarealwordqqq") is null, "unknown term is not fabricated");
Check(dictionary.Search("%'; DROP TABLE entries;--").Count == 0 && dictionary.Lookup("apple") is not null, "search input is bound as text and cannot execute SQL");
Console.WriteLine($"PASS {passed} dictionary checks; entries={dictionary.EntryCount:N0}");
