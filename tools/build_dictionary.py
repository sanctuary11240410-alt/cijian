"""Build the bundled offline dictionary from the unmodified ECDICT CSV.

Usage: python build_dictionary.py path/to/ecdict.csv path/to/Assets/dictionary.db
Source: https://github.com/skywind3000/ECDICT (MIT).
Only dictionary lookup uses this dataset; inclusion does not enroll a learning word.
"""
import csv
import hashlib
import json
import re
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path


def normalize(value):
    return re.sub(r"\s+", " ", value.strip().replace("’", "'")).lower()


def clean(value):
    return (value or "").replace("\\n", "\n").strip()


def build(source, destination):
    source, destination = Path(source), Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(".building.db")
    if temporary.exists():
        temporary.unlink()
    connection = sqlite3.connect(temporary)
    connection.executescript("""
        PRAGMA journal_mode=OFF;
        PRAGMA synchronous=OFF;
        PRAGMA page_size=4096;
        CREATE TABLE entries(id INTEGER PRIMARY KEY, term TEXT NOT NULL, norm TEXT NOT NULL,
          phonetic TEXT NOT NULL, meaning TEXT NOT NULL, definition TEXT NOT NULL,
          exchange TEXT NOT NULL, rank INTEGER NOT NULL);
        CREATE TABLE aliases(alias TEXT NOT NULL, entry_id INTEGER NOT NULL,
          PRIMARY KEY(alias,entry_id)) WITHOUT ROWID;
        CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
    """)
    count, aliases, skipped = 0, 0, 0
    batch, alias_batch = [], []
    with source.open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream):
            term = clean(row.get("word"))
            meaning, definition = clean(row.get("translation")), clean(row.get("definition"))
            if not term or not (meaning or definition) or len(term) > 200:
                skipped += 1
                continue
            count += 1
            raw_rank = row.get("frq") or row.get("bnc") or ""
            rank = int(raw_rank) if raw_rank.isdigit() and int(raw_rank) > 0 else 10000000
            batch.append((count, term, normalize(term), clean(row.get("phonetic")),
                          meaning, definition, clean(row.get("exchange")), rank))
            for part in (row.get("exchange") or "").split("/"):
                if ":" not in part:
                    continue
                kind, forms = part.split(":", 1)
                # '0' is the base form, '1' is metadata; neither is a derived-form alias.
                if kind in ("0", "1"):
                    continue
                for form in forms.split(","):
                    alias = normalize(form)
                    if alias and alias != normalize(term) and len(alias) <= 200:
                        alias_batch.append((alias, count))
            if len(batch) >= 5000:
                connection.executemany("INSERT INTO entries VALUES (?,?,?,?,?,?,?,?)", batch)
                connection.executemany("INSERT OR IGNORE INTO aliases VALUES (?,?)", alias_batch)
                batch.clear()
                alias_batch.clear()
                print(f"Imported {count:,} entries", flush=True)
    connection.executemany("INSERT INTO entries VALUES (?,?,?,?,?,?,?,?)", batch)
    connection.executemany("INSERT OR IGNORE INTO aliases VALUES (?,?)", alias_batch)
    connection.execute("CREATE INDEX entries_norm ON entries(norm)")
    aliases = connection.execute("SELECT count(*) FROM aliases").fetchone()[0]
    with source.open("rb") as original:
        digest = hashlib.file_digest(original, "sha256").hexdigest()
    metadata = {"entry_count": str(count), "alias_count": str(aliases), "schema": "1",
                "source": "https://github.com/skywind3000/ECDICT", "license": "MIT",
                "source_sha256": digest, "built_at_utc": datetime.now(timezone.utc).isoformat()}
    connection.executemany("INSERT INTO metadata VALUES (?,?)", metadata.items())
    connection.commit()
    assert connection.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
    connection.close()
    temporary.replace(destination)
    print(json.dumps({**metadata, "skipped_empty_or_oversized": skipped,
                      "database_bytes": destination.stat().st_size}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    build(*sys.argv[1:3])
