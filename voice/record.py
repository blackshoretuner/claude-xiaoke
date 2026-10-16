"""把小克的台词用训练好的声音（gsv.json）录成 wav：离线批量，录完就退出，不常驻。

用 GPT-SoVITS 整合包自带的 runtime\\python.exe 跑：
  python.exe record.py <台词.json>      台词.json 是一串字符串
已经录过的（按 norm() 之后的文字比）会跳过。产物在 voice\\lines\\，索引是 voice\\lines\\index.json：
  { norm(台词): {"file", "text", "err", "heard"} }
挂件按同样的 norm() 查索引，查到就播，查不到就不出声。

真正的合成和质检交给 gsv.json 里 synth 指定的脚本：每句最多录 3 遍，用 FunASR 听写回来按拼音比对，留最准的一遍。
"""

import hashlib
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG = json.load(open(os.path.join(HERE, "gsv.json"), encoding="utf-8"))
ROOT = CONFIG["root"]
LINES = os.path.join(HERE, "lines")
INDEX = os.path.join(LINES, "index.json")
KAOMOJI = r"\([^)一-鿿]*\)"


def norm(text):
    """查索引用的键：去掉颜文字、空白和波浪线。挂件里有一份一样的（Voice.Norm）。"""
    return re.sub(r"[\s～~]", "", re.sub(KAOMOJI, "", text))


def speakable(text):
    return re.sub(KAOMOJI, "", text).replace("～", "").replace("~", "").strip()


def load_index():
    return json.load(open(INDEX, encoding="utf-8")) if os.path.exists(INDEX) else {}


def main():
    wanted = json.load(open(sys.argv[1], encoding="utf-8-sig"))
    os.makedirs(LINES, exist_ok=True)
    # 同一时间只录一批：两批同时改 index.json，后写的会把先写的盖掉。
    lock = os.path.join(LINES, "_recording.lock")
    try:
        fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError:
        print("another recording is running")
        return
    try:
        os.write(fd, str(os.getpid()).encode())
        os.close(fd)
        record(wanted)
    finally:
        os.remove(lock)


def record(wanted):
    index = load_index()

    jobs, keys = [], {}
    for text in wanted:
        key = norm(text)
        if not key or key in keys.values():
            continue
        if key in index and os.path.exists(os.path.join(LINES, index[key]["file"])):
            continue
        out = os.path.join(LINES, hashlib.sha1(key.encode("utf-8")).hexdigest()[:16] + ".wav")
        keys[out] = key
        jobs.append({
            "out": out,
            "sovits": os.path.join(ROOT, CONFIG["sovits"]),
            "gpt": os.path.join(ROOT, CONFIG["gpt"]),
            "ref": os.path.join(ROOT, CONFIG["ref"]),
            "ref_text": CONFIG["ref_text"],
            "text": speakable(text),
            "lang": "中英混合" if re.search(r"[A-Za-z]", text) else "中文",
            "tries": 3,
            "trim": True,
        })
    if not jobs:
        print("nothing to record")
        return

    jobs_path = os.path.join(LINES, "_jobs.json")
    results_path = os.path.join(LINES, "_results.json")
    json.dump(jobs, open(jobs_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    env = dict(os.environ, TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD="1", PATH=ROOT + os.pathsep + os.environ.get("PATH", ""))
    env.pop("NoDefaultCurrentDirectoryInExePath", None)
    print("recording", len(jobs), "lines", flush=True)
    subprocess.run([sys.executable, CONFIG["synth"], ROOT, jobs_path, results_path], env=env, check=True)

    index = load_index()  # 录的这几分钟里可能有人改过，以最新的为底
    for r in json.load(open(results_path, encoding="utf-8")):
        index[keys[r["out"]]] = {"file": os.path.basename(r["out"]), "text": r["text"], "err": r["err"], "heard": r["heard"]}
    tmp = INDEX + ".part"
    json.dump(index, open(tmp, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    os.replace(tmp, INDEX)
    bad = [k for k, v in index.items() if v["err"] > 0.15]
    print("done", len(index), "lines;", len(bad), "may be off:", bad, flush=True)


if __name__ == "__main__":
    main()
