#!/usr/bin/env python3
"""
A server for trying UserFeedback's http destination on your own computer, see docs/http-protocol.md.

    tools/feedback-test-server.py [port] [folder]

Listens on http://localhost:8080/userfeedback/v1 by default, the url the example mod uses. Each report is
saved in its own folder under ./received-reports and summed up in the terminal. Never put it on the internet:
it checks nothing and keeps everything.

It can also stand in for the relay's registry (the list of mods that take reports through it). Set RELAY_MODS, then set
"RelayUrl": "http://localhost:8080/userfeedback/v1" in ModConfig/userfeedback.json and delete
VintagestoryData/UserFeedback/relay-registry-v2.json so it's fetched again:

    RELAY_MODS="userfeedbackexample:public,carryon:bug" tools/feedback-test-server.py
    RELAY_MODS="userfeedbackexample" RELAY_BUCKETS=1 tools/feedback-test-server.py

Each modid can have :public, :bug and :feedback after it, and :works when it counts "works for me" results
(:noreports leaves it out of "mods", so it only counts results). RELAY_BUCKETS (1-4) splits the list into buckets of
that many hex characters. Answers carry an ETag and are gzipped when asked, like the real relay.

"Works for me" results are posted to <RelayUrl>/works, checked like the relay does and printed.
WORKS_ANSWER=429 (or 403, 503, ...) answers that instead of 204, to try how the mod handles it:

    RELAY_MODS="userfeedbackexample:works,othermod:works:noreports" tools/feedback-test-server.py
"""
import email.parser
import email.policy
import gzip
import hashlib
import json
import os
import re
import sys
import time
from urllib.parse import urlsplit
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
FOLDER = sys.argv[2] if len(sys.argv) > 2 else "received-reports"
PATH = "/userfeedback/v1"
# Show each line right away, also when the output goes to a file or another program
sys.stdout.reconfigure(line_buffering=True)
MAX_BYTES = 20 * 1024 * 1024
count = 0


def relay_mods():
    """RELAY_MODS as {modid: {"public": bool, "kinds": [...] or None, "works": bool, "reports": bool}}"""
    mods = {}
    for item in filter(None, os.environ.get("RELAY_MODS", "").split(",")):
        modid, *flags = item.strip().split(":")
        kinds = [flag for flag in flags if flag in ("bug", "feedback")]
        mods[modid] = {"public": "public" in flags, "kinds": kinds or None, "works": "works" in flags, "reports": "noreports" not in flags}
    return mods


def prefix(modid, length):
    # The same as the mod and the relay: sha256 of the lowercased modid, in lowercase hex
    return hashlib.sha256(modid.lower().encode()).hexdigest()[:length]


def compact(mods, body):
    reports = {m: v for m, v in mods.items() if v["reports"]}
    body["mods"] = sorted(reports)
    body["public"] = sorted(m for m, v in reports.items() if v["public"])
    body["kinds"] = {m: v["kinds"] for m, v in reports.items() if v["kinds"]}
    works = sorted(m for m, v in mods.items() if v["works"])
    # Like the relay, only there once a mod counts results
    if works:
        body["works"] = works
    return body


