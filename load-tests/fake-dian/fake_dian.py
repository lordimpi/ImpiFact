"""Local fake of the DIAN SOAP web service for load tests.

The Worker sends every document here instead of the real DIAN (see load-tests/compose.load.yml). The fake answers
each operation with the canned WCF reply shape the integration tests use (DianReplies.cs): SendBillSync and GetStatus
accept the document with status 00, GetStatusZip accepts it, SendTestSetAsync returns a ZipKey. It never forwards
anything anywhere.

GET /stats returns the request count per operation and per wsa:To host, so a run can prove that every call the Worker
made was addressed to this fake.

Standard library only; it runs on the official python image with no packages installed.
"""

import base64
import json
import os
import re
import sys
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

APPLICATION_RESPONSE_XML = (
    '<ApplicationResponse xmlns="urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2" '
    'xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">'
    "<cbc:IssueDate>{date}</cbc:IssueDate><cbc:IssueTime>{time}-05:00</cbc:IssueTime></ApplicationResponse>"
)

ACTION_PATTERN = re.compile(r"<(?:\w+:)?Action[^>]*>[^<]*/(\w+)</(?:\w+:)?Action>")
TO_PATTERN = re.compile(r"<(?:\w+:)?To[^>]*>([^<]*)</(?:\w+:)?To>")

# Optional artificial latency per call, in milliseconds, to mimic the real service.
LATENCY_SECONDS = int(os.environ.get("FAKE_DIAN_LATENCY_MS", "0")) / 1000.0

stats_lock = threading.Lock()
stats = {"operations": {}, "to_hosts": {}, "total": 0}


def fields(is_valid, status_code):
    now = time.gmtime(time.time() - 5 * 3600)
    application_response = APPLICATION_RESPONSE_XML.format(
        date=time.strftime("%Y-%m-%d", now), time=time.strftime("%H:%M:%S", now)
    )
    encoded = base64.b64encode(application_response.encode("utf-8")).decode("ascii")
    return (
        '<b:ErrorMessage xmlns:c="http://schemas.microsoft.com/2003/10/Serialization/Arrays"/>'
        f"<b:IsValid>{'true' if is_valid else 'false'}</b:IsValid>"
        f"<b:StatusCode>{status_code}</b:StatusCode>"
        "<b:StatusDescription>Procesado Correctamente.</b:StatusDescription>"
        '<b:StatusMessage i:nil="true"/>'
        f"<b:XmlBase64Bytes>{encoded}</b:XmlBase64Bytes>"
        '<b:XmlBytes i:nil="true"/><b:XmlDocumentKey i:nil="true"/><b:XmlFileName i:nil="true"/>'
    )


def result(operation):
    return (
        f'<{operation}Response xmlns="http://wcf.dian.colombia">'
        f'<{operation}Result xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" '
        'xmlns:i="http://www.w3.org/2001/XMLSchema-instance">'
        f"{fields(True, '00')}</{operation}Result></{operation}Response>"
    )


def body_for(operation):
    if operation in ("SendBillSync", "GetStatus"):
        return result(operation)
    if operation == "GetStatusZip":
        return (
            '<GetStatusZipResponse xmlns="http://wcf.dian.colombia">'
            '<GetStatusZipResult xmlns:b="http://schemas.datacontract.org/2004/07/DianResponse" '
            'xmlns:i="http://www.w3.org/2001/XMLSchema-instance">'
            f"<b:DianResponse>{fields(True, '00')}</b:DianResponse></GetStatusZipResult></GetStatusZipResponse>"
        )
    if operation == "SendTestSetAsync":
        return (
            '<SendTestSetAsyncResponse xmlns="http://wcf.dian.colombia">'
            '<SendTestSetAsyncResult xmlns:b="http://schemas.datacontract.org/2004/07/UploadDocumentResponse" '
            'xmlns:i="http://www.w3.org/2001/XMLSchema-instance">'
            '<b:ErrorMessageList xmlns:c="http://schemas.datacontract.org/2004/07/XmlParamsResponseTrackId"/>'
            f"<b:ZipKey>{uuid.uuid4()}</b:ZipKey></SendTestSetAsyncResult></SendTestSetAsyncResponse>"
        )
    return None


def envelope(operation, body):
    return (
        '<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://www.w3.org/2005/08/addressing">'
        '<s:Header><a:Action s:mustUnderstand="1">'
        f"http://wcf.dian.colombia/IWcfDianCustomerServices/{operation}Response</a:Action></s:Header>"
        f"<s:Body>{body}</s:Body></s:Envelope>"
    )


def count(operation, to_host):
    with stats_lock:
        stats["total"] += 1
        stats["operations"][operation] = stats["operations"].get(operation, 0) + 1
        stats["to_hosts"][to_host] = stats["to_hosts"].get(to_host, 0) + 1


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):  # noqa: A002 - signature from BaseHTTPRequestHandler
        sys.stdout.write("%s %s\n" % (self.log_date_time_string(), format % args))
        sys.stdout.flush()

    def send(self, status, content_type, payload):
        data = payload.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == "/stats":
            with stats_lock:
                self.send(200, "application/json", json.dumps(stats, sort_keys=True))
            return
        if self.path == "/health":
            self.send(200, "text/plain", "ok")
            return
        self.send(404, "text/plain", "not found")

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        request = self.rfile.read(length).decode("utf-8", errors="replace")
        action = ACTION_PATTERN.search(request)
        operation = action.group(1) if action else "unknown"
        to = TO_PATTERN.search(request)
        to_host = re.sub(r"^\w+://([^/:]+).*$", r"\1", to.group(1)) if to else "missing"
        count(operation, to_host)

        body = body_for(operation)
        if body is None:
            self.send(500, "text/plain", f"fake DIAN does not implement {operation}")
            return
        if LATENCY_SECONDS > 0:
            time.sleep(LATENCY_SECONDS)
        self.send(200, "application/soap+xml; charset=utf-8", envelope(operation, body))


if __name__ == "__main__":
    port = int(os.environ.get("FAKE_DIAN_PORT", "8080"))
    server = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    sys.stdout.write(f"fake DIAN listening on {port}\n")
    sys.stdout.flush()
    server.serve_forever()
