import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('model_eval_bridge', Path(__file__).resolve().parents[1] / 'model-eval-bridge.py')
b = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(b)


class BridgeTests(unittest.TestCase):
    def run_bridge(self, results, extra=()):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            program = root / 'valid.ant'
            program.write_text('noop', encoding='utf-8')
            out = root / 'output'
            args = ['bridge', '--programs', str(program), '--baseline', 'none', '--out', str(out), *extra]
            with patch.object(sys, 'argv', args), patch.object(b, 'Bridge'), \
                 patch.object(b, 'git_head', return_value='fixture'), \
                 patch.object(b, 'make_baseline_slot', return_value={'protocolVersion': 19}) as save, \
                 patch.object(b, 'evaluate_one', side_effect=results), \
                 contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                code = b.main()
            return code, json.loads(Path(str(out)+'.json').read_text(encoding='utf-8')), save.call_count

    def test_missing_baseline_rejected_before_start(self):
        with patch.object(sys, 'argv', ['bridge', '--programs', 'unused', '--baseline', '/missing-baseline-regression.ant']), \
             patch.object(b, 'Bridge') as bridge, contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                b.main()
            self.assertEqual(error.exception.code, 2)
            bridge.assert_not_called()

    def test_all_errors_not_success_or_ranked(self):
        code, report, _ = self.run_bridge([{'error': 'host unavailable'}] * 2)
        self.assertEqual(code, 2)
        self.assertEqual(report['determinism']['passed'], 1)
        self.assertIsNone(report['cells'][0]['rankInGroup'])

    def test_zero_is_valid_score(self):
        code, report, _ = self.run_bridge([{'valid': True, 'score': 0}] * 2)
        self.assertEqual(code, 0)
        self.assertEqual(report['cells'][0]['rankInGroup'], 1)
        self.assertIsNone(report['cells'][0]['rank'])
        self.assertIsNone(report['cells'][0]['seed'])

    def test_infrastructure_failure_even_with_score(self):
        code, report, _ = self.run_bridge([{'score': 5, 'infrastructureError': True}] * 2)
        self.assertEqual(code, 2)
        self.assertIsNone(report['cells'][0]['rankInGroup'])

    def test_nondeterminism_fails(self):
        code, _, _ = self.run_bridge([{'score': 1}, {'score': 2}])
        self.assertEqual(code, 4)

    def test_matrix_uses_one_snapshot_and_no_global_rank(self):
        code, report, saves = self.run_bridge([{'score': 1}]*4,
            ['--matrix', '--ticks', '600', '1200'])
        self.assertEqual(code, 0)
        self.assertEqual(saves, 1)
        self.assertEqual(len({c['slot'] for c in report['cells']}), 1)
        self.assertTrue(all(c['rank'] is None for c in report['cells']))
        self.assertEqual([c['rankInGroup'] for c in report['cells']], [1, 1])

    def test_running_dirty_host_rejected(self):
        class Host:
            def call(self, *args):
                return {'Snapshot': {'Tick': 10, 'Paused': False}}
        with self.assertRaises(b.BridgeError):
            b.make_baseline_slot(Host(), 'test', allow_dirty=True)

    def test_invalid_program_is_not_infrastructure_failure(self):
        class Host:
            def call(self, name, args):
                assert name == 'validate_program'
                return {'valid': False, 'error': 'parse_error'}
        result = b.evaluate_one(Host(), 'slot', 1, 'hauler', 'bad')
        self.assertEqual(result['error'], 'invalid_program')
        self.assertFalse(result.get('infrastructureError', False))
        self.assertFalse(b.is_scored(result))

    def test_failure_excluded_from_valid_zero_rank(self):
        code, report, _ = self.run_bridge(
            [{'score': 0}, {'score': 0}, {'error': 'invalid_program', 'valid': False},
             {'error': 'invalid_program', 'valid': False}],
            ['--matrix', '--ticks', '600', '1200'])
        self.assertEqual(code, 0)
        self.assertEqual(report['cells'][0]['rankInGroup'], 1)
        self.assertIsNone(report['cells'][1]['rankInGroup'])

    def test_validation_transport_failure_flagged(self):
        class Host:
            def call(self, name, args):
                raise b.BridgeError('unavailable')
        self.assertTrue(b.evaluate_one(Host(), 'slot', 1, 'hauler', 'noop')['infrastructureError'])


if __name__ == '__main__':
    unittest.main()
