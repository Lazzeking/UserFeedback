#!/usr/bin/env python3
"""
A server for trying UserFeedback's http destination on your own computer, see docs/http-protocol.md.

    tools/feedback-test-server.py [port] [folder]

Listens on http://localhost:8080/userfeedback/v1 by default, the url the example mod uses. Each report is
saved in its own folder under ./received-reports and summed up in the terminal. Never put it on the internet:
it checks nothing and keeps everything.
"""
import email.parser
import email.policy
import json
import os
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
FOLDER = sys.argv[2] if len(sys.argv) > 2 else "received-reports"
PATH = "/userfeedback/v1"
# Show each line right away, also when the output goes to a file or another program
sys.stdout.reconfigure(line_buffering=True)
MAX_BYTES = 20 * 1024 * 1024
count = 0


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        global count
        if self.path != PATH:
            return self.answer(404, {"message": f"post to {PATH}"})

        length = int(self.headers.get("Content-Length", 0))
        if length > MAX_BYTES:
            return self.answer(413, {"message": "too large"})

        # Parse the multipart body with the email parser, it understands the same format
        raw = b"Content-Type: " + self.headers["Content-Type"].encode() + b"\r\n\r\n" + self.rfile.read(length)
        message = email.parser.BytesParser(policy=email.policy.HTTP).parsebytes(raw)
        if not message.is_multipart():
            return self.answer(400, {"message": "expected multipart/form-data"})

        count += 1
        report_id = f"T{count:03d}"
        folder = os.path.join(FOLDER, f"{time.strftime('%Y-%m-%d_%H-%M-%S')}_{report_id}")
        os.makedirs(folder, exist_ok=True)

        print(f"\n=== {report_id} from {self.headers.get('User-Agent')}, mod {self.headers.get('X-UserFeedback-Mod')!r}")
        for part in message.iter_parts():
            name = part.get_param("name", header="content-disposition")
            filename = os.path.basename(part.get_filename() or name)
            data = part.get_payload(decode=True) or b""
            with open(os.path.join(folder, filename), "wb") as f:
                f.write(data)
            print(f"  {name}: {filename}, {len(data)} bytes")
            if name == "report":
                report = json.loads(data)
                print(f"  rating {report.get('Rating')}, mod {report.get('ModId')} {report.get('ModVersion')}: {report.get('Text', '')[:100]!r}")

        print(f"  saved in {folder}")
        self.answer(201, {"id": report_id, "message": "Received by the test server"})

    def answer(self, status, body):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format, *args):
        pass


print(f"Waiting for reports on http://localhost:{PORT}{PATH}, saving them in {os.path.abspath(FOLDER)}")
ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
