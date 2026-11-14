# VERSION=0.1.0
# 变更摘要：
#   0.1.0（2026-11-14）纳入语义化版本；真桥启动前等待 Evaluator --serve 就绪，消除 observe 抢跑竞态。
"""Real Linux/Windows headless regression: isolated host, official MCP, valid/invalid DSL."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import uuid

root = Path(__file__).resolve().parents[1]
env = dict(os.environ, ANTMIND_PIPE='antmind-eval-check-' + uuid.uuid4().hex[:12])
with tempfile.TemporaryDirectory() as tmp:
    folder = Path(tmp)
    invalid = folder / 'invalid.ant'
    invalid.write_text('this is not valid ant DSL !!!', encoding='utf-8')
    with (folder / 'host.log').open('w') as log:
        host = subprocess.Popen(['dotnet', str(root / 'tools/AntMind.Evaluator/bin/Debug/net10.0/AntMind.Evaluator.dll'), '--serve'], cwd=root, env=env, stdout=log, stderr=log)
        try:
            host_path = folder / 'host.log'
            deadline = time.monotonic() + 10
            while time.monotonic() < deadline:
                if host.poll() is not None:
                    raise RuntimeError('Evaluator host exited before readiness: exit %s' % host.poll())
                if 'AntMind headless host' in host_path.read_text(encoding='utf-8', errors='replace'):
                    break
                time.sleep(0.05)
            else:
                raise RuntimeError('Evaluator host did not become ready within 10 seconds')
            time.sleep(0.2)  # PipeServer binds just after the readiness line; avoid a startup race.
            run = subprocess.run([sys.executable, str(root / 'scripts/model-eval-bridge.py'), '--programs', str(root / 'content/programs/hauler-reliable.ant'), str(invalid), '--baseline', 'none', '--matrix', '--ticks', '600', '1200', '--out', str(folder / 'result')], cwd=root, env=env, timeout=180)
            if run.returncode:
                raise RuntimeError('Real bridge failed: exit %s' % run.returncode)
            report = json.loads((folder / 'result.json').read_text(encoding='utf-8'))
            cells = report['cells']
            assert len(cells) == 4
            assert report['determinism']['passed'] == 4
            assert len({c['slot'] for c in cells}) == 1
            valid = [c for c in cells if c.get('valid') is True]
            invalid_cells = [c for c in cells if c.get('valid') is False]
            assert len(valid) == len(invalid_cells) == 2
            assert all(c['rankInGroup'] == 1 and c.get('score') is not None for c in valid)
            assert all(c['rankInGroup'] is None and c['error'] == 'invalid_program' for c in invalid_cells)
            print('PASS real model-eval bridge: 4 cells, 4 deterministic, 2 valid / 2 invalid, shared snapshot')
        finally:
            host.terminate()
            try:
                host.wait(timeout=10)
            except subprocess.TimeoutExpired:
                host.kill()
                host.wait()
