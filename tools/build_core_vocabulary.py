"""Extract the 2020 curriculum's 3,000 printed entries, with explicit aliases.

The statutory appendix supplies membership and spelling only. ECDICT supplies
dictionary glosses/IPA (MIT); existing teaching examples keep their own source.
No guessed inflection/suffix stripping or generated definitions are used.
"""
import hashlib
import json
import re
import sqlite3
from pathlib import Path
from pypdf import PdfReader

ROOT = Path(__file__).resolve().parents[1]
RESEARCH = ROOT.parent / 'corpus-research'
ASSETS = ROOT / 'src/WordCoach.App/Assets'
SCOPE = 'curriculum-2020-3000'
SOURCE = 'https://www.pep.com.cn/xw/zt/rjwy/gzkb2020/202205/P020220517522153664167.pdf'


def norm(s):
    return re.sub(r'\s+', ' ', s.replace('’', "'").strip()).lower()


def printed_entries():
    reader = PdfReader(RESEARCH / 'curriculum-2020.pdf')
    text = '\n'.join(page.extract_text() for page in reader.pages[128:184])
    text = text.split('A\n', 1)[1].split('主要国家名称及相关信息', 1)[0]
    lines = [line.strip() for line in text.splitlines() if re.match('[A-Za-z]', line.strip())]
    result, removed_headers, pending = [], set(), ''
    for line in lines:
        if len(line) == 1 and line.isupper() and line not in removed_headers:
            removed_headers.add(line)
            continue
        line = pending + line
        if line.count('(') > line.count(')'):
            pending = line + ' '
            continue
        pending = ''
        result.append(line)
    assert not pending
    assert len(result) == 3000, f'Unexpected appendix count: {len(result)}'
    assert len(removed_headers) == 25
    return result


def parse_entry(raw):
    stars = raw.count('*')
    label = raw.replace('*', '').strip()
    # A single line may be a spelling alternative, irregular plural, or a paired
    # gender form. The printed entry remains one curriculum unit.
    bracket = re.search(r'\(([^)]+)\)', label)
    alias = []
    if bracket:
        content = bracket.group(1).replace('pl. ', '')
        if content == 's' and label.startswith('toward'):
            alias = ['towards']
        else:
            alias = [norm(s) for s in content.split(',')]
        label = re.sub(r'\s*\([^)]+\)', '', label).strip()
    slash = [p.strip() for p in label.split('/')]
    if len(slash) > 1:
        label, extra = slash[0], slash[1:]
        alias.extend(norm(s) for s in extra)
    term = norm(label)
    assert re.fullmatch(r"[a-zà-ÿ]+(?:[ .'\-][a-zà-ÿ]+)*\.?", term), (raw, term)
    return term, sorted(set(alias) - {term}), stars


def display_term(raw):
    return re.sub(r'\s*\([^)]+\)', '', raw.replace('*', '')).split('/')[0].strip()


def dictionary_gloss(value):
    value = (value or '').replace('\\r\\n', '\n').replace('\\n', '\n').replace('\\r', '\n')
    lines = [s.strip() for s in value.splitlines() if s.strip()]
    # Specialized dictionary labels are reference material, not default lessons.
    lines = [s for s in lines if not s.startswith('[')]
    return '\n'.join(lines[:4]).strip()


def teaching_phrases(values):
    result = []
    for phrase in values:
        # The old general dictionary mixed specialist collocations into lessons.
        # Keep a short general gloss; leave specialist content in the dictionary.
        if re.search(r'\[(?:医|计|机|化|法)\]|钻井|钻机|镗床|螺纹|焊接|催化活性', phrase):
            continue
        short = re.split('[；;]', phrase, maxsplit=1)[0].strip()
        if short: result.append(short)
    return result[:3]


