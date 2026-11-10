"""GhostWire local training API and static frontend server.

Run from the project root with: python3 -m backend.app
"""
import json
import math
import os
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.error import URLError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

from .modbus import Simulator, read_registers


FRONTEND = Path(__file__).resolve().parent.parent / 'frontend'
SCENARIOS = [
    {
        'id': 'pressure-scaling',
        'title': 'The Giant Pressure',
        'summary': 'A pressure reading responds, but its engineering value looks wrong.',
        'initial': {'unit_id': 7, 'function_code': 3, 'address': 10, 'quantity': 1, 'scale': 1.0},
        'documentation': ['Simulated device: unit 7, holding register offset 10.',
                          'The simulated pressure register uses a multiplier of 0.01 bar per count.'],
        'unit': 7, 'registers': {3: {10: 39}}, 'solution': {'function_code': 3, 'address': 10, 'quantity': 1, 'scale': 0.01},
    },
    {
        'id': 'unit-id',
        'title': 'The Silent Sensor',
        'summary': 'A read times out despite a running simulated gateway.',
        'initial': {'unit_id': 1, 'function_code': 4, 'address': 0, 'quantity': 1, 'scale': 1.0},
        'documentation': ['Simulated gateway routes this device at unit 17.',
                          'The simulated device publishes its value at input register offset 0.',
                          'In this simulation only, a unit mismatch is silently ignored.'],
        'unit': 17, 'registers': {4: {0: 73}}, 'solution': {'function_code': 4, 'address': 0, 'quantity': 1},
    },
    {
        'id': 'register-addressing',
        'title': 'The Wrong Register',
        'summary': 'The device responds with an illegal data address exception.',
        'initial': {'unit_id': 3, 'function_code': 4, 'address': 21, 'quantity': 1, 'scale': 1.0},
        'documentation': ['Simulated device: unit 3, input register offset 20.',
                          'This exercise uses zero-based protocol offsets, not one-based register labels.'],
        'unit': 3, 'registers': {4: {20: 125}}, 'solution': {'function_code': 4, 'address': 20, 'quantity': 1},
    },
]


def _validate(payload):
    fields = {'scenario_id', 'unit_id', 'function_code', 'address', 'quantity', 'scale'}
    if not isinstance(payload, dict) or set(payload) != fields:
        raise ValueError('Expected exactly scenario_id, unit_id, function_code, address, quantity and scale')
    if not isinstance(payload['scenario_id'], str) or payload['scenario_id'] not in {s['id'] for s in SCENARIOS}:
        raise ValueError('Unknown scenario_id')
    for key, low, high in (('unit_id', 1, 247), ('function_code', 3, 4), ('address', 0, 65535), ('quantity', 1, 125)):
        value = payload[key]
        if type(value) is not int or not low <= value <= high:
            raise ValueError(f'{key} is outside its allowed range')
    if payload['function_code'] not in (3, 4) or payload['address'] + payload['quantity'] > 65536:
        raise ValueError('Invalid function or register range')
    scale = payload['scale']
    if type(scale) not in (int, float) or not 0 < scale <= 1000000 or not math.isfinite(scale):
        raise ValueError('scale must be finite, positive and at most 1000000')
    return dict(payload)


def _explain(evidence):
    """Optional provider text is advisory; it never controls protocol results."""
    base = os.environ.get('GHOSTWIRE_AI_BASE_URL', '').rstrip('/')
    key = os.environ.get('GHOSTWIRE_AI_API_KEY', '')
    model = os.environ.get('GHOSTWIRE_AI_MODEL', '')
    fallback = ('This is simulated training evidence. ' + ' '.join(evidence['observations']) +
                ' ' + ' '.join(evidence['next_steps']))
    if not (base and key and model):
        return fallback, 'deterministic'
    try:
        parsed = urlsplit(base)
        if parsed.scheme not in ('http', 'https') or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
            return fallback, 'deterministic'
        body = json.dumps({
            'model': model,
            'messages': [
                {'role': 'system', 'content': 'Explain this simulated Modbus training evidence concisely. Never claim real-device access. Distinguish observed facts from hypotheses; do not change or invent protocol results.'},
                {'role': 'user', 'content': json.dumps(evidence, allow_nan=False)},
            ],
            'temperature': 0.2,
        }).encode('utf-8')
        request = Request(base + '/chat/completions', data=body,
                          headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + key}, method='POST')
        with urlopen(request, timeout=4) as response:
            data = response.read(65537)
        if len(data) > 65536:
            return fallback, 'deterministic'
        content = json.loads(data)['choices'][0]['message']['content']
        if isinstance(content, str) and content.strip():
            return content.strip()[:4000], 'openai-compatible'
    except (OSError, URLError, ValueError, KeyError, IndexError, TypeError):
        pass
    return fallback, 'deterministic'


