"""Run an explicit Agent-planned production demonstration through official STDIO MCP.

Requires a paused, fresh desktop world. Only discovered source coordinates are used.
No stock, materials, food or positions are injected. Leaves a paused saved demonstration.
"""
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import uuid

ROOT = Path(__file__).resolve().parents[1]


def main():
    env = os.environ.copy()
    dotnet_exe = "dotnet.exe" if os.name == "nt" else "dotnet"
    exe = ROOT / ".tools/dotnet" / dotnet_exe
    popen_kwargs = dict(cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                        stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
    if os.name == "nt":
        popen_kwargs["creationflags"] = subprocess.CREATE_NO_WINDOW
    process = subprocess.Popen([str(exe) if exe.exists() else dotnet_exe,
        str(ROOT / "src/AntMind.Mcp/bin/Debug/net10.0/AntMind.Mcp.dll")], **popen_kwargs)
    replies = queue.Queue()
    def receive():
        for line in process.stdout:
            replies.put(json.loads(line))
        replies.put(None)
    threading.Thread(target=receive, daemon=True).start()
    counter = 0
    def rpc(method, params):
        nonlocal counter
        counter += 1
        process.stdin.write(json.dumps(dict(jsonrpc="2.0", id=counter, method=method, params=params), ensure_ascii=False)+"\n")
        process.stdin.flush()
        while True:
            result = replies.get(timeout=30)
            if result is None:
                raise RuntimeError("MCP exited")
            if result.get("id") == counter:
                if "error" in result:
                    raise RuntimeError(result)
                return result["result"]
    def tool(tool_name, **args):
        result = rpc("tools/call", dict(name=tool_name, arguments=args))
        if result.get("isError"):
            raise RuntimeError(result)
        return json.loads("".join(c.get("text", "") for c in result["content"] if c["type"] == "text"))
    def observe():
        return tool("observe")["Snapshot"]
    state = None
    def command(kind, **args):
        nonlocal state
        r = tool("command", requestId=str(uuid.uuid4()), expectedRevision=state["Revision"], expectedWorldId=state["WorldId"],
            kind=kind, value=args.pop("value", 0), reason="Agent production-chain demonstration", **args)
        if not r.get("Result", {}).get("Accepted"):
            raise RuntimeError(r)
        state["Revision"] = r["Result"]["Revision"]
        return r["Result"]
    def until(predicate, limit=1800):
        nonlocal state
        for t in range(limit):
            command("Step")
            if t % 10 == 0:
                state = observe()
                errors = [(a["Id"], a["LastError"]) for a in state["Ants"] if a["LastError"]]
                if errors:
                    raise RuntimeError(errors)
                if predicate(state):
                    return
            if t % 200 == 0:
                print("Running at Tick", state["Tick"], flush=True)
        raise RuntimeError("Demonstration stage did not complete: "+json.dumps(state, ensure_ascii=False))
    def group_point(group, key, p):
        command("SetGroupPoint", groupId=group, key=key, pointX=p["X"], pointY=p["Y"])
    def param(group, key, value):
        command("SetGroupParameter", groupId=group, key=key, value=value)
    def route(target, destination, origin, source=0, from_holder=0, item=3):
        group_point(1, "drop", destination)
        group_point(1, "take", origin)
        for key, value in dict(to=target, source=source, **{"from": from_holder}, item=item).items():
            param(1, key, value)
    def save_evidence(label):
        path=ROOT / "artifacts/agent-live" / (label+".json")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    try:
        rpc("initialize",dict(protocolVersion="2025-06-18",capabilities={},clientInfo=dict(name="production-demo-agent",version="1")))
        process.stdin.write(json.dumps(dict(jsonrpc="2.0",method="notifications/initialized"))+"\n")
        process.stdin.flush()
        state=observe()
        if not state["Paused"] or state["Tick"] != 0 or state["Buildings"]:
            raise RuntimeError("Demo requires a fresh paused world and does not reset an existing colony.")
        command("SaveGame",key="before-production-demo")
        fruit=next(s for s in state["Sources"] if s["ItemType"]=="fruit")
        fiber=next(s for s in state["Sources"] if s["ItemType"]=="fiber")
        nest=state["Nest"]
        cache_pos=dict(X=nest["X"],Y=nest["Y"]+2000)
        station_pos=dict(X=nest["X"]+1000,Y=nest["Y"])
        cache=int(command("PlanBuild",key="cache",pointX=cache_pos["X"],pointY=cache_pos["Y"])["Detail"])
        station=int(command("PlanBuild",key="workshop",pointX=station_pos["X"],pointY=station_pos["Y"])["Detail"])
        command("ProduceEgg")
        command("HatchEgg",species="chef")
        command("CreateGroup",name="Production logistics",task="Agent routes physical items",activity="logistics")
        command("AssignAnts",groupId=1,antIds=[a["Id"] for a in state["Ants"] if a["Species"]=="hauler"])
        command("CreateGroup",name="Kitchen",task="Build and process",activity="kitchen",objectiveX=cache_pos["X"],objectiveY=cache_pos["Y"])
        state=observe()
        command("AssignAnts",groupId=2,antIds=[a["Id"] for a in state["Ants"] if a["Species"]=="chef"])
        param(2,"station",cache)
        param(2,"recipe",1)
        source='''if(queen_connected){request_queen_info();}
if(carry>0){
  if(max(abs(x-group_x("drop")),abs(y-group_y("drop")))>1000){move_to(group_x("drop"),group_y("drop"));}
  else{deposit(param("to"),carry_food_type,carry);}
}else{
  if(max(abs(x-group_x("take")),abs(y-group_y("take")))>1000){move_to(group_x("take"),group_y("take"));}
  else{if(param("source")>0){harvest(param("source"),capacity);}else{withdraw(param("from"),param("item"),capacity);}}
}'''
        validation=tool("validate_program",source=source)
        if not validation["valid"]:
            raise RuntimeError(validation)
        command("ApplyProgram",species="hauler",programSource=source)
        route(cache,cache_pos,fiber["Position"],source=fiber["Id"])
        until(lambda s:any(b["Id"]==cache and b["Phase"]=="ready" for b in s["Buildings"]))
        save_evidence("production-cache")
        print("Built cache",state["Tick"],flush=True)
        route(station,station_pos,fiber["Position"],source=fiber["Id"])
        command("UpdateGroup",groupId=2,name="Kitchen",objectiveX=station_pos["X"],objectiveY=station_pos["Y"])
        param(2,"station",station)
        until(lambda s:any(b["Id"]==station and b["Phase"]=="ready" for b in s["Buildings"]))
        save_evidence("production-workshop")
        print("Built workshop",state["Tick"],flush=True)
        # Clear excess construction cargo into cache before changing the payload type.
        route(cache,cache_pos,fiber["Position"],source=fiber["Id"])
        command("AssignAnts",groupId=1,antIds=[a["Id"] for a in state["Ants"] if a["Species"]=="hauler"])
        # Stop new harvest by using empty nest fiber as the origin.
        route(cache,cache_pos,nest,item=3)
        until(lambda s:all(a["Carry"]==0 for a in s["Ants"] if a["Species"]=="hauler"),400)
        # Put newly harvested fruit into the cache, then deliver it to the workshop.
        route(cache,cache_pos,fruit["Position"],source=fruit["Id"],item=1)
        until(lambda s:next(b for b in s["Buildings"] if b["Id"]==cache)["Store"]["Amounts"].get("fruit",0)>=4)
        route(station,station_pos,cache_pos,from_holder=cache,item=1)
        until(lambda s:next(b for b in s["Buildings"] if b["Id"]==station)["Output"]["Amounts"].get("fruit_concentrate",0)>=1)
        save_evidence("production-batch")
        # Allow current fruit loads to settle; then collect outputs and admit them to nest.
        route(station,station_pos,nest,item=3)
        until(lambda s:all(a["Carry"]==0 for a in s["Ants"] if a["Species"]=="hauler"),400)
        route(0,nest,station_pos,from_holder=station,item=4)
        until(lambda s:s["Stock"].get("fruit_concentrate",0)>=1)
        command("SaveGame",key="production-demo")
        state=observe()
        save_evidence("production-complete")
        evaluated=tool("evaluate_save",slot="production-demo",ticks=100)
        (ROOT/"artifacts/agent-live/production-evaluation.json").write_text(json.dumps(evaluated,ensure_ascii=False,indent=2),encoding="utf-8")
        print(json.dumps(dict(tick=state["Tick"],stock=state["Stock"],firstAdmissionPoints=state["FirstAdmissionPoints"],buildings=state["Buildings"],chef=[a for a in state["Ants"] if a["Species"]=="chef"]),ensure_ascii=False,indent=2),flush=True)
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


if __name__=="__main__":
    main()
