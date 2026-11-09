#!/usr/bin/env python3
# VERSION=0.1.1
# 变更摘要：
#   0.1.1 修 make_baseline_slot 的 Command 缺 value/reason 参数（Program.cs:67 是无默认值位置参数）；
#         默认 ticks 300→1200（实测 fresh 世界 tick=0 要跑够才有首次入巢计分，300 tick 全 0 无区分度）；
#         加「全 0 分」提示，避免误判评测结果。
#   0.1.0 首版：薄驱动，复用 game-agent.py 协议，评分全交官方 evaluate_save。
"""Ant Mind 多模型 DSL 同题评测薄驱动（headless Linux 版配套）。

职责边界（WXT 定的「不自建、不重复造车轮」）：
  本脚本【只接收已经生成好的 DSL 文件】（各免费模型的输出），把每一份喂给官方
  evaluate_save 工具评分，出一张同题对照表（JSON + CSV 落 runs/）。
  脚本内部【不调用任何 LLM / new-api 网关】——「让模型写 DSL」在外部完成：
  用 new-api 网关 100.115.209.15:3001/v1 让免费模型照 content/programs/*.ant 的格式
  各写一份搬运蚁 DSL，存成 runs/dsl/<模型名>.ant，再把路径传给本脚本 --programs 即可。

同题对照怎么保证（对应 AGENTS.md「对比测试必须同题对照」）：
  所有候选共享同一个存档 slot + 同一 ticks + 同一 species。脚本启动时从 fresh
  paused 世界（Evaluator --serve，种子 42）存一份基准档，之后每个 evaluate_save
  都从这一份档分支出 Baseline（默认程序）与 Candidate（装了该 DSL），互不污染。
  【种子不是评估参数】——世界随机态由存档固定，同题性靠「同一 slot」而非传 seed；
  想换题（不同世界状态）就换 --slot 重跑一轮，脚本本身保持最小。

主评分用 Candidate.NewFoodPoints（= FirstAdmissionPoints 增量，防倒运刷分），
不看 GrossDelivered；这与上游 v0.13 计分口径一致。

评测起点实测（0.1.0 本机探针）：基准档存的是 fresh paused 世界（tick=0），
蚁群要跑够 tick 才完成「首次入巢」计分。ticks=300 时 4 个候选全 0 分（无区分度），
ticks=1200（evaluate_save 上限）时官方默认 8 分、劣质 DSL 0 分（有区分度）。
故默认 ticks 取上限 1200；若坚持较小 ticks 且结果全 0，脚本会给出提示。

前置：Evaluator --serve 宿主已在同 UID / 同 TMPDIR / 同 mount namespace 下运行
（容器里由 ENTRYPOINT 拉起），ANTMIND_PIPE 环境变量与本脚本一致。

用法（容器内 /app 下，默认 ticks=1200）：
  python3 scripts/model-eval-bridge.py \
      --species hauler \
      --programs runs/dsl/model-a.ant runs/dsl/model-b.ant \
      --out runs/model-eval
"""
import argparse
import csv
import json
import os
import queue
import subprocess
import sys
import threading
import uuid
from datetime import datetime, timezone
from pathlib import Path

VERSION = "0.1.1"
ROOT = Path(__file__).resolve().parents[1]
MCP_DLL = ROOT / "src/AntMind.Mcp/bin/Debug/net10.0/AntMind.Mcp.dll"
DEFAULT_BASELINE = ROOT / "content/programs/hauler-reliable.ant"
SPECIES = ("scout", "hauler", "soldier", "chef")


def ci(obj, key, default=None):
    """大小写不敏感取键：C# record 默认 PascalCase，个别工具结果用 camelCase。"""
    if not isinstance(obj, dict):
        return default
    low = key.lower()
    for k, v in obj.items():
        if k.lower() == low:
            return v
    return default


