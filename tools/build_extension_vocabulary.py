"""Build the small, curated exam extension set. Never manufacture a Victor 1500 list.

The companion TSV contains independently written definitions, collocations and examples.
Textbook coverage is an audit result, not a claim that the learner knows an entry.
"""
import csv
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RESEARCH = ROOT.parent / 'corpus-research'
SOURCES = {
    '2022': 'https://m.jszs.com/uploads/file/202312/06/1701849185494197.pdf',
    '2023': 'https://cdn.zizzs.com/zixunzhan/202310/ad8ab2c7-82fe-465f-8c40-f4619572b696.pdf',
    '2024': 'https://static-gkcx.gaokao.cn/upload/zhenti/20240612/1718156490_7990.pdf',
}
# British IPA, independently transcribed for these teaching entries.
IPA = {
    'mindful':'ˈmaɪndfəl', 'jaw-dropping':'ˈdʒɔːˌdrɒpɪŋ', 'blemished':'ˈblemɪʃt',
    'ground-breaking':'ˈɡraʊndˌbreɪkɪŋ', 'align':'əˈlaɪn', 'overturn':'ˌəʊvəˈtɜːn',
    'interplay':'ˈɪntəpleɪ', 'inconsistency':'ˌɪnkənˈsɪstənsi', 'connectivity':'ˌkɒnekˈtɪvəti',
    'minimalism':'ˈmɪnɪməlɪzəm', 'intolerable':'ɪnˈtɒlərəbəl', 'declutter':'ˌdiːˈklʌtə',
    'solitude':'ˈsɒlɪtjuːd', 'mindless':'ˈmaɪndləs', 'reschedule':'ˌriːˈʃedjuːl',
    'guardian':'ˈɡɑːdiən', 'sunscreen':'ˈsʌnskriːn', 'holistic':'həʊˈlɪstɪk',
    'multitask':'ˌmʌltiˈtɑːsk', 'abstraction':'æbˈstrækʃən', 'outnumber':'ˌaʊtˈnʌmbə',
    'specimen':'ˈspesɪmən', 'dataset':'ˈdeɪtəset', 'eye-catching':'ˈaɪˌkætʃɪŋ',
    'manuscript':'ˈmænjʊskrɪpt', 'thesaurus':'θɪˈsɔːrəs', 'differentiate':'ˌdɪfəˈrenʃieɪt',
    'idiomatic':'ˌɪdiəˈmætɪk', 'demotivate':'ˌdiːˈməʊtɪveɪt', 'retrace':'rɪˈtreɪs',
    'apologetic':'əˌpɒləˈdʒetɪk',
}
# Explicit editorial decisions. A question asking readers to infer a meaning is
# evidence of appearance only; it never adds recommendation weight.
CURATED = {
    'mindful': 'be mindful of 可迁移到时间、资源和行为提醒，学习固定搭配的阅读义。',
    'down the road': '作为将来时间标记可跨话题使用；和字面沿路义分开。',
    'dream up': '表示想出或构想，可用于计划、方法和故事创意。',
    'roll out': '表示推出并推广服务或方案，可用于社会、科技和生活文章。',
    'embark on': '表示着手新活动，搭配项目、学习和旅程；入选根据独立词典用法，不因猜词题加分。',
    'overturn': '可与决定、解释和既有观点搭配，帮助理解论证中的转折。',
    'make the case for': '概括论证支持关系，能帮助识别作者的主张和理由。',
    'act on': '表达把建议或信息转成行动的关系，适合多种阅读话题。',
    'guardian': '报名、活动说明中的监护人义具有独立生活阅读价值。',
    'rain or shine': '公告与活动说明中表示不受天气影响，需要整体识别。',
    "ahead of one's time": '评价思想或设计超前，整体含义不是简单的时间先后。',
    'shine through': '形容品质或能力显露，独立词典确认该用法；不因猜词题加分。',
    'outnumber': '直接表达数量比较关系，适用于人群、调查和研究描述。',
    'differentiate': '表示区分概念或事物，可用于解释和比较；以阅读识义为目标。',
    'leaf through': '描述翻阅材料的方式，学习整体动词短语。',
    'thin out': '描述人群、数量或密度逐渐减少，可在不同叙事中复用。',
}
REFERENCE = {
    'jaw-dropping': '形象化复合词；当前仅有单篇数量描述，暂不作为默认背诵项。',
    'blemished': '原卷已有中文注释；未见额外跨语境入选依据，保留查阅。',
    'ground-breaking': '译林教材已检出，且是可解释的复合词，默认不再推荐。',
    'align': '原卷已注中文；该次牙齿排列语境不足以证明必须单独记忆。',
    'interplay': '当前依据局限于一篇专业研究，不因单次出现扩张背诵范围。',
    'inconsistency': 'consistent 词族派生；先在词族解释中认识，需要时个人加入。',
    'connectivity': '译林教材已检出；连通性是具体语境中的派生概念。',
    'a host of': '译林教材已检出，作为个人缺口时可自行加入。',
    'bite back': '偏人物情绪叙事，当前跨语境证据不足，降为可选表达。',
    'minimalism': '文章书籍主题词，非默认必背范围。',
    'intolerable': 'tolerate/tolerable 词族变形；不把可分析的否定派生自动当作生词。',
    'declutter': '删除原来的猜词题提权；该次数字生活主题不足以支持默认入选。',
    'solitude': '原卷已有中文注释，暂留查阅和个人主动积累。',
    'mindless': 'mind + -less 的可分析派生；默认附在词族学习而不增加新词负担。',
    'reschedule': '当前证据只来自听力选项，且 re- 构词透明，不据此加入必背包。',
    'sunscreen': '活动物品清单中的具体生活物品，按个人实际缺口选学。',
    'holistic': '当前出现集中在专业理念介绍，不扩大为默认必背项。',
    'multitask': 'multi- + task 的可分析构词，适合语境解释，不自动独立排课。',
    'abstraction': 'abstract 词族的抽象名词，先随原词解释，需要时再单独练习。',
    'specimen': '译林教材已检出且原卷有注释，默认不重复推荐。',
    'dataset': '研究语境下的透明复合词 data + set，默认只查阅。',
    'eye-catching': '译林教材已检出，默认不重复推荐。',
    'manuscript': '原卷给中文注释且场景偏出版，暂列可选。',
    'thesaurus': '专门工具名称，原卷有说明；无需把所有出版用语纳入背诵。',
    'idiomatic': '主要用于语言学习说明，当前范围内优先级不足。',
    "cast one's eye over": '与浏览类表达重叠，优先保留 leaf through，避免一次塞入多个近义短语。',
    'demotivate': 'de- + motivate 派生，先解释词族而非视为独立未知词。',
    'retrace': '原卷已有注释且 re- 构词可分析，默认不排入。',
    'apologetic': 'apology/apologise 词族形容词，先随词族理解，可自行加入写作训练。',
    'fish out': '偏人物动作描写，当前单篇证据不足，保留可选。',
    'wash over': '偏情绪叙事的比喻表达，暂不占用默认阅读缺口学习量。',
    'round out': '补充完整义可用，但目前只核对单篇，先降低候选规模、保留查阅。',
    'draw on': '译林教材已检出，作为个人缺口时可自行加入。',
    'have the last laugh': '叙事习语可选；当前证据不足以放入默认精选。',
}
LEXICAL_SOURCES = {
    'embark on': 'https://www.oxfordlearnersdictionaries.com/us/definition/english/embark-on',
    'shine through': 'https://dictionary.cambridge.org/dictionary/english/shine-through',
    'roll out': 'https://dictionary.cambridge.org/dictionary/english/roll-out',
    'make the case for': 'https://dictionary.cambridge.org/us/dictionary/english/make-a-case-for',
    'overturn': 'https://www.oxfordlearnersdictionaries.com/external/pdf/wordlists/oxford-3000-5000/American_Oxford_5000_by_CEFR_level.pdf',
    'differentiate': 'https://www.oxfordlearnersdictionaries.com/external/pdf/wordlists/oxford-3000-5000/American_Oxford_5000_by_CEFR_level.pdf',
}

