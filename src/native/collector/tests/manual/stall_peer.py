"""A WebSocket peer that takes the upgrade and then reads nothing: the manual check of #1848 step 6.

Test-only, never shipped; see docs/test/NativeWsStalledPeer.md.

usage: python -I stall_peer.py <port> <certdir|plain> <hold_seconds>
Serves every connection on a thread of its own: answers the upgrade with 101 (echoing an offered
subprotocol), reads nothing for <hold_seconds>, then reads to the end of the stream and closes. With a
certdir, TLS with <certdir>/cert.pem and <certdir>/key.pem (Python's ssl module); "plain" for ws://.
Listens on 127.0.0.1 only.
"""
import base64
import hashlib
import socket
import ssl
import sys
import threading
import time

port, certdir, hold = int(sys.argv[1]), sys.argv[2], float(sys.argv[3])
context = None
if certdir != 'plain':
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(certdir + '/cert.pem', certdir + '/key.pem')


def serve(conn):
    try:
        if context:
            conn = context.wrap_socket(conn, server_side=True)
        head = b''
        while b'\r\n\r\n' not in head:
            chunk = conn.recv(1)
            if not chunk:
                return
            head += chunk
        lines = head.decode('latin-1').split('\r\n')
        headers = {l.split(':', 1)[0].strip().lower(): l.split(':', 1)[1].strip() for l in lines[1:] if ':' in l}
        accept = base64.b64encode(hashlib.sha1((headers['sec-websocket-key'] + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest()).decode()
        reply = 'HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n'
        if 'sec-websocket-protocol' in headers:
            reply += 'Sec-WebSocket-Protocol: ' + headers['sec-websocket-protocol'] + '\r\n'
        conn.sendall((reply + '\r\n').encode())
        time.sleep(hold)
        while conn.recv(65536):
            pass
    except Exception as error:
        print('connection ended:', type(error).__name__, flush=True)
    finally:
        conn.close()


listener = socket.socket()
listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
listener.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 64 * 1024)
listener.bind(('127.0.0.1', port))
listener.listen(16)
print('listening on', port, 'tls' if context else 'plain', 'hold', hold, flush=True)
while True:
    accepted, _ = listener.accept()
    accepted.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 64 * 1024)
    threading.Thread(target=serve, args=(accepted,), daemon=True).start()
