#!/usr/bin/env python3
# VERSION=0.2.0
# 变更摘要：
#   0.2.0 矩阵化升级（reviewer P0-1 / P1-3 共 11 项，逐条落地）：
#     ① --species 扩到 5 蚁种 scout/hauler/soldier/chef/builder（新增 builder），支持多值与逗号分隔
#     ② 每个 cell 显式记录维度：seed（宿主固定 42，多 seed 靠多 slot）、slot、species、ticks、
#        baselineFile、dslSha256、protocolVersion（observe 顶层 ProtocolVersion=18）、gitHead、runtimeMs
#     ③ 确定性自检改为「逐 cell 全字段比对」：同一 cell 复跑两次比对整个结果对象（含所有分数字段、
#        ProgramErrors、InventoryDelta、raw），不再只比首份 DSL 的 1 个字段
#     ④ 采集 v0.19 新增的 EvaluationBranch 字段 ProcessingValueAdded / DevelopmentValueDelta，
#        缺失记 None 不默认 0（大小写不敏感取键，沿用 ci()）
#     ⑤ ProgramErrors 细化成 {total, firstErrorTick, errorCodes, bySpecies}；返回里取不到的子项记 None，不编造
#     ⑥ validate_program 默认强制前置（每份 DSL 先校验再评估，不合法如实记入结果且不跳过），
#        --allow-invalid 允许跳过前置校验直接评估
#     ⑦ Bridge 的 __init__/rpc/call/close 全套异常包装：进程起不来、JSON-RPC 超时、管道断、close 报错
#        都给中文可读错误并收尾杀子进程，不裸抛栈
#     ⑧ make_baseline_slot 默认要求 fresh（Tick==0 且 Paused），否则报错退出；--allow-dirty-host 放宽
#     ⑨ VERSION 双份合为单一真源：代码从文件头 # VERSION= 行解析（不再硬编码常量），升 0.1.1→0.2.0
#     ⑩ 修 out.with_suffix(...) 对带点 --out 会截断的 bug，改成显式拼 .json / .csv
#     ⑪ --matrix 跑 programs × ticks × species 笛卡尔积，出一个 JSON + 一个 CSV 对照表；
#        主评分用 Candidate.NewFoodPoints（FirstAdmissionPoints 增量），不看 GrossDelivered；
#        baseline 概念分清：evaluate_save 内部 Run(false) 空白对照分支 → emptyControlScore，
#        塞进排名的官方默认 DSL 候选行 → baselineFile / baselineFileScore，两者命名不混
#     附：v0.18 及以前 content14 下「官方默认 8 分 / 劣质 DSL 0 分」基线在 content15/schema15 下作废，
#        本版不再写死该数值，改由运行产物的 baselineCalibration 段按 (species,ticks) 实测重标定
#   0.1.1 修 make_baseline_slot 的 Command 缺 value/reason 参数；默认 ticks 300→1200；加「全 0 分」提示
#   0.1.0 首版：薄驱动，复用 game-agent.py 协议，评分全交官方 evaluate_save
"""Ant Mind 多模型 DSL 同题评测薄驱动（headless Linux 版配套，v0.2.0 矩阵化）。

职责边界（WXT 定的「不自建、不重复造车轮」）：
  本脚本【只接收已经生成好的 DSL 文件】，把每一份喂给官方 evaluate_save 评分，
  出同题对照表（一个 JSON + 一个 CSV）。脚本内部【不调用任何 LLM / new-api 网关】。

同题对照怎么保证：
  宿主 Evaluator --serve 是 fresh paused 世界，种子写死 42
  （tools/AntMind.Evaluator/Program.cs:32），--serve 起时 Tick=0 且 Paused。
  每个 cell 从这份 fresh 世界存一份自己的基准档（多 cell 各自 slot，避免互踩），
  之后 evaluate_save 从该档分支出 Baseline（Run(false)，空白对照）与 Candidate
  （装了该 DSL），两条内存分支互不污染。所有 slot 内容一致 = 同题。
  【种子不是评估参数】EvaluationRequest 没有 seed 字段；世界随机态由存档固定。
  多 seed 只能换宿主种子 + 每 seed 各建一份基准档（多 slot），本任务不做 --compare-ratios。

两个 baseline 概念，别混：
  * emptyControlScore / deltaVsEmptyControl：evaluate_save 内部 Run(false) 空白对照分支
    （SavedGames.cs:76/84，不装候选 DSL，从同一存档跑的默认程序分支）。
  * baselineFile / baselineFileScore / deltaVsBaselineFile：作为候选行塞进排名的
    官方默认 DSL 文件（--baseline）在相同 (species,ticks) 下的得分。

主评分 = Candidate.NewFoodPoints（FirstAdmissionPoints 增量，防倒运刷分），
不看 GrossDelivered（后者只在表里展示、不参与排名）。

前置：wire18 的 Evaluator.dll 与 Mcp.dll 必须是同批重建产物（否则 Pipes.cs 回
  "protocol_version"）；宿主与 client 同 EUID / 同 TMPDIR / 同 mount namespace，
  ANTMIND_PIPE 用独立值（如 antmind-v019-matrix），host/client 继承同一值。

用法（单表，多份 DSL 对比）：
  python3 scripts/model-eval-bridge.py --species hauler \
      --programs A.ant B.ant C.ant --out .tmp-eval/single

用法（矩阵：2 DSL × 2 ticks × 2 物种，逐 cell 全字段确定性自检）：
  ANTMIND_PIPE=antmind-v019-matrix python3 scripts/model-eval-bridge.py --matrix \
      --species hauler builder --ticks 600 1200 \
      --programs A.ant B.ant --baseline official/builder.ant \
      --out .tmp-eval/v019-matrix

默认输出前缀落在被 gitignore 的 .tmp-eval/ 下，不污染工作树。
"""
import argparse
import csv
import hashlib
import json
import os
import queue
import re
import subprocess
import sys
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path


