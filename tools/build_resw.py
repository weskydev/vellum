"""Builds src/Vellum/Strings/<lang>/Resources.resw from tools/i18n/<lang>.tsv (key<TAB>text), using en-US as the reference.

- ".TT" / ".AN" are short for the tooltip / accessible-name keys; a missing .AN copies the .TT text
  when the English texts are the same.
- Keys missing from a .tsv are left out; the app falls back to English for them.
"""
import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape, unescape

ROOT = Path(__file__).resolve().parent.parent / "src" / "Vellum" / "Strings"
HERE = Path(__file__).parent / "i18n"
TT = ".[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"
AN = ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"

en_text = (ROOT / "en-US" / "Resources.resw").read_text(encoding="utf-8-sig")
header = en_text[: en_text.index("  <data ")]
en = {k: unescape(v) for k, v in re.findall(r'<data name="([^"]+)"[^>]*>\s*<value>(.*?)</value>', en_text, re.S)}


def full(key: str) -> str:
    if key.endswith(".TT"):
        return key[:-3] + TT
    if key.endswith(".AN"):
        return key[:-3] + AN
    return key


for lang in sys.argv[1:]:
    rows = {}
    for n, line in enumerate((HERE / f"{lang}.tsv").read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        key, _, value = line.partition("\t")
        key = full(key.strip())
        if key not in en:
            print(f"{lang}: unknown key on line {n}: {key}")
            continue
        rows[key] = value
    # Accessible names that read the same as the tooltip in English
    for key in list(rows):
        if key.endswith(TT):
            an = key[: -len(TT)] + AN
            if an in en and an not in rows and en[an] == en[key]:
                rows[an] = rows[key]
    # Placeholders must survive the translation
    for key, value in rows.items():
        want = sorted(re.findall(r"\{\d\}", en[key]))
        got = sorted(re.findall(r"\{\d\}", value))
        if want != got:
            print(f"{lang}: placeholder mismatch in {key}: {want} vs {got}")
    body = "".join(
        f'  <data name="{escape(k, {chr(34): "&quot;"})}" xml:space="preserve">\n    <value>{escape(rows[k])}</value>\n  </data>\n'
        for k in en if k in rows
    )
    out = ROOT / lang / "Resources.resw"
    out.parent.mkdir(exist_ok=True)
    out.write_text(header + body + "</root>", encoding="utf-8")
    missing = [k for k in en if k not in rows]
    print(f"{lang}: {len(rows)} strings, {len(missing)} fall back to English")