def build():
    audit_path = RESEARCH / 'extension-coverage.json'
    audit = json.loads(audit_path.read_text(encoding='utf-8-sig')) if audit_path.exists() else {}
    entries = []
    with (ROOT / 'tools' / 'extension-entries.tsv').open(encoding='utf-8-sig', newline='') as f:
        for index, row in enumerate(csv.DictReader(f, delimiter='\t')):
            term = row['term']
            coverage = audit.get(term, {})
            hits = coverage.get('hits', [])
            checked = coverage.get('checkedBooks', 0)
            kind = 'Sense' if term == 'down the road' else row['kind']
            sense_extension = term == 'down the road' and bool(hits)
            if sense_extension:
                label = '教材检出字面义沿路；本条学习比喻义将来，单独安排'
            elif hits:
                label = '译林教材已检出：' + '、'.join(hits[:3])
            elif checked == 7:
                label = '七册译林教材文字检索未检出；含常见词形，非人工逐页认定'
            else:
                label = '译林教材覆盖待核对；暂列真题拓展候选'
            assert term in CURATED or term in REFERENCE, f'Missing explicit review: {term}'
            recommended = term in CURATED and (not hits or sense_extension)
            evidence = f"{row['year']}年新{'高考' if row['year']=='2022' else '课标'}Ⅰ卷 · {row['section']}"
            # Editorial utility, independent of unknownness or raw token rarity.
            priority = 60 if recommended else 0
            utility = CURATED.get(term) or REFERENCE[term]
            entries.append({
                'Id':'ext-' + hashlib.sha256(term.encode()).hexdigest()[:16],
                'ScopeId': 'curated-extension-2026-09' if recommended else 'reference-extension-2026-09',
                'LemmaId': 'lemma-' + term, 'Aliases': [],
                'Term':term, 'Phonetic':IPA.get(term, ''), 'Meaning':row['meaning'],
                'Phrases':[p.strip() for p in row['phrases'].split('|')],
                'Example': '自编例句：' + row['example'],
                'SourceExcerpt': f"出现依据：{evidence}。{row['note']}\n释义、搭配和例句为软件独立编写，并非原卷整句摘录。",
                'ExamFrequency':0, 'NecessityScore':0, 'Reasons':[],
                'Source':'2026-09 精选审核 · ' + evidence,
                'IsFoundation':False,
                'RecommendationEligible':recommended,
                'RecommendationReason': ('精选候选：' if recommended else '仅查阅/自选：') + utility + '这是编辑取舍，未证明高考跨年高频，也不表示你不会。',
                'RecommendationPriority':priority,
                'Coverage':label, 'EntryKind':kind,
                'EvidenceSources':[SOURCES[row['year']]] + ([LEXICAL_SOURCES[term]] if term in LEXICAL_SOURCES else []),
            })
    assert len({x['Term'] for x in entries}) == len(entries)
    assert all(x['Meaning'] and len(x['Phrases'])>=2 and x['Example'] and x['EvidenceSources'] for x in entries)
    output = ROOT / 'src' / 'WordCoach.App' / 'Assets' / 'extension-vocabulary.json'
    output.write_text(json.dumps(entries, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f"Built {len(entries)} entries; {sum(x['RecommendationEligible'] for x in entries)} recommended; {sum(x['EntryKind']=='Phrase' for x in entries)} phrases.")

if __name__ == '__main__':
    build()