def _version_from_header():
    """单一真源：版本号只写在文件头 # VERSION= 行，代码从这里解析，不再有第二份常量。"""
    with Path(__file__).open(encoding="utf-8") as fh:
        for line in fh:
            m = re.match(r"^#\s*VERSION\s*=\s*(\S+)\s*$", line)
            if m:
                return m.group(1)
            if line.strip() and not line.startswith("#"):
                break
    raise RuntimeError("文件头缺少 # VERSION= 版本标注，无法解析版本号")


VERSION = _version_from_header()
ROOT = Path(__file__).resolve().parents[1]
MCP_DLL = ROOT / "src/AntMind.Mcp/bin/Debug/net10.0/AntMind.Mcp.dll"
DEFAULT_BASELINE = ROOT / "content/programs/hauler-reliable.ant"
SPECIES = ("scout", "hauler", "soldier", "chef", "builder")
HOST_SEED = 42  # tools/AntMind.Evaluator/Program.cs:32 写死：new GameSession(ContentLoader.LoadDefault(), 42)
SLOT_CHARS = set("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-")
BASELINE_LABEL = "官方默认(参照/baselineFile)"
DEPRECATED_BASELINE = {"old": "官方默认 8 分 / 劣质 DSL 0 分",
                       "scope": "v0.18 及以前 content14",
                       "status": "在 content15/schema15 下作废，本版按 (species,ticks) 实测重标定"}


class BridgeError(RuntimeError):
    """评测桥可读错误：中文消息，避免向 WXT 裸抛 traceback。"""


def ci(obj, key, default=None):
    """大小写不敏感取键：C# record 默认 PascalCase，个别工具结果用 camelCase。"""
    if not isinstance(obj, dict):
        return default
    low = key.lower()
    for k, v in obj.items():
        if k.lower() == low:
            return v
    return default


def first_present(obj, keys):
    """从若干候选键名里取第一个非 None 值；全取不到返回 None（不编造）。"""
    for key in keys:
        value = ci(obj, key)
        if value is not None:
            return value
    return None


def split_values(values):
    """nargs='+' 再叠加逗号分隔：--species 'hauler,builder' 与 --species hauler builder 等价。"""
    out = []
    for token in values or []:
        for piece in str(token).split(","):
            piece = piece.strip()
            if piece and piece not in out:
                out.append(piece)
    return out


def git_head():
    """git rev-parse HEAD；取不到记 None（不编造）。"""
    try:
        r = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, stdout=subprocess.PIPE,
                           stderr=subprocess.DEVNULL, text=True, encoding="utf-8", timeout=15)
        return r.stdout.strip() if r.returncode == 0 and r.stdout.strip() else None
    except (OSError, subprocess.SubprocessError):
        return None


def clean_slot_stem(text):
    stem = "".join(c if c in SLOT_CHARS else "-" for c in str(text))
    stem = re.sub(r"-+", "-", stem).strip("-")
    return stem or "eval"


def derive_slot(stem, index, species, ticks):
    """多 cell 各不相同的合法 slot（1..40 位 ASCII 字母数字/_/-），避免互相踩存档。"""
    suffix = "-c%03d-%s-t%d" % (index, species[:6], ticks)
    keep = 40 - len(suffix)
    return (stem[:keep] if keep > 0 else "eval") + suffix


def valid_slot(slot):
    return 1 <= len(slot) <= 40 and all(c in SLOT_CHARS for c in slot)


def canonical(obj):
    return json.dumps(obj, ensure_ascii=False, sort_keys=True, default=str)


