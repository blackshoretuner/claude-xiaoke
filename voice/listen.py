"""把录好的台词做成试听页 lines/listen.html：可能念得不准的（听写对不上）排在前面。"""

import html
import json
from pathlib import Path

LINES = Path(__file__).resolve().parent / "lines"
index = json.loads((LINES / "index.json").read_text(encoding="utf-8"))
rows = sorted(index.values(), key=lambda v: -v["err"])
cards = "".join(
    f'<div class="card{" warn" if v["err"] > 0.15 else ""}"><div class="text">{html.escape(v["text"])}</div>'
    f'<div class="heard">听写：{html.escape(v["heard"])} · 偏差 {v["err"]:.2f}</div>'
    f'<audio controls preload="none" src="{v["file"]}"></audio></div>'
    for v in rows
)
(LINES / "listen.html").write_text(
    f"""<!doctype html><meta charset="utf-8"><title>小克的台词试听</title>
<style>body{{margin:0;padding:18px 22px;background:#faf6f0;color:#5a2a14;font-family:"Microsoft YaHei UI",sans-serif}}
h1{{font-size:20px;margin:0 0 4px}}p{{margin:0 0 14px;color:#9a6a50;font-size:14px}}
.grid{{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:8px}}
.card{{background:#fff;border:1px solid #ecdccb;border-radius:10px;padding:8px 10px}}
.card.warn{{border-color:#e8a27a;background:#fff6ef}}.text{{font-weight:700;margin-bottom:2px}}
.heard{{font-size:12px;color:#b08070;margin-bottom:4px}}audio{{width:100%;height:32px}}</style>
<h1>小克的台词试听</h1>
<p>共 {len(rows)} 句。橙色框是听写对不上的（多半是 ASR 把"啦"听成"了"这类，不一定真有问题），排在最前面。哪句念得不好，告诉我重录。</p>
<div class="grid">{cards}</div>""",
    encoding="utf-8",
)
print(len(rows))
