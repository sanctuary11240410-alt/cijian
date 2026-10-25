"""Validate every curated entry against locally downloaded exam PDF text.
Exams are research inputs only; never bundled or used as a purported frequency corpus.
"""
import csv, json, re, hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RESEARCH = ROOT.parent / 'corpus-research'
VARIANTS = {
    'round out':['rounding out'], 'dream up':['dreamed up'], 'roll out':['roll it out'],
    'align':['aligned'], 'overturn':['overturns'], 'inconsistency':['inconsistencies'],
    'bite back':['bit back'], "ahead of one's time":['ahead of my time'],
    'multitask':['multitask'], 'outnumber':['outnumber'], 'specimen':['specimens'],
    'dataset':['dataset','datasets'], 'differentiate':['differentiating'],
    "cast one's eye over":['casting your eye over'], 'demotivate':['demotivated'],
    'retrace':['retracing'], 'fish out':['fished out'], 'wash over':['washed over'], 'thin out':['thinning out'],
}
texts={}
for year in ('2022','2023','2024'):
    texts[year]=re.sub(r'\s+',' ',(RESEARCH/f'exam-{year}.txt').read_text(encoding='utf-8')).lower().replace('’',"'").replace('“','"').replace('”','"')
results=[]
with (ROOT/'tools'/'extension-entries.tsv').open(encoding='utf-8',newline='') as f:
    for row in csv.DictReader(f,delimiter='\t'):
        forms=[row['term']]+VARIANTS.get(row['term'],[])
        matched=[form for form in forms if re.search(r'(?<![a-z])'+re.escape(form)+r'(?![a-z])',texts[row['year']])]
        results.append({'term':row['term'],'year':row['year'],'section':row['section'],'matchedForms':matched,'verified':bool(matched)})
catalog = json.loads((ROOT/'src/WordCoach.App/Assets/extension-vocabulary.json').read_text(encoding='utf-8'))
by_term = {entry['Term']: entry for entry in catalog}
for result in results:
    entry = by_term[result['term']]
    result.update({'recommendationEligible': entry['RecommendationEligible'], 'scopeId': entry['ScopeId'],
                   'decision': entry['RecommendationReason'], 'priority': entry['RecommendationPriority']})
assert len(catalog) == len(results) == 50
assert all(entry['RecommendationPriority'] == (60 if entry['RecommendationEligible'] else 0) for entry in catalog)
assert not by_term['declutter']['RecommendationEligible']
assert not by_term['reschedule']['RecommendationEligible']
assert all(not by_term[t]['RecommendationEligible'] for t in ['holistic', 'dataset', 'thesaurus', 'blemished'])
assert all('不因猜词题加分' in by_term[t]['RecommendationReason'] for t in ['embark on', 'shine through'])
assert all(entry['ScopeId'] == ('curated-extension-2026-09' if entry['RecommendationEligible'] else 'reference-extension-2026-09') for entry in catalog)
audit={'retrievedOn':'2026-09-07','reviewedOn':'2026-09-12','entryCount':len(results),
       'curatedCount':sum(e['RecommendationEligible'] for e in catalog),
       'allAttested':all(x['verified'] for x in results),
       'method':'Whole word / explicitly listed inflection search in actual exam PDF text; section reviewed separately. No cross-year frequency claimed.',
       'inputs':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in RESEARCH.glob('exam-*.pdf')},'entries':results}
(RESEARCH/'exam-attestation.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'entries':len(results),'allAttested':audit['allAttested'],'missing':[x['term'] for x in results if not x['verified']]},ensure_ascii=False))
if not audit['allAttested']: raise SystemExit(1)