def diff_fields(a, b):
    """返回两个结果对象里不一致的顶层键名（全字段比对用）。"""
    keys = sorted(set(a) | set(b))
    missing = "<MISSING>"
    return [k for k in keys
            if canonical(a.get(k, missing)) != canonical(b.get(k, missing))]


def parse_program_errors(cand):
    """⑤ ProgramErrors 细化结构：能从返回里取到多少取多少，取不到的子项记 None，不编造。

    现状（SavedGames.cs:79-82）：evaluate_save 只汇总一个 int ProgramErrors 计数，
    返回里没有 firstErrorTick / errorCodes / bySpecies，因此这些子项如实记 None。
    """
    return {
        "total": ci(cand, "ProgramErrors"),
        "firstErrorTick": first_present(cand, ("FirstErrorTick", "ProgramErrorFirstTick",
                                               "ErrorFirstTick", "firstErrorTick")),
        "errorCodes": first_present(cand, ("ErrorCodes", "ProgramErrorCodes",
                                           "ProgramErrorCodesByTick", "errorCodes")),
        "bySpecies": first_present(cand, ("ProgramErrorsBySpecies", "ErrorCountsBySpecies",
                                          "ErrorsBySpecies", "bySpecies")),
    }


class Bridge:
    """复用 game-agent.py 的逐行 JSON-RPC：起 MCP.dll 子进程并收发消息。

    ⑦ 全套异常包装：所有失败都转成 BridgeError 中文消息，并在必要时杀掉子进程收尾。
    """

    def __init__(self):
        dotnet_exe = "dotnet.exe" if os.name == "nt" else "dotnet"
        local = ROOT / ".tools/dotnet" / dotnet_exe
        exe = str(local) if local.exists() else dotnet_exe
        if not MCP_DLL.exists():
            raise BridgeError("找不到 MCP 程序集 %s，请先构建 wire18 Debug 产物" % MCP_DLL)
        env = os.environ.copy()
        if local.exists():
            env["DOTNET_ROOT"] = str(local.parent)
        env.setdefault("ANTMIND_PIPE", "antmind-v1")
        kwargs = dict(cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                      stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
        if os.name == "nt":
            kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
        self.proc = None
        self.replies = queue.Queue()
        self.counter = 0
        try:
            self.proc = subprocess.Popen([exe, str(MCP_DLL)], **kwargs)
        except (OSError, ValueError) as exc:
            raise BridgeError("无法启动 MCP 桥接进程（%s %s）：%s" % (exe, MCP_DLL, exc)) from exc
        try:
            threading.Thread(target=self._receive, daemon=True).start()
            self.rpc("initialize", dict(protocolVersion="2025-06-18", capabilities={},
                                        clientInfo=dict(name="model-eval-bridge", version=VERSION)))
            self._send(dict(jsonrpc="2.0", method="notifications/initialized"))
        except BridgeError:
            self._kill()
            raise
        except Exception as exc:
            self._kill()
            raise BridgeError("MCP 桥接初始化失败：%s" % exc) from exc

    def _kill(self):
        """统一收尾：尽力杀子进程，吞掉一切二次异常，绝不让收尾本身裸抛。"""
        if self.proc is None:
            return
        try:
            if self.proc.poll() is None:
                self.proc.kill()
            self.proc.wait(timeout=5)
        except Exception:
            try:
                self.proc.kill()
            except Exception:
                pass

    def _receive(self):
        try:
            for line in self.proc.stdout:
                line = line.strip()
                if not line:
                    continue
                try:
                    self.replies.put(json.loads(line))
                except json.JSONDecodeError as exc:
                    self.replies.put({"__decode_error__": str(exc)})
        except (OSError, ValueError) as exc:
            self.replies.put({"__pipe_broken__": str(exc)})
        finally:
            self.replies.put(None)

    def _send(self, message):
        if self.proc is None or self.proc.poll() is not None:
            raise BridgeError("MCP 桥接进程已退出，无法发送请求")
        try:
            self.proc.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
            self.proc.stdin.flush()
        except (BrokenPipeError, OSError, ValueError) as exc:
            self._kill()
            raise BridgeError("MCP 桥接管道断开，写入请求失败：%s" % exc) from exc

    def rpc(self, method, params, timeout=120):
        self.counter += 1
        rid = self.counter
        self._send(dict(jsonrpc="2.0", id=rid, method=method, params=params))
        while True:
            try:
                msg = self.replies.get(timeout=timeout)
            except queue.Empty as exc:
                self._kill()
                raise BridgeError("JSON-RPC 等待响应超时（%d 秒，method=%s，id=%d）；已终止桥接子进程"
                                  % (timeout, method, rid)) from exc
            if msg is None:
                raise BridgeError("MCP 桥接进程意外退出（method=%s），管道已断" % method)
            if not isinstance(msg, dict):
                raise BridgeError("MCP 桥接返回非对象消息（method=%s）：%r" % (method, msg))
            if "__pipe_broken__" in msg:
                raise BridgeError("MCP 桥接读取管道失败：%s" % msg["__pipe_broken__"])
            if "__decode_error__" in msg:
                raise BridgeError("MCP 桥接返回非法 JSON：%s" % msg["__decode_error__"])
            if msg.get("id") != rid:
                continue
            if "error" in msg:
                raise BridgeError("JSON-RPC 返回错误（method=%s）：%s"
                                  % (method, json.dumps(msg["error"], ensure_ascii=False)))
            return msg["result"]

    def call(self, name, arguments):
        try:
            result = self.rpc("tools/call", dict(name=name, arguments=arguments))
        except BridgeError:
            raise
        except Exception as exc:
            raise BridgeError("工具调用 %s 失败：%s" % (name, exc)) from exc
        if not isinstance(result, dict):
            raise BridgeError("工具 %s 返回结构异常（不是对象）" % name)
        if result.get("isError"):
            raise BridgeError("工具 %s 报告错误：%s"
                              % (name, json.dumps(result, ensure_ascii=False)[:500]))
        try:
            content = result.get("content") or []
            text = "".join(c.get("text", "") for c in content
                           if isinstance(c, dict) and c.get("type") == "text")
            return json.loads(text) if text.strip() else {}
        except (json.JSONDecodeError, TypeError, AttributeError) as exc:
            raise BridgeError("工具 %s 返回内容无法解析为 JSON：%s" % (name, exc)) from exc

    def close(self):
        try:
            if self.proc is None:
                return
            if self.proc.stdin and not self.proc.stdin.closed:
                try:
                    self.proc.stdin.close()
                except (OSError, ValueError) as exc:
                    print("警告：关闭桥接 stdin 失败：%s" % exc, file=sys.stderr)
            try:
                self.proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                print("警告：桥接进程 5 秒内未退出，强制终止", file=sys.stderr)
                self._kill()
            except (OSError, ValueError) as exc:
                print("警告：等待桥接进程退出失败：%s" % exc, file=sys.stderr)
                self._kill()
        except Exception as exc:
            print("警告：关闭桥接时出错，已强制收尾：%s" % exc, file=sys.stderr)
            self._kill()


def make_baseline_slot(bridge, slot, allow_dirty=False):
    """⑧ 从 fresh paused 世界存一份该 cell 专属基准档；默认非 fresh 直接报错退出。

    observe 顶层同时带 ProtocolVersion（wire18=18），一并返回供 ② 记录维度。
    Command 的 value / reason 是无默认值的位置参数（Program.cs:105），必须显式给。
    """
    resp = bridge.call("observe", {})
    snap = ci(resp, "Snapshot")
    if not snap:
        raise BridgeError("observe 未返回 Snapshot，宿主未就绪（检查 ANTMIND_PIPE 与 wire18 同批产物）")
    revision = ci(snap, "Revision")
    world_id = ci(snap, "WorldId")
    tick = ci(snap, "Tick")
    paused = ci(snap, "Paused")
    protocol_version = ci(resp, "ProtocolVersion")
    if not allow_dirty and (tick != 0 or not paused):
        raise BridgeError("宿主世界不是 fresh paused：Tick=%s, Paused=%s；请用 fresh 的 "
                          "Evaluator --serve 宿主，或加 --allow-dirty-host 放宽" % (tick, paused))
    if allow_dirty and (tick != 0 or not paused):
        print("警告：--allow-dirty-host 已放宽，宿主 Tick=%s Paused=%s" % (tick, paused), file=sys.stderr)
    r = bridge.call("command", dict(requestId=str(uuid.uuid4()), expectedRevision=revision,
                                    expectedWorldId=world_id, kind="SaveGame", value=0,
                                    reason="model-eval 矩阵 cell 基准存档", key=slot))
    if not ci(ci(r, "Result") or {}, "Accepted"):
        raise BridgeError("存基准档失败（slot=%s）：%s" % (slot, json.dumps(r, ensure_ascii=False)[:500]))
    return dict(revision=revision, world_id=world_id, tick=tick, paused=paused,
                protocolVersion=protocol_version)


def evaluate_one(bridge, slot, ticks, species, source, force_validate=True):
    """一次 cell 评估，返回【纯结果对象】（不含 label/file/rank/维度/计时，便于 ③ 全字段比对）。"""
    result = {}
    if force_validate:
        try:
            v = bridge.call("validate_program", dict(source=source))
            result["valid"] = bool(ci(v, "valid", False))
            if not result["valid"]:
                result["validate"] = v  # ⑥ 不合法如实记入，不跳过
        except Exception as exc:
            result["valid"] = None
            result["validate"] = "校验调用失败：%s" % exc
    else:
        result["valid"] = None
        result["validateSkipped"] = True
    try:
        resp = bridge.call("evaluate_save", dict(slot=slot, ticks=ticks, species=species, source=source))
    except BridgeError as exc:
        # ⑥ 单份 DSL 评估失败（如非法 DSL 触发 TryQueueApply 抛错）如实记入，绝不中断整个矩阵出表
        result["error"] = "evaluate_save 调用失败：%s" % exc
        return result
    err = ci(resp, "Error")
    if err:
        result["error"] = err
        return result
    ev = ci(resp, "Evaluation")
    if not ev:
        result["error"] = "返回缺少 Evaluation：%s" % json.dumps(resp, ensure_ascii=False)[:300]
        return result
    cand = ci(ev, "Candidate") or {}
    base = ci(ev, "Baseline") or {}
    result["score"] = ci(cand, "NewFoodPoints")                       # 主评分
    result["emptyControlScore"] = ci(base, "NewFoodPoints")            # Run(false) 空白对照分支
    both = result["score"] is not None and result["emptyControlScore"] is not None
    result["deltaVsEmptyControl"] = (result["score"] - result["emptyControlScore"]) if both else None
    result["grossDelivered"] = ci(cand, "GrossDelivered")              # 仅展示，不参与排名
    result["antsLost"] = ci(cand, "AntsLost")
    result["programErrors"] = parse_program_errors(cand)               # ⑤
    result["endTick"] = ci(cand, "EndTick")
    result["startTick"] = ci(cand, "StartTick")
    result["inventoryDelta"] = ci(cand, "InventoryDelta")
    result["processingValueAdded"] = ci(cand, "ProcessingValueAdded")            # ④ 缺失记 None
    result["developmentValueDelta"] = ci(cand, "DevelopmentValueDelta")         # ④ 缺失记 None
    result["emptyControlProcessingValueAdded"] = ci(base, "ProcessingValueAdded")
    result["emptyControlDevelopmentValueDelta"] = ci(base, "DevelopmentValueDelta")
    result["raw"] = ev
    return result


def csv_val(value):
    if value is None:
        return ""
    if isinstance(value, (dict, list)):
        return json.dumps(value, ensure_ascii=False, sort_keys=True)
    if isinstance(value, bool):
        return "是" if value else "否"
    return value


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--programs", nargs="+", type=str, required=True,
                    help="各模型输出的 DSL 文件（可多值/逗号分隔；始终展开为多个候选行）")
    ap.add_argument("--baseline", type=str, default=str(DEFAULT_BASELINE),
                    help="官方默认 DSL 作参照组（baselineFile 候选行）；设 none 关闭")
    ap.add_argument("--slot", default="model-eval-baseline",
                    help="基准存档名或矩阵 slot 前缀（1..40 位 ASCII 字母数字/_/-）；多 cell 时派生各自 slot")
    ap.add_argument("--ticks", nargs="+", type=str, default=["1200"],
                    help="评估步数，可多值/逗号分隔（如 600 1200 或 600,1200），每个 1..1200；多值需配 --matrix")
    ap.add_argument("--species", nargs="+", type=str, default=["hauler"],
                    help="DSL 装到哪个蚁种，可多值/逗号分隔（scout/hauler/soldier/chef/builder）；多值需配 --matrix")
    ap.add_argument("--matrix", action="store_true",
                    help="跑 programs × ticks × species 笛卡尔积，出单个 JSON + 单个 CSV 对照表"
                         "（--ticks/--species 多值时必须加此开关）")
    ap.add_argument("--allow-invalid", action="store_true",
                    help="跳过 validate_program 前置校验，直接 evaluate_save（不合法 DSL 由 ProgramErrors 如实捕捉）")
    ap.add_argument("--allow-dirty-host", action="store_true",
                    help="放宽 fresh 检查：允许宿主非 Tick=0/非 Paused 时继续建基准档")
    ap.add_argument("--no-selfcheck", action="store_true",
                    help="关闭确定性自检（默认逐 cell 复跑两次做全字段比对）")
    ap.add_argument("--out", default="",
                    help="输出前缀，显式拼 .json/.csv（默认 .tmp-eval/model-eval-<UTC时间戳>，被 gitignore）")
    args = ap.parse_args()

    programs = split_values(args.programs)
    species_values = split_values(args.species)
    ticks_raw = split_values(args.ticks)
    if not programs:
        ap.error("--programs 至少给一份 DSL")
    if not species_values:
        ap.error("--species 至少一个蚁种")
    bad_species = [s for s in species_values if s not in SPECIES]
    if bad_species:
        ap.error("--species 含不支持的蚁种 %s；可用：%s" % (bad_species, "/".join(SPECIES)))
    try:
        ticks_values = [int(t) for t in ticks_raw]
    except ValueError:
        ap.error("--ticks 必须是整数：%s" % ticks_raw)
    bad_ticks = [t for t in ticks_values if not 1 <= t <= 1200]
    if bad_ticks:
        ap.error("--ticks 必须 1..1200（上游 evaluate_save 硬限制），越界：%s" % bad_ticks)
    # ⑪ --programs 多值是 0.1.1 起就有的默认行为；只有 ticks/species 多值才强制 --matrix
    if not args.matrix and (len(ticks_values) > 1 or len(species_values) > 1):
        ap.error("检测到 --ticks/--species 多值但未给 --matrix；加 --matrix 跑笛卡尔积，或只留单值")
    stem = clean_slot_stem(args.slot)
    if not valid_slot(stem):
        ap.error("--slot 清洗后仍不合法：%s（需 1..40 位 ASCII 字母数字/_/-）" % stem)

    baseline_path = None if args.baseline.strip().lower() == "none" else Path(args.baseline)
    if baseline_path is not None and not baseline_path.is_absolute():
        baseline_path = ROOT / baseline_path
    candidates = []
    if baseline_path is not None and baseline_path.exists():
        candidates.append((BASELINE_LABEL, baseline_path))
    program_paths = []
    for raw in programs:
        p = Path(raw)
        if not p.is_absolute():
            p = ROOT / p
        program_paths.append(p)
        candidates.append((p.stem, p))
    for label, p in candidates:
        if not p.exists():
            ap.error("DSL 文件不存在：%s（%s）" % (p, label))

    cell_dims = [(sp, tk, label, p) for sp in species_values
                 for tk in ticks_values for label, p in candidates]
    cell_count = len(cell_dims)
    multi_slot = cell_count > 1
    protocol_global = None
    world_global = None
    git_head_value = git_head()
    cells = []
    determinism = {"enabled": not args.no_selfcheck, "total": 0, "passed": 0, "failed": 0, "cells": []}
    bridge = None
    try:
        bridge = Bridge()
        for index, (sp, tk, label, p) in enumerate(cell_dims, 1):
            slot = derive_slot(stem, index, sp, tk) if multi_slot else stem
            if not valid_slot(slot):
                raise BridgeError("派生 slot 不合法：%s" % slot)
            source = p.read_text(encoding="utf-8")
            t0 = time.perf_counter()
            world = make_baseline_slot(bridge, slot, allow_dirty=args.allow_dirty_host)
            if world_global is None:
                world_global = world
                protocol_global = world.get("protocolVersion")
            result = evaluate_one(bridge, slot, tk, sp, source, force_validate=not args.allow_invalid)
            runtime_ms = round((time.perf_counter() - t0) * 1000, 1)
            # ③ 逐 cell 全字段比对：同一 cell（同 slot/DSL/ticks/species）复跑两次
            if not args.no_selfcheck:
                t1 = time.perf_counter()
                again = evaluate_one(bridge, slot, tk, sp, source, force_validate=not args.allow_invalid)
                second_ms = round((time.perf_counter() - t1) * 1000, 1)
                diffs = diff_fields(result, again)
                # 确定性 = 两次结果逐字段一致；DSL 不合法/评估报错是如实记录的数据，
                # 只要两次 error 一致就算确定性通过（不把「cell 失败」混同「非确定性」）
                passed = not diffs
                selfcheck = {"enabled": True, "passed": passed, "diffFields": diffs,
                             "secondRuntimeMs": second_ms}
                determinism["total"] += 1
                determinism["passed" if passed else "failed"] += 1
            else:
                selfcheck = {"enabled": False, "passed": None, "diffFields": None,
                             "secondRuntimeMs": None}
            determinism["cells"].append({"cellId": "c%03d" % index, "slot": slot,
                                         "species": sp, "ticks": tk, "label": label,
                                         "passed": selfcheck["passed"],
                                         "diffFields": selfcheck["diffFields"]})
            cells.append({
                "cellId": "c%03d" % index,
                # ② 维度显式摊平在 cell 顶层
                "seed": HOST_SEED,
                "seedNote": "宿主固定 42（Evaluator/Program.cs:32）；多 seed 需换宿主种子 + 每 seed 各建基准档(多 slot)",
                "slot": slot,
                "species": sp,
                "ticks": tk,
                "baselineFile": str(baseline_path) if baseline_path is not None else None,
                "isBaselineFileCandidate": label == BASELINE_LABEL,
                "dslSha256": hashlib.sha256(source.encode("utf-8")).hexdigest(),
                "protocolVersion": world.get("protocolVersion"),
                "gitHead": git_head_value,
                "runtimeMs": runtime_ms,
                "label": label,
                "file": str(p),
                **result,
                "selfcheck": selfcheck,
            })
    except BridgeError as exc:
        print("评测桥错误：%s" % exc, file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("已中断：正在收尾桥接进程", file=sys.stderr)
        return 130
    except Exception as exc:  # 兜底：任何意外都不裸抛栈
        print("评测过程发生意外错误：%s: %s" % (type(exc).__name__, exc), file=sys.stderr)
        return 3
    finally:
        if bridge is not None:
            bridge.close()

    # 回填「相对 baselineFile 参照行的 Δ」：按 (species,ticks) 分组取参照行得分
    ref_score = {}
    for c in cells:
        if c["isBaselineFileCandidate"] and c.get("score") is not None:
            ref_score[(c["species"], c["ticks"])] = c["score"]
    for c in cells:
        ref = ref_score.get((c["species"], c["ticks"]))
        c["baselineFileScore"] = ref
        c["deltaVsBaselineFile"] = (c["score"] - ref) if (ref is not None and c.get("score") is not None) else None

    # 排名：主评分 Candidate.NewFoodPoints 降序（None 垫底）；GrossDelivered 不参与
    ranked = sorted(cells, key=lambda c: (c.get("score") is not None, c.get("score") or 0), reverse=True)
    for i, c in enumerate(ranked, 1):
        c["rank"] = i
    group_rank = {}
    ordered = sorted(cells, key=lambda c: (c["species"], c["ticks"],
                                           -(c.get("score") or 0), c["label"]))
    for c in ordered:
        key = (c["species"], c["ticks"])
        group_rank[key] = group_rank.get(key, 0) + 1
        c["rankInGroup"] = group_rank[key]

    # schema15 重标定：按 (species,ticks) 列出 baselineFile 参照行实测得分，明确旧 8/0 作废
    calibration = []
    for c in cells:
        if c["isBaselineFileCandidate"]:
            calibration.append({"species": c["species"], "ticks": c["ticks"],
                                "slot": c["slot"], "dslSha256": c["dslSha256"],
                                "baselineFile": c["baselineFile"],
                                "newFoodPoints": c.get("score"),
                                "emptyControlScore": c.get("emptyControlScore"),
                                "grossDelivered": c.get("grossDelivered"),
                                "programErrorsTotal": (c.get("programErrors") or {}).get("total")})
    calibration.sort(key=lambda x: (x["species"], x["ticks"]))

    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H%M%SZ")
    out = Path(args.out) if args.out else ROOT / ".tmp-eval" / ("model-eval-%s" % stamp)
    if not out.is_absolute():
        out = ROOT / out
    out.parent.mkdir(parents=True, exist_ok=True)
    # ⑩ 显式拼后缀；带点 --out（如 foo.v2）不再被 with_suffix 截断
    json_path = Path(str(out) + ".json")
    csv_path = Path(str(out) + ".csv")
    payload = dict(
        version=VERSION, generatedAt=stamp, matrix=bool(args.matrix),
        control=dict(
            programs=[str(p) for p in program_paths], ticks=ticks_values, species=species_values,
            cellCount=cell_count, baselineFile=str(baseline_path) if baseline_path is not None else None,
            slotStem=stem, slotMode=("每 cell 派生独立 slot" if multi_slot else "单 slot"),
            pipe=os.environ.get("ANTMIND_PIPE", "antmind-v1"), hostSeed=HOST_SEED,
            seedNote="宿主固定 42；EvaluationRequest 无 seed 字段，多 seed 靠换宿主种子 + 多 slot",
            protocolVersion=protocol_global, gitHead=git_head_value, world=world_global,
            primaryScore="Candidate.NewFoodPoints（FirstAdmissionPoints 增量）",
            notPrimaryScore="GrossDelivered 仅展示，不参与排名",
            baselineSemantics={
                "emptyControlBranch": "evaluate_save 内部 Run(false)：不装候选 DSL、从同一存档跑的空白对照分支 → emptyControlScore / deltaVsEmptyControl",
                "baselineFileCandidate": "作为候选行塞进排名的官方默认 DSL → baselineFile / isBaselineFileCandidate / baselineFileScore / deltaVsBaselineFile",
            },
            validateMode=("--allow-invalid 跳过前置校验" if args.allow_invalid else "validate_program 强制前置，如实记录且不跳过"),
            freshMode=("--allow-dirty-host 放宽" if args.allow_dirty_host else "要求 fresh（Tick==0 且 Paused）"),
            selfcheckMode=("已关闭" if args.no_selfcheck else "逐 cell 全字段比对（复跑两次）"),
        ),
        determinism=determinism,
        baselineCalibration={"deprecatedBaseline": DEPRECATED_BASELINE, "measured": calibration},
        cells=ordered,
    )
    json_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    header = ["cellId", "排名", "组内排名", "species", "ticks", "slot", "DSL/文件", "是否baselineFile参照行",
              "语法有效", "空白对照分(Run(false))", "候选分NewFoodPoints(主评分)", "Δvs空白对照",
              "baselineFile参照分", "Δvs baselineFile", "ProcessingValueAdded", "DevelopmentValueDelta",
              "ProgramErrors.total", "ProgramErrors.firstErrorTick", "ProgramErrors.errorCodes",
              "ProgramErrors.bySpecies", "InventoryDelta", "总交付GrossDelivered(非主评分)",
              "损失蚁", "结束Tick", "seed", "dslSha256", "protocolVersion", "gitHead", "runtimeMs",
              "确定性自检", "备注"]
    with csv_path.open("w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(header)
        for c in ordered:
            pe = c.get("programErrors") or {}
            w.writerow([
                c["cellId"], c["rank"], c["rankInGroup"], c["species"], c["ticks"], c["slot"],
                c["label"], csv_val(c["isBaselineFileCandidate"]), csv_val(c.get("valid")),
                csv_val(c.get("emptyControlScore")), csv_val(c.get("score")),
                csv_val(c.get("deltaVsEmptyControl")), csv_val(c.get("baselineFileScore")),
                csv_val(c.get("deltaVsBaselineFile")), csv_val(c.get("processingValueAdded")),
                csv_val(c.get("developmentValueDelta")), csv_val(pe.get("total")),
                csv_val(pe.get("firstErrorTick")), csv_val(pe.get("errorCodes")),
                csv_val(pe.get("bySpecies")), csv_val(c.get("inventoryDelta")),
                csv_val(c.get("grossDelivered")), csv_val(c.get("antsLost")), csv_val(c.get("endTick")),
                c["seed"], c["dslSha256"], csv_val(c.get("protocolVersion")), csv_val(c.get("gitHead")),
                csv_val(c.get("runtimeMs")),
                ("通过" if c["selfcheck"]["passed"] else "未通过") if c["selfcheck"]["enabled"] else "关闭",
                csv_val(c.get("error") or ("" if c.get("valid") in (True, None) else c.get("validate"))),
            ])

    print("Ant Mind 多模型 DSL 同题评测（v%s，%s）" % (VERSION, "矩阵" if args.matrix else "单表"))
    print("控制：%d cell | species=%s | ticks=%s | pipe=%s | protocolVersion=%s | gitHead=%s" %
          (cell_count, ",".join(species_values), ",".join(str(t) for t in ticks_values),
           os.environ.get("ANTMIND_PIPE", "antmind-v1"), protocol_global,
           (git_head_value or "None")[:12]))
    print("主评分=Candidate.NewFoodPoints；GrossDelivered 仅展示；空白对照=Run(false)≠baselineFile 参照行")
    print("%-5s %-8s %-5s %-24s %-9s %-9s %-8s %-6s" %
          ("cell", "species", "ticks", "DSL/文件", "候选分", "Δvs参照", "错误数", "自检"))
    for c in ordered:
        pe = c.get("programErrors") or {}
        print("%-5s %-8s %-5s %-24s %-9s %-9s %-8s %-6s" %
              (c["cellId"], c["species"], c["ticks"], c["label"][:24],
               str(c.get("score", "-")), str(c.get("deltaVsBaselineFile", "-")),
               str(pe.get("total", "-")),
               ("通过" if c["selfcheck"]["passed"] else "未通过") if c["selfcheck"]["enabled"] else "关闭"))
    if determinism["enabled"]:
        print("确定性自检（逐 cell 全字段比对）：%d/%d 通过%s" %
              (determinism["passed"], determinism["total"],
               "" if not determinism["failed"] else "，失败 cell：%s" %
               [x["cellId"] for x in determinism["cells"] if x["passed"] is False]))
    if calibration:
        print("schema15 官方默认基线重标定（旧「8/0」作废）：")
        for x in calibration:
            print("  %s ticks=%d → NewFoodPoints=%s（空白对照 %s，GrossDelivered %s）" %
                  (x["species"], x["ticks"], x["newFoodPoints"], x["emptyControlScore"], x["grossDelivered"]))
    scored = [c.get("score") for c in cells if c.get("score") is not None]
    if scored and all(s == 0 for s in scored):
        print("提示：所有 cell 均为 0 分——fresh 世界 tick=0，tick 太少可能还没完成首次入巢计分，评测无区分度")
    print("结果：%s\n      %s" % (json_path, csv_path))
    return 0 if (not determinism["enabled"] or determinism["failed"] == 0) else 4


if __name__ == "__main__":
    sys.exit(main())
