import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "frequency-source" / "english.txt"
OUTPUT = Path(__file__).resolve().parents[1] / "src" / "WordCoach.App" / "Assets" / "vocabulary.json"

EASY_WORDS = {
    "answer", "picture", "wrong", "space", "adult", "popular", "coming", "deep", "myself",
    "almost", "outside", "coffee", "center", "online", "clear", "whether", "ability", "fruit",
    "touch", "clean", "husband", "anything", "national", "event", "society", "development",
    "water", "family", "country", "world", "house", "money", "music", "story", "young",
    "morning", "night", "food", "health", "happy", "small", "large", "early", "later",
    "today", "tomorrow", "yesterday", "person", "problem", "question", "change", "reason",
    "language", "computer", "phone", "letter", "number", "different", "possible", "really",
    "always", "often", "sometimes", "never", "again", "together", "without", "during",
    "between", "around", "inside", "away", "already", "enough", "every", "another",
}


def field(block: str, start: str, end: str) -> str:
    match = re.search(re.escape(start) + r"(.*?)" + re.escape(end), block, re.S)
    return re.sub(r"\s+", " ", match.group(1)).strip() if match else ""


def clean_phrase_text(value: str) -> list[str]:
    if not value:
        return []
    parts = re.split(r"\s+(?=\d+\.\s)", value)
    cleaned = [re.sub(r"^\d+\.\s*", "", part).strip() for part in parts]
    return [part for part in cleaned if part][:4]


def compute_score(term: str, frequency: int, meaning: str, phrases: list[str]) -> tuple[int, list[str]]:
    letters = re.sub(r"[^a-z]", "", term.lower())
    length_score = min(22, max(0, len(letters) - 5) * 3)
    sense_count = meaning.count("；") + meaning.count("：")
    sense_score = min(24, sense_count * 4)
    phrase_score = min(18, len(phrases) * 5)
    frequency_score = min(30, round(math.log1p(frequency) / math.log(123) * 30))
    easy_penalty = 32 if term.lower() in EASY_WORDS else 0
    inflection_penalty = 12 if re.search(r"(ing|ed|est)$", term.lower()) else 0
    score = max(1, min(100, 18 + frequency_score + length_score + sense_score + phrase_score - easy_penalty - inflection_penalty))
    reasons = []
    if frequency >= 70:
        reasons.append("真题语料高频")
    elif frequency >= 30:
        reasons.append("真题语料常见")
    if sense_count >= 3:
        reasons.append("多义易漏")
    if len(phrases) >= 2:
        reasons.append("搭配值得检查")
    if len(letters) >= 9:
        reasons.append("词形较长")
    if not reasons:
        reasons.append("抽样校准")
    if easy_penalty:
        reasons = ["基础词默认跳过"]
    return score, reasons


def parse() -> list[dict]:
    text = SOURCE.read_text(encoding="utf-8")
    entries = []
    seen = set()
    for block in text.split("【【a1】】")[1:]:
        frequency_match = re.match(r"\s*(\d+)", block)
        head = field(block, "【【a3】】", "【【a4】】")
        term_match = re.match(r"([A-Za-z][A-Za-z .'-]*?)(?=\s+/)", head)
        if not frequency_match or not term_match:
            continue
        term = re.sub(r"\s+", " ", term_match.group(1)).strip().lower()
        if term in seen or " " in term:
            continue
        seen.add(term)
        phonetics = re.findall(r"/([^/]+?)/", head)
        meaning = field(block, "【【a5】】", "【【a6】】")
        phrase_text = field(block, "【【a8】】", "【【a9】】")
        phrases = clean_phrase_text(phrase_text)
        definition_text = field(block, "【【a10】】", "【【a11】】")
        example_match = re.search(r"例：\s*(.+?)(?:\s+(?:\S+定义：|\d+：)|$)", definition_text)
        example = example_match.group(1).strip() if example_match else ""
        frequency = int(frequency_match.group(1))
        score, reasons = compute_score(term, frequency, meaning, phrases)
        entries.append({
            "id": f"freq-{len(entries) + 1:04d}",
            "term": term,
            "phonetic": phonetics[0] if phonetics else "",
            "meaning": meaning,
            "phrases": phrases,
            "example": example,
            "examFrequency": frequency,
            "necessityScore": score,
            "reasons": reasons,
            "source": "HK-SHAO English-Dictionary 130+ papers corpus",
            "isFoundation": term in EASY_WORDS,
        })
    return entries


if __name__ == "__main__":
    words = parse()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(words, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"wrote {len(words)} words to {OUTPUT}")
    print(f"high necessity: {sum(1 for word in words if word['necessityScore'] >= 70)}")
    print(f"foundation skipped: {sum(1 for word in words if word['isFoundation'])}")
