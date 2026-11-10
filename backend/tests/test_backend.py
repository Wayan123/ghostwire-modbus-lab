import json
import os
import socket
import struct
import threading
import unittest
from http.client import HTTPConnection
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from unittest.mock import patch

from backend.app import Application, create_http_server
from backend.modbus import Simulator, read_registers


class ModbusTests(unittest.TestCase):
    def test_fc03_fc04_and_address_exception_over_tcp(self):
        with Simulator(7, {3: {10: 39}, 4: {20: 123}}) as simulator:
            self.assertEqual(read_registers(simulator.port, 7, 3, 10, 1), ("response", [39], None))
            self.assertEqual(read_registers(simulator.port, 7, 4, 20, 1), ("response", [123], None))
            self.assertEqual(read_registers(simulator.port, 7, 3, 11, 1), ("exception", [], 2))
            self.assertEqual(read_registers(simulator.port, 8, 3, 10, 1, timeout=0.1)[0], "timeout")

    def test_simulator_rejects_writes(self):
        with Simulator(7, {3: {10: 39}}) as simulator:
            with socket.create_connection(("127.0.0.1", simulator.port), timeout=1) as sock:
                sock.sendall(struct.pack(">HHHBBHH", 1, 0, 6, 7, 6, 10, 400))
                response = sock.makefile('rb').read(9)
                self.assertEqual(response, struct.pack(">HHHBBB", 1, 0, 3, 7, 0x86, 1))
            self.assertEqual(read_registers(simulator.port, 7, 3, 10, 1)[1], [39])


class ApplicationTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(os.environ, {'GHOSTWIRE_AI_BASE_URL': '', 'GHOSTWIRE_AI_API_KEY': '', 'GHOSTWIRE_AI_MODEL': ''})
        environment.start()
        self.addCleanup(environment.stop)
        self.app = Application()
        self.addCleanup(self.app.close)
        self.scenarios = {s['id']: s for s in self.app.scenarios()}

    def test_case_titles(self):
        self.assertEqual([s['title'] for s in self.app.scenarios()],
                         ['The Giant Pressure', 'The Silent Sensor', 'The Wrong Register'])

    def run_case(self, scenario_id, **changes):
        return self.app.test({"scenario_id": scenario_id, **self.scenarios[scenario_id]["initial"], **changes})

    def test_scaling_is_observed_not_manufactured(self):
        result = self.run_case('pressure-scaling', scale=0.01)
        self.assertEqual(result['status'], 'response')
        self.assertEqual(result['raw_registers'], [39])
        self.assertAlmostEqual(result['engineering_value'], 0.39)
        self.assertEqual(result['unit'], 'bar')
        self.assertTrue(result['verified'])
        self.assertEqual(result['explanation_source'], 'deterministic')

    def test_unit_timeout_then_recovery(self):
        timeout = self.run_case('unit-id')
        self.assertEqual(timeout['status'], 'timeout')
        self.assertIn('unit ID', ' '.join(timeout['possible_causes']))
        self.assertIn('connection', ' '.join(timeout['possible_causes']))
        self.assertIn('device', ' '.join(timeout['possible_causes']))
        self.assertIn('unit ID 1, function 4, address 0, quantity 1', ' '.join(timeout['observations']))
        recovered = self.run_case('unit-id', unit_id=17)
        self.assertEqual(recovered['status'], 'response')
        self.assertTrue(recovered['verified'])

    def test_address_exception_then_recovery(self):
        self.assertEqual(self.run_case('register-addressing')['status'], 'exception')
        recovered = self.run_case('register-addressing', address=20)
        self.assertEqual(recovered['status'], 'response')
        self.assertTrue(recovered['verified'])

    def test_validation_rejects_untrusted_target_and_bad_values(self):
        for change in ({'host': 'example.com'}, {'unit_id': True}, {'function_code': 6},
                       {'address': 65535, 'quantity': 2}, {'scale': float('nan')}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.run_case('pressure-scaling', **change)

    def test_provider_unavailable_falls_back_without_changing_protocol_result(self):
        with patch.dict(os.environ, {'GHOSTWIRE_AI_BASE_URL': 'http://127.0.0.1:1/v1',
                                    'GHOSTWIRE_AI_API_KEY': 'test', 'GHOSTWIRE_AI_MODEL': 'model'}):
            result = self.run_case('pressure-scaling', scale=0.01)
        self.assertEqual(result['explanation_source'], 'deterministic')
        self.assertEqual(result['raw_registers'], [39])
        self.assertAlmostEqual(result['engineering_value'], 0.39)

    def test_provider_explanation_does_not_replace_protocol_evidence(self):
        captured = {}

        class Provider(BaseHTTPRequestHandler):
            def do_POST(self):
                captured['path'] = self.path
                captured['body'] = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                body = json.dumps({'choices': [{'message': {'content': 'Advisory explanation'}}]}).encode()
                self.send_response(200)
                self.send_header('Content-Length', str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *args):
                pass

        server = ThreadingHTTPServer(('127.0.0.1', 0), Provider)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            with patch.dict(os.environ, {'GHOSTWIRE_AI_BASE_URL': f'http://127.0.0.1:{server.server_port}/v1',
                                        'GHOSTWIRE_AI_API_KEY': 'test', 'GHOSTWIRE_AI_MODEL': 'model'}):
                result = self.run_case('pressure-scaling', scale=0.01)
        finally:
            server.shutdown()
            server.server_close()
            thread.join()
        self.assertEqual(result['explanation_source'], 'openai-compatible')
        self.assertEqual(result['explanation'], 'Advisory explanation')
        self.assertEqual(result['raw_registers'], [39])
        self.assertEqual(captured['path'], '/v1/chat/completions')
        evidence = json.loads(captured['body']['messages'][1]['content'])
        self.assertEqual(evidence['raw_registers'], [39])


class HttpTests(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(os.environ, {'GHOSTWIRE_AI_BASE_URL': '', 'GHOSTWIRE_AI_API_KEY': '', 'GHOSTWIRE_AI_MODEL': ''})
        environment.start()
        self.addCleanup(environment.stop)
        self.app = Application()
        self.server = create_http_server(self.app, port=0)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.app.close)
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)

    def request(self, method, path, body=None, headers=None):
        connection = HTTPConnection('127.0.0.1', self.server.server_port, timeout=3)
        connection.request(method, path, body, headers or {})
        response = connection.getresponse()
        data = response.read()
        connection.close()
        return response.status, data

    def test_scenarios_and_validation_over_http(self):
        status, body = self.request('GET', '/api/scenarios')
        self.assertEqual(status, 200)
        scenarios = json.loads(body)['scenarios']
        self.assertEqual(len(scenarios), 3)
        payload = {'scenario_id': scenarios[0]['id'], **scenarios[0]['initial']}
        status, body = self.request('POST', '/api/test', json.dumps(payload), {'Content-Type': 'application/json'})
        self.assertEqual(status, 200)
        self.assertIn(json.loads(body)['status'], ('response', 'timeout', 'exception'))
        payload['host'] = '192.0.2.1'
        status, _ = self.request('POST', '/api/test', json.dumps(payload), {'Content-Type': 'application/json'})
        self.assertEqual(status, 400)

    def test_cross_origin_post_is_rejected(self):
        status, _ = self.request('POST', '/api/test', '{}', {'Content-Type': 'application/json', 'Origin': 'https://example.com'})
        self.assertEqual(status, 403)


if __name__ == '__main__':
    unittest.main()
