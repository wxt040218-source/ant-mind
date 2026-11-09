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
    parser.add_argument("operation", choices=["observe", "validate", "apply", "command", "inspect", "group", "summary", "events", "watch", "configure-group", "preview", "preflight"])
    parser.add_argument("--config",type=Path,help="Full configure_group JSON arguments")
    parser.add_argument("--after",type=int,default=0)
    parser.add_argument("--wait",type=int,default=0)
    parser.add_argument("--watch-id",default="")
    parser.add_argument("--watch-action",choices=["list","upsert","remove"],default="list")
    parser.add_argument("--kinds",default="")
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
    if not 0<=args.wait<=30:parser.error("--wait must be 0..30")
    if args.operation=="configure-group" and args.config is None:parser.error("--config is required")
    if args.operation in ("preview","preflight") and args.source is None:parser.error("--source is required")
    if args.operation=="apply" and args.group!=0:
        parser.error("All groups share species code; do not use --group when applying programs")
    if args.operation in ("validate", "apply") and args.source is None:
        parser.error("--source is required")
    if args.operation == "command" and not args.kind:
        parser.error("--kind is required")
    dotnet_exe = "dotnet.exe" if os.name == "nt" else "dotnet"
    runtime = ROOT / ".tools/dotnet" / dotnet_exe
    env = os.environ.copy()
    if runtime.exists():
        env["DOTNET_ROOT"] = str(runtime.parent)
    env.setdefault("ANTMIND_PIPE", "antmind-v1")
    # creationflags 只在 Windows 有效；Linux 下传非零值会抛 ValueError
    popen_kwargs = dict(cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                        stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
    if os.name == "nt":
        popen_kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
    process = subprocess.Popen(
        [str(runtime) if runtime.exists() else dotnet_exe, str(ROOT / "src/AntMind.Mcp/bin/Debug/net10.0/AntMind.Mcp.dll")],
        **popen_kwargs)
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
            message = replies.get(timeout=max(20,args.wait+10))
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
        if args.operation=="summary":
            output=call("observe_summary",{})
        elif args.operation in ("events","watch","configure-group","preview","preflight"):
            summary=call("observe_summary",{}).get("Summary")
            if not summary:raise RuntimeError("Missing current game summary")
            world=summary["WorldId"]
            if args.operation=="events":output=call("read_events",dict(worldId=world,after=args.after,watchId=args.watch_id,timeoutSeconds=args.wait))
            elif args.operation=="watch":
                settings=dict(worldId=world,action=args.watch_action,id=args.watch_id,name=args.name or "Agent watch",radiusCells=args.radius or 8)
                if args.kinds:settings["kinds"]=args.kinds.split(",")
                if args.point:settings.update(zip(("centerX","centerY"),map(int,args.point.split(","))))
                output=call("watch_events",settings)
            elif args.operation=="preview":output=call("preview_program",dict(species=args.species,source=source,antId=args.ant))
            elif args.operation=="preflight":output=call("preview_program_batch",dict(species=args.species,source=source))
            else:
                settings=json.loads(args.config.read_text(encoding="utf-8-sig"))
                for attempt in range(5):
                    summary=call("observe_summary",{})["Summary"]
                    output=call("configure_group",dict(settings,requestId=str(uuid.uuid4()),expectedRevision=summary["Revision"],expectedWorldId=summary["WorldId"]))
                    if output.get("Result",{}).get("Code")!="revision_conflict":break
                if not output.get("Result",{}).get("Accepted"):raise RuntimeError(output)
        if args.operation in ("apply", "validate"):
            result = call("validate_program", dict(source=source))
            if not result.get("valid"):
                raise RuntimeError(result)
        if args.operation in ("summary","events","watch","configure-group","preview","preflight"):pass
        elif args.operation == "validate":
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
