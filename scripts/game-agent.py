"""Call the project's official MCP bridge against an already running game.

No host is started and no unrestricted player projection is used.
"""
import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import uuid

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["observe", "validate", "apply", "command", "inspect", "group"])
    parser.add_argument("--source", type=Path)
    parser.add_argument("--kind")
    parser.add_argument("--id", type=int, default=0)
    parser.add_argument("--hold-seconds", type=int, default=0)
    parser.add_argument("--value", type=int, default=0)
    parser.add_argument("--species", default="scout")
    parser.add_argument("--reason", default="")
    parser.add_argument("--group", type=int, default=0)
    parser.add_argument("--ant", type=int, default=0)
    parser.add_argument("--name", default="")
    parser.add_argument("--task", default="")
    parser.add_argument("--activity")
    parser.add_argument("--ants")
    parser.add_argument("--key", default="")
    parser.add_argument("--point")
    parser.add_argument("--food", default=None)
    parser.add_argument("--radius", type=int)
    parser.add_argument("--rally")
    parser.add_argument("--objective")
    parser.add_argument("--retreat")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if not 0 <= args.hold_seconds <= 300:
        parser.error("--hold-seconds must be in 0..300")
    if args.operation=="apply" and args.group!=0:
        parser.error("All groups share species code; do not use --group when applying programs")
    if args.operation in ("validate", "apply") and args.source is None:
        parser.error("--source is required")
    if args.operation == "command" and not args.kind:
        parser.error("--kind is required")
    runtime = ROOT / ".tools/dotnet/dotnet.exe"
    env = os.environ.copy()
    if runtime.exists():
        env["DOTNET_ROOT"] = str(runtime.parent)
    env.setdefault("ANTMIND_PIPE", "antmind-v1")
    process = subprocess.Popen(
        [str(runtime) if runtime.exists() else "dotnet", str(ROOT / "src/AntMind.Mcp/bin/Debug/net10.0/AntMind.Mcp.dll")],
        cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL, text=True, encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW)
    replies = queue.Queue()

    def receive():
        for line in process.stdout:
            replies.put(json.loads(line))
        replies.put(None)

    threading.Thread(target=receive, daemon=True).start()
    counter = 0

    def send(message):
        process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
        process.stdin.flush()

    def rpc(method, params):
        nonlocal counter
        counter += 1
        request_id = counter
        send(dict(jsonrpc="2.0", id=request_id, method=method, params=params))
        while True:
            message = replies.get(timeout=20)
            if message is None:
                raise RuntimeError("MCP bridge exited")
            if message.get("id") != request_id:
                continue
            if "error" in message:
                raise RuntimeError(message["error"])
            return message["result"]

    def call(name, arguments):
        result = rpc("tools/call", dict(name=name, arguments=arguments))
        if result.get("isError"):
            raise RuntimeError(result)
        return json.loads("".join(x.get("text", "") for x in result["content"] if x["type"] == "text"))

    try:
        rpc("initialize", dict(protocolVersion="2025-06-18", capabilities={}, clientInfo=dict(name="antmind-live-agent", version="1")))
        send(dict(jsonrpc="2.0", method="notifications/initialized"))
        source = args.source.read_text(encoding="utf-8") if args.source else ""
        if args.operation in ("apply", "validate"):
            result = call("validate_program", dict(source=source))
            if not result.get("valid"):
                raise RuntimeError(result)
        if args.operation == "validate":
            output = result
        elif args.operation == "group":
            output = call("inspect_group", dict(groupId=args.group))
        elif args.operation == "inspect":
            output = call("inspect_object", dict(kind=args.kind, id=args.id))
        elif args.operation == "observe":
            output = call("observe", {})
        else:
            for _ in range(5):
                observed = call("observe", {})
                if observed.get("Error") or not observed.get("Snapshot"):
                    raise RuntimeError(observed)
                result = call("command", dict(requestId=str(uuid.uuid4()), expectedRevision=observed["Snapshot"]["Revision"],expectedWorldId=observed["Snapshot"]["WorldId"],
                    kind="ApplyProgram" if args.operation == "apply" else args.kind, value=args.value,
                    reason=args.reason, species=args.species, programSource=source,
                    groupId=args.group,antId=args.ant,name=args.name,task=args.task,
                    activity=args.activity,foodFilter=args.food,radiusCells=args.radius,
                    antIds=[int(x) for x in args.ants.split(",")] if args.ants else None,key=args.key,
                    **({"pointX":int(args.point.split(",")[0]),"pointY":int(args.point.split(",")[1])} if args.point else {}),
                    **{key+axis: int(value) for key in ("rally","objective","retreat")
                       if getattr(args,key) for axis,value in zip(("X","Y"),getattr(args,key).split(","))}))
                if result.get("Result", {}).get("Code") != "revision_conflict":
                    break
            if not result.get("Result", {}).get("Accepted"):
                raise RuntimeError(result)
            output = result
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
        if output.get("Snapshot"):
            view = output["Snapshot"]
            summary = {key: view[key] for key in ("SchemaVersion", "Revision", "Tick", "Paused", "Speed", "Visibility", "Inventory", "Delivered", "BrainLevel", "NetworkLevel", "QueenRangeCells")}
            summary["Milestones"] = view.get("Milestones", [])
            summary["AgentConnected"] = view["AgentConnected"]
            summary["Resources"] = view["Resources"]
            summary["QueenResources"] = view["QueenResources"]
            summary["Programs"] = view["Programs"]
            summary["Ants"] = [{key: ant[key] for key in ("Id", "Species", "Position", "Carry", "ProgramVersion", "LastError", "LastInstructionSpent", "LastDecisionBranch", "QueenConnected")} for ant in view["Ants"]]
            output = summary
        print(json.dumps(output, ensure_ascii=False, indent=2), flush=True)
        if args.hold_seconds:
            threading.Event().wait(args.hold_seconds)
    finally:
        try:
            process.stdin.close()
            process.wait(timeout=5)
        except (subprocess.TimeoutExpired, BrokenPipeError):
            process.kill()
            process.wait()


if __name__ == "__main__":
    main()