def works_problem(body, header):
    """What the relay would answer 400 for, None when the result is fine (the same rules as WorksResult.cs)"""
    if not isinstance(body, dict) or body.get("FormatVersion") != 1:
        return "FormatVersion must be 1"
    modid = body.get("ModId")
    if not isinstance(modid, str) or not re.match(r"^[a-z0-9_]{2,64}$", modid):
        return "ModId"
    if header != modid:
        return "X-UserFeedback-Mod must be the ModId"
    for field in ("ModVersion", "GameVersion"):
        if not isinstance(body.get(field), str) or not VERSION.match(body[field]):
            return field
    source, result, replaces = body.get("Source"), body.get("Result"), body.get("Replaces")
    if source not in ("auto", "manual") or result not in ("clean", "errors"):
        return "Source or Result"
    if source == "manual" and result != "clean":
        return "manual is always clean"
    if replaces is not None and (source != "auto" or replaces not in ("clean", "errors") or replaces == result):
        return "Replaces"
    if "OtherModsChanged" in body and replaces is None:
        return "OtherModsChanged only with Replaces"
    return None


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        url = urlsplit(self.path)
        mods = relay_mods()
        buckets = int(os.environ.get("RELAY_BUCKETS", "0"))
        if not mods or not url.path.startswith(PATH + "/registry"):
            return self.answer(404, {"message": "set RELAY_MODS to serve a registry"})

        rest = url.path[len(PATH + "/registry"):].strip("/")
        base = {"formatVersion": 2, "privacyUrl": None}
        if rest == "":
            body = dict(base, buckets={"prefixLength": buckets}) if buckets else compact(mods, dict(base))
            print(f"\n=== registry asked for ({'buckets of ' + str(buckets) if buckets else 'whole list'}), {url.query}")
        elif buckets and len(rest) == buckets:
            body = compact({m: v for m, v in mods.items() if prefix(m, buckets) == rest}, {"formatVersion": 2, "prefix": rest})
            print(f"\n=== registry bucket {rest} asked for: {body['mods']}")
        else:
            return self.answer(404, {"message": "no such bucket"})
        self.answer(200, body, cache=True)

    def do_POST(self):
        global count
        if self.path == PATH + "/works":
            return self.works()
        # The relay's upload address is <RelayUrl>/reports, a mod's own http destination is PATH itself
        if self.path not in (PATH, PATH + "/reports"):
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
                print(f"  {report.get('Kind', 'feedback')}, rating {report.get('Rating')}, mod {report.get('ModId')} {report.get('ModVersion')}: {report.get('Text', '')[:100]!r}")

        print(f"  saved in {folder}")
        self.answer(201, {"id": report_id, "message": "Received by the test server"})

    def works(self):
        length = int(self.headers.get("Content-Length", 0))
        if length > 2048:
            return self.answer(413, {"message": "A result is at most 2 KB."})
        try:
            body = json.loads(self.rfile.read(length))
        except ValueError:
            return self.answer(400, {"message": "The body isn't JSON."})

        print(f"\n=== works for me from {self.headers.get('User-Agent')}: {json.dumps(body)}")
        problem = works_problem(body, self.headers.get("X-UserFeedback-Mod"))
        if problem:
            print(f"  refused: {problem}")
            return self.answer(400, {"message": f"The relay couldn't read this result: {problem}."})
        if not relay_mods().get(body["ModId"], {}).get("works"):
            print("  refused: not in RELAY_MODS with :works")
            return self.answer(403, {"message": "This mod doesn't count \"works for me\" here."})

        forced = int(os.environ.get("WORKS_ANSWER", "204"))
        if forced != 204:
            print(f"  answered {forced} because of WORKS_ANSWER")
            if forced == 429:
                return self.answer(429, {"message": "Too many results from your address today."}, headers={"Retry-After": "600"})
            return self.answer(forced, {"message": f"Test server answered {forced} (WORKS_ANSWER)."})
        print("  counted")
        self.send_response(204)
        self.end_headers()

    def answer(self, status, body, cache=False, headers=None):
        data = json.dumps(body).encode()
        etag = '"' + hashlib.sha256(data).hexdigest()[:16] + '"'
        if cache and self.headers.get("If-None-Match") == etag:
            print("  unchanged, answered 304")
            self.send_response(304)
            self.send_header("ETag", etag)
            self.end_headers()
            return

        gzipped = cache and "gzip" in self.headers.get("Accept-Encoding", "")
        if gzipped:
            data = gzip.compress(data)
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        for name, value in (headers or {}).items():
            self.send_header(name, value)
        if cache:
            self.send_header("ETag", etag)
        if gzipped:
            self.send_header("Content-Encoding", "gzip")
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, format, *args):
        pass


print(f"Waiting for reports on http://localhost:{PORT}{PATH}, saving them in {os.path.abspath(FOLDER)}")
ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
