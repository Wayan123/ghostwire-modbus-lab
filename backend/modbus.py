"""Minimal read-only Modbus/TCP transport for the local training simulator.

Only FC03 and FC04 are implemented. No client target address is accepted.
"""
import socket
import socketserver
import struct
import threading
import time


def _receive(sock, count):
    result = bytearray()
    while len(result) < count:
        part = sock.recv(count - len(result))
        if not part:
            raise ConnectionError('Connection closed during Modbus frame')
        result.extend(part)
    return bytes(result)


class _Handler(socketserver.BaseRequestHandler):
    def handle(self):
        self.request.settimeout(1.0)
        try:
            header = _receive(self.request, 7)
            transaction, protocol, length, unit = struct.unpack('>HHHB', header)
            if protocol != 0 or length < 2 or length > 254:
                return
            pdu = _receive(self.request, length - 1)
            if unit != self.server.unit:
                # This simulator models a gateway that silently ignores a wrong unit ID.
                time.sleep(0.6)
                return
            function = pdu[0]
            if function not in (3, 4):
                reply = bytes((function | 0x80, 1))
            elif len(pdu) != 5:
                reply = bytes((function | 0x80, 3))
            else:
                address, quantity = struct.unpack('>HH', pdu[1:])
                registers = self.server.registers.get(function, {})
                if quantity < 1 or quantity > 125:
                    reply = bytes((function | 0x80, 3))
                elif address + quantity > 65536 or any(i not in registers for i in range(address, address + quantity)):
                    reply = bytes((function | 0x80, 2))
                else:
                    values = [registers[i] for i in range(address, address + quantity)]
                    reply = bytes((function, quantity * 2)) + struct.pack('>' + 'H' * quantity, *values)
            self.request.sendall(struct.pack('>HHHB', transaction, 0, len(reply) + 1, unit) + reply)
        except (OSError, ConnectionError, struct.error):
            return


class _Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = False
    daemon_threads = True


class Simulator:
    """A read-only simulator bound exclusively to IPv4 loopback and an ephemeral port."""
    def __init__(self, unit, registers):
        self.server = _Server(('127.0.0.1', 0), _Handler)
        self.server.unit = unit
        self.server.registers = registers
        self.port = self.server.server_address[1]
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


def read_registers(port, unit, function, address, quantity, timeout=0.3):
    """Read from the controlled loopback simulator; return status, values, exception code."""
    request = struct.pack('>HHHBBHH', 1, 0, 6, unit, function, address, quantity)
    try:
        with socket.create_connection(('127.0.0.1', port), timeout=timeout) as sock:
            sock.settimeout(timeout)
            sock.sendall(request)
            header = _receive(sock, 7)
            transaction, protocol, length, response_unit = struct.unpack('>HHHB', header)
            if transaction != 1 or protocol != 0 or response_unit != unit or not 2 <= length <= 254:
                raise ValueError('Invalid Modbus response header')
            pdu = _receive(sock, length - 1)
            if pdu[0] == function | 0x80 and len(pdu) == 2:
                return 'exception', [], pdu[1]
            if pdu[0] != function or len(pdu) != 2 + quantity * 2 or pdu[1] != quantity * 2:
                raise ValueError('Invalid Modbus response data')
            return 'response', list(struct.unpack('>' + 'H' * quantity, pdu[2:])), None
    except socket.timeout:
        return 'timeout', [], None
    except (OSError, ConnectionError, ValueError, struct.error):
        return 'error', [], None
