using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace WordCoach.App.Services;

public sealed record DictionaryEntry(string Term, string Phonetic, string Meaning,
    string EnglishDefinition, string Exchange, string Source);

/// <summary>Read-only offline dictionary, backed by the SQLite library shipped with Windows.</summary>
public sealed class OfflineDictionary : IDisposable
{
    private IntPtr _db;
    private readonly object _gate = new();
    private const string Columns = "e.term,e.phonetic,e.meaning,e.definition,e.exchange";
    public bool Available => _db != IntPtr.Zero;
    public int EntryCount { get; private set; }

    public OfflineDictionary(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "Assets", "dictionary.db");
        if (!File.Exists(path)) return;
        var result = Native.sqlite3_open_v2(Utf8(path), out _db, 1 | 0x10000, IntPtr.Zero);
        if (result != 0) { var message = Error(); Dispose(); throw new InvalidDataException(message); }
        try
        {
            var rows = Query("SELECT value FROM metadata WHERE key='entry_count'");
            if (rows.Count == 0 || !int.TryParse(rows[0][0], out var count) || count <= 0)
                throw new InvalidDataException("词典文件缺少有效的词条数量记录。");
            EntryCount = count;
        }
        catch { Dispose(); throw; }
    }

    public DictionaryEntry? Lookup(string text)
    {
        var query = Normalize(text);
        if (!Available || query.Length is 0 or > 200) return null;
        lock (_gate)
        {
            var exact = Entries($"SELECT {Columns} FROM entries e WHERE e.norm=? ORDER BY e.rank,e.id LIMIT 1", query);
            if (exact.Count > 0) return exact[0];
            return Entries($"SELECT {Columns} FROM aliases a JOIN entries e ON e.id=a.entry_id WHERE a.alias=? ORDER BY e.rank,e.id LIMIT 1", query).FirstOrDefault();
        }
    }

    public IReadOnlyList<DictionaryEntry> Search(string text, int limit = 30)
    {
        var query = Normalize(text);
        if (!Available || query.Length is 0 or > 200) return [];
        limit = Math.Clamp(limit, 1, 100);
        lock (_gate)
        {
            var found = new List<DictionaryEntry>();
            var exact = Lookup(query);
            if (exact is not null) found.Add(exact);
            List<DictionaryEntry> matches;
            if (query.Any(c => c >= '\u3400' && c <= '\u9fff'))
                matches = Entries($"SELECT {Columns} FROM entries e WHERE instr(e.meaning,?)>0 LIMIT {limit}", query);
            else
                matches = Entries($"SELECT {Columns} FROM entries e WHERE e.norm>=? AND e.norm<? ORDER BY e.norm LIMIT {limit}", query, query + '\uffff');
            found.AddRange(matches);
            return found.DistinctBy(e => e.Term, StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
        }
    }

    private List<DictionaryEntry> Entries(string sql, params string[] parameters) => Query(sql, parameters)
        .Select(r => new DictionaryEntry(r[0], r[1], r[2], r[3], FormatExchange(r[4]), "ECDICT 开源离线词典 · MIT")).ToList();

    private static string FormatExchange(string exchange)
    {
        var labels = new Dictionary<string, string>
        {
            ["p"] = "过去式", ["d"] = "过去分词", ["i"] = "现在分词", ["3"] = "第三人称单数",
            ["r"] = "比较级", ["t"] = "最高级", ["s"] = "复数", ["0"] = "原形"
        };
        return string.Join("；", exchange.Split('/').Select(part => part.Split(':', 2))
            .Where(pair => pair.Length == 2 && labels.ContainsKey(pair[0]) && !string.IsNullOrWhiteSpace(pair[1]))
            .Select(pair => labels[pair[0]] + " " + pair[1]));
    }

    private List<string[]> Query(string sql, params string[] parameters)
    {
        if (!Available) return [];
        var bytes = Utf8(sql);
        Check(Native.sqlite3_prepare_v2(_db, bytes, bytes.Length, out var statement, IntPtr.Zero));
        try
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var value = Utf8(parameters[i]);
                Check(Native.sqlite3_bind_text(statement, i + 1, value, value.Length - 1, new IntPtr(-1)));
            }
            var rows = new List<string[]>();
            int result;
            while ((result = Native.sqlite3_step(statement)) == 100)
            {
                var row = new string[Native.sqlite3_column_count(statement)];
                for (var column = 0; column < row.Length; column++)
                    row[column] = Marshal.PtrToStringUTF8(Native.sqlite3_column_text(statement, column)) ?? "";
                rows.Add(row);
            }
            if (result != 101) Check(result);
            return rows;
        }
        finally { Native.sqlite3_finalize(statement); }
    }

    private void Check(int result) { if (result != 0) throw new InvalidDataException("词典查询失败：" + Error()); }
    private string Error() => _db == IntPtr.Zero ? "无法打开词典文件。" : Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(_db)) ?? "SQLite 错误";
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + '\0');
    private static string Normalize(string text) => Regex.Replace((text ?? "").Trim().Replace('’', '\''), @"\s+", " ").ToLowerInvariant();
    public void Dispose()
    {
        lock (_gate) { if (_db != IntPtr.Zero) { Native.sqlite3_close_v2(_db); _db = IntPtr.Zero; } }
        GC.SuppressFinalize(this);
    }

    private static class Native
    {
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] value, int length, IntPtr destructor);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_count(IntPtr statement);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    }
}