def build():
    old = json.loads((ASSETS / 'vocabulary.json').read_text(encoding='utf-8-sig'))
    old_by_term = {norm(x['term']): {k[0].upper()+k[1:]: v for k, v in x.items()} for x in old}
    db = sqlite3.connect(f'file:{(ASSETS / "dictionary.db").as_posix()}?mode=ro', uri=True)
    db.row_factory = sqlite3.Row
    entries, missing_gloss, missing_ipa = [], [], []
    overrides = {
        'a': ('eɪ; ə', 'art. 一个；某一（用于单数可数名词前；元音音素前用 an）'),
        'ad': ('ˌeɪ ˈdiː', 'abbr. 公元（拉丁语 Anno Domini 的缩写）'),
        'bc': ('ˌbiː ˈsiː', 'abbr. 公元前（before Christ）'),
        'pe': ('ˌpiː ˈiː', 'abbr. 体育；体育课（physical education）'),
        'a.m.': ('ˌeɪ ˈem', 'abbr. 上午；午前（午夜至中午之间）'),
        'p.m.': ('ˌpiː ˈem', 'abbr. 下午；午后（中午至午夜之间）'),
        'i': ('aɪ', 'pron. 我（主格，书写时大写 I）'),
        'café': ('ˈkæfeɪ', 'n. 咖啡馆；供应饮料和简餐的小餐馆'),
        'laptop': ('ˈlæptɒp', 'n. 笔记本电脑；便携式电脑'),
    }
    homographs = {
        'China': ('ˈtʃaɪnə', 'n. 中国'), 'china': ('ˈtʃaɪnə', 'n. 瓷；瓷器'),
        'March': ('mɑːtʃ', 'n. 三月'), 'march': ('mɑːtʃ', 'v. 行进；行军；游行\nn. 行军；游行；进行曲'),
        'May': ('meɪ', 'n. 五月'), 'may': ('meɪ', 'modal v. 可以（表示许可）；可能（表示可能性）；但愿（表示祝愿）'),
        'Miss': ('mɪs', 'n. 小姐（用在未婚女子姓名前的称呼）'), 'miss': ('mɪs', 'v. 错过；未击中；想念；未察觉'),
        'US': ('ˌjuː ˈes', 'abbr. 美国（United States）'), 'us': ('ʌs; əs', 'pron. 我们（宾格）'),
    }
    supplemental_ipa = {
        'according to': ('əˈkɔːdɪŋ ˌtuː', 'https://dictionary.cambridge.org/us/dictionary/english/according-to'),
        'bean curd': ('ˈbiːn ˌkɜːd', 'https://dictionary.cambridge.org/us/pronunciation/english/bean-curd'),
        'blog': ('blɒɡ', 'https://dictionary.cambridge.org/us/pronunciation/english/blog'),
        'ought to': ('ˈɔːt ˌtə', 'https://dictionary.cambridge.org/pronunciation/english/ought-to'),
        'website': ('ˈwebsaɪt', 'https://dictionary.cambridge.org/us/pronunciation/english/website'),
        # Phrase-level transcription combines the standard component pronunciations.
        'due to': ('ˈdjuː tə', 'https://dictionary.cambridge.org/dictionary/english/due'),
    }
    printed = printed_entries()
    for raw in printed:
        term, aliases, level = parse_entry(raw)
        display = display_term(raw)
        found = db.execute('SELECT * FROM entries WHERE norm = ? ORDER BY term = ? DESC, rank LIMIT 1', (term, display)).fetchone()
        if found is None:
            for alias in aliases:
                found = db.execute('SELECT * FROM entries WHERE norm = ? ORDER BY rank LIMIT 1', (alias,)).fetchone()
                if found: break
        inherited = {} if display in homographs and display != term else old_by_term.get(term, {})
        phonetic = ((found['phonetic'] if found else '') or inherited.get('Phonetic', '')).strip()
        meaning = dictionary_gloss(found['meaning'] if found else '') or inherited.get('Meaning', '')
        if term in overrides: phonetic, meaning = overrides[term]
        if display in homographs: phonetic, meaning = homographs[display]
        if term in supplemental_ipa: phonetic = supplemental_ipa[term][0]
        if ' / ' in raw:
            paired = aliases[0]
            paired_row = db.execute('SELECT meaning FROM entries WHERE norm = ? ORDER BY rank LIMIT 1', (paired,)).fetchone()
            paired_meaning = dictionary_gloss(paired_row['meaning']) if paired_row else ''
            meaning = f'{display}: {meaning}\n{paired}: {paired_meaning}'
        if not meaning: missing_gloss.append(term)
        if not phonetic: missing_ipa.append(term)
        identifier = inherited.get('Id') or 'core-' + hashlib.sha256(display.encode()).hexdigest()[:16]
        entries.append({
            'Id': identifier, 'Term': display,
            'LemmaId': 'lemma-' + term, 'Aliases': aliases, 'ScopeId': SCOPE,
            'Phonetic': phonetic, 'Meaning': meaning,
            'Phrases': teaching_phrases(inherited.get('Phrases', [])), 'Example': inherited.get('Example', ''),
            'SourceExcerpt': f'课标原条目：{raw}\n课标只提供词表范围，未提供中文释义。释义/音标主要来自 ECDICT 离线词典，缩写与大小写异义另行订正；已有搭配和例句沿用 HK-SHAO/English-Dictionary（Apache-2.0；未逐条完成教学审校）。' + ('\n缺失音标按词典核对后补记；due to 按组成词读音标注常见弱读。' if term in supplemental_ipa else ''),
            'ExamFrequency': 0, 'NecessityScore': 0, 'Reasons': [],
            'Source': '2020 修订版课程标准附录 2；ECDICT（MIT）',
            'IsFoundation': level == 0, 'RecommendationEligible': False,
            'RecommendationReason': '核心词表范围；按个人未知清单学习，课标收录不表示你不会。',
            'RecommendationPriority': 0,
            'Coverage': ['课标内 · 义务教育基础', '课标内 · 高中必修', '课标内 · 选择性必修'][level],
            'EntryKind': 'Word', 'EvidenceSources': [SOURCE, 'https://github.com/skywind3000/ECDICT'] + (['https://github.com/HK-SHAO/English-Dictionary'] if inherited else []) + ([f'https://dictionary.cambridge.org/dictionary/english/{"cafe" if term == "café" else term}'] if term in {'café', 'laptop'} else []) + ([supplemental_ipa[term][1]] if term in supplemental_ipa else []),
        })
    db.close()
    assert len({e['Id'] for e in entries}) == 3000
    assert len({e['Term'] for e in entries}) == 3000
    assert not missing_gloss, missing_gloss
    out = ASSETS / 'core-vocabulary.json'
    out.write_text(json.dumps(entries, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    metadata = {
        'ScopeId': SCOPE, 'Title': '2020 修订版课标 3000 条', 'IsComplete': True,
        'EntryCount': len(entries), 'AliasCount': sum(len(e['Aliases']) for e in entries),
        'LevelCounts': {str(n): sum(parse_entry(raw)[2] == n for raw in printed) for n in range(3)},
        'CaseInsensitiveHeadwordCount': len({norm(e['Term']) for e in entries}),
        'OfficialPrintedEntries': 3000, 'Source': SOURCE,
        'SourcePdfSha256': hashlib.sha256((RESEARCH / 'curriculum-2020.pdf').read_bytes()).hexdigest(),
        'GeneratedJsonSha256': hashlib.sha256(out.read_bytes()).hexdigest(),
        'MissingPhonetics': missing_ipa,
        'Limitations': '完整指附录正文 3000 条范围与明确别名，不是全部义项、搭配和例句均经教学审校。官方并列括注仍按一个条目计；国名参考表不另算。没有自动转成 2025 修订版或宣称某届江苏高考实施范围。',
    }
    (ASSETS / 'core-scope.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(metadata, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    build()