class Bridge:
    """复用 game-agent.py 的逐行 JSON-RPC：起 MCP.dll 子进程并收发消息。"""

    def __init__(self):
        dotnet_exe = "dotnet.exe" if os.name == "nt" else "dotnet"
        local = ROOT / ".tools/dotnet" / dotnet_exe
        env = os.environ.copy()
        if local.exists():
            env["DOTNET_ROOT"] = str(local.parent)
        env.setdefault("ANTMIND_PIPE", "antmind-v1")
        kwargs = dict(cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                      stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
        if os.name == "nt":
            kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
        self.proc = subprocess.Popen(
            [str(local) if local.exists() else dotnet_exe, str(MCP_DLL)], **kwargs)
        self.replies = queue.Queue()
        threading.Thread(target=self._receive, daemon=True).start()
        self.counter = 0
        rpc = self.rpc("initialize", dict(protocolVersion="2025-06-18", capabilities={},
                                          clientInfo=dict(name="model-eval-bridge", version=VERSION)))
        del rpc
        self._send(dict(jsonrpc="2.0", method="notifications/initialized"))

    def _receive(self):
        for line in self.proc.stdout:
            line = line.strip()
            if line:
                self.replies.put(json.loads(line))
        self.replies.put(None)

    def _send(self, message):
        self.proc.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
        self.proc.stdin.flush()

    def rpc(self, method, params, timeout=120):
        self.counter += 1
        rid = self.counter
        self._send(dict(jsonrpc="2.0", id=rid, method=method, params=params))
        while True:
            msg = self.replies.get(timeout=timeout)
            if msg is None:
                raise RuntimeError("MCP 桥接进程意外退出")
            if msg.get("id") != rid:
                continue
            if "error" in msg:
                raise RuntimeError(msg["error"])
            return msg["result"]

    def call(self, name, arguments):
        result = self.rpc("tools/call", dict(name=name, arguments=arguments))
        if result.get("isError"):
            raise RuntimeError(result)
        text = "".join(c.get("text", "") for c in result["content"] if c["type"] == "text")
        return json.loads(text) if text.strip() else {}

    def close(self):
        try:
            self.proc.stdin.close()
            self.proc.wait(timeout=5)
        except (subprocess.TimeoutExpired, BrokenPipeError):
            self.proc.kill()
            self.proc.wait()


def make_baseline_slot(bridge, slot):
    """从 fresh paused 世界存一份所有候选共用的基准档，返回存档是否新建成功。"""
    snap = ci(bridge.call("observe", {}), "Snapshot")
    if not snap:
        raise RuntimeError("observe 未返回 Snapshot，宿主未就绪")
    revision = ci(snap, "Revision")
    world_id = ci(snap, "WorldId")
    tick = ci(snap, "Tick")
    paused = ci(snap, "Paused")
    if tick != 0 or not paused:
        print("警告：宿主世界非 fresh paused（Tick=%s, Paused=%s），仍以当前态作同题起点" % (tick, paused),
              file=sys.stderr)
    # Command 工具的 value / reason 是无默认值的位置参数（Program.cs:67），必须显式给，
    # 否则 MCP 绑定会因缺参报错；SaveGame 不用 value，给 0。
    r = bridge.call("command", dict(requestId=str(uuid.uuid4()), expectedRevision=revision,
                                    expectedWorldId=world_id, kind="SaveGame", value=0,
                                    reason="model-eval 同题基准存档", key=slot))
    if not ci(ci(r, "Result") or {}, "Accepted"):
        raise RuntimeError("存基准档失败：%s" % json.dumps(r, ensure_ascii=False))
    return dict(revision=revision, world_id=world_id, tick=tick, paused=paused)


def evaluate_one(bridge, slot, ticks, species, source):
    """一次 evaluate_save：可选先本地校验语法，再走官方隔离评估。返回一行结果。"""
    row = {}
    try:
        v = bridge.call("validate_program", dict(source=source))
        row["valid"] = bool(ci(v, "valid", False))
        if not row["valid"]:
            row["validate"] = v
    except Exception as exc:  # 语法校验失败不阻断打分，交给 evaluate_save 记 ProgramErrors
        row["valid"] = False
        row["validate"] = str(exc)
    resp = bridge.call("evaluate_save", dict(slot=slot, ticks=ticks, species=species, source=source))
    err = ci(resp, "Error")
    if err:
        row["error"] = err
        return row
    ev = ci(resp, "Evaluation")
    if not ev:
        row["error"] = "返回缺少 Evaluation：%s" % json.dumps(resp, ensure_ascii=False)[:300]
        return row
    cand = ci(ev, "Candidate") or {}
    base = ci(ev, "Baseline") or {}
    row["score"] = ci(cand, "NewFoodPoints", 0)
    row["baselineScore"] = ci(base, "NewFoodPoints", 0)
    row["delta"] = (row["score"] or 0) - (row["baselineScore"] or 0)
    row["grossDelivered"] = ci(cand, "GrossDelivered", 0)
    row["antsLost"] = ci(cand, "AntsLost", 0)
    row["programErrors"] = ci(cand, "ProgramErrors", 0)
    row["endTick"] = ci(cand, "EndTick", 0)
    row["raw"] = ev
    return row


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--programs", nargs="+", type=Path, required=True, help="各模型输出的 DSL 文件（同题候选题）")
    ap.add_argument("--baseline", type=Path, default=DEFAULT_BASELINE, help="官方默认 DSL 作参照组（设 none 关闭）")
    ap.add_argument("--slot", default="model-eval-baseline", help="共用的基准存档名（1..40 字母数字/_/-）")
    ap.add_argument("--ticks", type=int, default=1200, help="评估步数，1..1200（默认取上限：tick=0 的 fresh 世界要跑够才有首次入巢计分）")
    ap.add_argument("--species", default="hauler", choices=SPECIES, help="DSL 装到哪个蚁种")
    ap.add_argument("--out", default="", help="输出前缀（默认 runs/model-eval-<UTC时间戳>），写 .json/.csv")
    ap.add_argument("--no-selfcheck", action="store_true", help="关闭确定性自检（默认对首份 DSL 复跑一次比对）")
    args = ap.parse_args()
    if not 1 <= args.ticks <= 1200:
        ap.error("--ticks 必须 1..1200（上游 evaluate_save 硬限制）")
    if not (1 <= len(args.slot) <= 40) or any(c not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-" for c in args.slot):
        ap.error("--slot 必须 1..40 位 ASCII 字母数字/_/-")

    candidates = []
    if args.baseline and str(args.baseline).lower() != "none" and args.baseline.exists():
        candidates.append(("官方默认(参照)", args.baseline))
    for p in args.programs:
        candidates.append((p.stem, p))
    for label, p in candidates:
        if not p.exists():
            ap.error("DSL 文件不存在：%s" % p)

    bridge = Bridge()
    rows = []
    determinism = None
    try:
        world = make_baseline_slot(bridge, args.slot)
        for label, p in candidates:
            source = p.read_text(encoding="utf-8")
            row = evaluate_one(bridge, args.slot, args.ticks, args.species, source)
            row["label"] = label
            row["file"] = str(p)
            rows.append(row)
        # 确定性自检：同一份 DSL 复跑一次，Candidate 分数必须一致（验证同题无副作用）
        if not args.no_selfcheck and candidates:
            label, p = candidates[0]
            again = evaluate_one(bridge, args.slot, args.ticks, args.species, p.read_text(encoding="utf-8"))
            first = rows[0].get("score")
            second = again.get("score")
            determinism = dict(label=label, first=first, second=second,
                               passed=(first is not None and first == second and not again.get("error")))
    finally:
        bridge.close()

    ranked = sorted(rows, key=lambda r: (r.get("score") is not None, r.get("score") or 0), reverse=True)
    for i, r in enumerate(ranked, 1):
        r["rank"] = i
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H%M%SZ")
    out = Path(args.out) if args.out else ROOT / "runs" / ("model-eval-%s" % stamp)
    if not out.is_absolute():
        out = ROOT / out
    out.parent.mkdir(parents=True, exist_ok=True)
    payload = dict(version=VERSION, generatedAt=stamp,
                   control=dict(slot=args.slot, ticks=args.ticks, species=args.species,
                                pipe=os.environ.get("ANTMIND_PIPE", "antmind-v1"), world=world),
                   determinism=determinism, rows=ranked)
    json_path = out.with_suffix(".json")
    csv_path = out.with_suffix(".csv")
    json_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    with csv_path.open("w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["排名", "模型/文件", "语法有效", "基线分", "候选分(NewFoodPoints)", "提升Δ",
                    "总交付", "损失蚁", "程序错误", "结束Tick", "备注"])
        for r in ranked:
            w.writerow([r.get("rank", ""), r["label"], r.get("valid", ""),
                        r.get("baselineScore", ""), r.get("score", ""), r.get("delta", ""),
                        r.get("grossDelivered", ""), r.get("antsLost", ""), r.get("programErrors", ""),
                        r.get("endTick", ""), r.get("error", "")])

    print("Ant Mind 多模型 DSL 同题评测（v%s）" % VERSION)
    print("同题控制：slot=%s  ticks=%d  species=%s  pipe=%s" %
          (args.slot, args.ticks, args.species, os.environ.get("ANTMIND_PIPE", "antmind-v1")))
    print("%-4s %-22s %-6s %-10s %-10s %-8s %-8s" %
          ("排名", "模型/文件", "有效", "基线分", "候选分", "提升Δ", "程序错误"))
    for r in ranked:
        print("%-4s %-22s %-6s %-10s %-10s %-8s %-8s" %
              (r.get("rank", ""), r["label"][:22], str(r.get("valid", "")),
               str(r.get("baselineScore", "-")), str(r.get("score", "-")),
               str(r.get("delta", "-")), str(r.get("programErrors", "-")) + ("  ERR:" + r["error"][:40] if r.get("error") else "")))
    if determinism is not None:
        print("确定性自检：%s（首份 DSL 两次得分 %s / %s）" %
              ("通过" if determinism["passed"] else "未通过",
               determinism["first"], determinism["second"]))
    # 全 0 提示：fresh 世界 tick=0，ticks 太小时蚁群来不及完成首次入巢，评测无区分度。
    scored = [r.get("score") for r in ranked if r.get("score") is not None]
    if scored and all(s == 0 for s in scored):
        print("提示：所有候选均为 0 分——fresh 世界（tick=0）下 %d tick 内可能还没完成首次入巢计分，"
              "评测无区分度；请把 --ticks 提到 1200（上限）重跑。" % args.ticks)
    print("结果：%s\n      %s" % (json_path, csv_path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