class Application:
    def __init__(self):
        self.simulators = {}
        try:
            for scenario in SCENARIOS:
                self.simulators[scenario['id']] = Simulator(scenario['unit'], scenario['registers'])
        except Exception:
            self.close()
            raise

    def close(self):
        for simulator in self.simulators.values():
            simulator.close()
        self.simulators.clear()

    def scenarios(self):
        return [{key: scenario[key] for key in ('id', 'title', 'summary', 'initial', 'documentation')} for scenario in SCENARIOS]

    def test(self, payload):
        request = _validate(payload)
        scenario = next(item for item in SCENARIOS if item['id'] == request['scenario_id'])
        status, raw, exception = read_registers(self.simulators[scenario['id']].port, request['unit_id'],
                                                request['function_code'], request['address'], request['quantity'])
        value = raw[0] * request['scale'] if raw else None
        verified = (status == 'response' and request['unit_id'] == scenario['unit'] and
                    all(request[key] == expected for key, expected in scenario['solution'].items()))
        if status == 'response':
            observations = [f'Simulated FC{request["function_code"]:02d} response returned {len(raw)} register(s): {raw}.',
                            f'The first raw register multiplied by the selected scale is {value:g}.']
            causes = [] if verified else ['A successful read does not by itself confirm scaling, register choice or interpretation.']
            next_steps = ['Compare the setting and interpreted value with the simulated device documentation.'] if not verified else ['The expected scenario settings and a successful response are verified.']
        elif status == 'timeout':
            observations = ['The simulated Modbus request timed out without a response.']
            causes = ['A unit ID mismatch is one possible cause; timeouts alone do not prove it.',
                      'A connection issue or unavailable device could also cause a timeout.']
            next_steps = ['Compare the unit ID with the simulated gateway documentation and retest.']
        elif status == 'exception':
            observations = [f'The simulated device returned Modbus exception code {exception:02d}.']
            causes = ['The requested register range may not exist for this function.'] if exception == 2 else ['The simulator rejected the request.']
            next_steps = ['Compare function code and zero-based address with the simulated device documentation.']
        else:
            observations = ['The local Modbus transport failed or returned an invalid frame.']
            causes = ['The simulator or local connection may be unavailable.']
            next_steps = ['Restart the local application and retry.']
        observations.insert(0, f'Simulated request: unit ID {request["unit_id"]}, function {request["function_code"]}, address {request["address"]}, quantity {request["quantity"]}.')
        hints = ['Training hint: ' + scenario['documentation'][0]]
        result = {'status': status, 'raw_registers': raw, 'engineering_value': value,
                  'observations': observations, 'possible_causes': causes, 'training_hints': hints,
                  'next_steps': next_steps, 'verified': verified, 'request': request}
        if scenario['id'] == 'pressure-scaling':
            result['unit'] = 'bar'
        result['explanation'], result['explanation_source'] = _explain(result)
        return result


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, app, **kwargs):
        self.app = app
        super().__init__(*args, directory=str(FRONTEND), **kwargs)

    def _json(self, status, data):
        body = json.dumps(data, allow_nan=False).encode('utf-8')
        self.send_response(status)
        self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(body)

    def _local_host(self):
        host = self.headers.get('Host', '')
        return host in (f'127.0.0.1:{self.server.server_port}', f'localhost:{self.server.server_port}')

    def do_GET(self):
        if not self._local_host():
            self.send_error(403)
        elif urlsplit(self.path).path == '/api/scenarios':
            self._json(200, {'scenarios': self.app.scenarios()})
        elif urlsplit(self.path).path.startswith('/api/'):
            self._json(404, {'error': 'Not found'})
        else:
            super().do_GET()

    def do_POST(self):
        if not self._local_host():
            self._json(403, {'error': 'Forbidden host'})
            return
        origin = self.headers.get('Origin')
        if origin and origin not in (f'http://127.0.0.1:{self.server.server_port}', f'http://localhost:{self.server.server_port}'):
            self._json(403, {'error': 'Cross-origin requests are not allowed'})
            return
        if self.path != '/api/test':
            self._json(404, {'error': 'Not found'})
            return
        if self.headers.get_content_type() != 'application/json':
            self._json(415, {'error': 'Content-Type must be application/json'})
            return
        try:
            length = int(self.headers.get('Content-Length', ''))
            if length < 1 or length > 16384:
                raise ValueError('Invalid request size')
            payload = json.loads(self.rfile.read(length))
            result = self.app.test(payload)
        except (ValueError, UnicodeDecodeError, json.JSONDecodeError) as exc:
            self._json(400, {'error': str(exc)})
            return
        self._json(200, result)


def create_http_server(app, port=8000):
    return ThreadingHTTPServer(('127.0.0.1', port), partial(Handler, app=app))


def main():
    app = Application()
    try:
        server = create_http_server(app, int(os.environ.get('GHOSTWIRE_PORT', '8000')))
        try:
            print(f'GhostWire running at http://127.0.0.1:{server.server_port}', flush=True)
            server.serve_forever()
        except KeyboardInterrupt:
            pass
        finally:
            server.server_close()
    finally:
        app.close()


if __name__ == '__main__':
    main()
