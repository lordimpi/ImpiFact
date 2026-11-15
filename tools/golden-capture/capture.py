"""Capture golden fixtures (XML + CUFE/CUDE) from a local ApiDian reference instance.

For every case folder under tests/golden/<type>/<case>/ that contains an input.json,
this script sends the JSON to the PHP reference with "query_uuid": true. In that mode
ApiDian builds, signs and computes the CUFE/CUDE but returns before calling the DIAN.
It then stores:

    expected.xml   signed XML with the <ds:Signature> element removed (byte-exact otherwise)
    expected.cufe  CUFE or CUDE returned by ApiDian
    response.json  ApiDian JSON response

Usage (from the repository root, with the reference stack running):

    python tools/golden-capture/capture.py              # setup + capture every case
    python tools/golden-capture/capture.py --type invoice

Only Python's standard library and the openssl CLI are required.
"""

from __future__ import annotations

import argparse
import base64
import json
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# Fixed test identity. Everything here is fictitious except the DIAN public test
# resolution (18760000001, prefix SETP, range 990000000-995000000) and its technical key.
COMPANY_NIT = "900000001"
COMPANY_DV = "2"
SOFTWARE_ID = "3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47"
SOFTWARE_PIN = "12345"
CERT_PASSWORD = "golden-test"
TECHNICAL_KEY = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c"

COMPANY = {
    "type_document_identification_id": 6,
    "type_organization_id": 1,
    "type_regime_id": 1,
    "type_liability_id": 14,
    "business_name": "EMPRESA DE PRUEBA GOLDEN SAS",
    "merchant_registration": "0000000-00",
    "municipality_id": 149,
    "address": "CALLE 1 # 1-01",
    "phone": "3000000000",
    "email": "golden@example.com",
}

RESOLUTIONS = [
    {
        "type_document_id": 1, "prefix": "SETP", "resolution": "18760000001",
        "resolution_date": "2019-01-19", "technical_key": TECHNICAL_KEY,
        "from": 990000000, "to": 995000000, "date_from": "2019-01-19", "date_to": "2030-01-19",
    },
    {"type_document_id": 4, "prefix": "NC", "from": 1, "to": 99999999},
    {"type_document_id": 5, "prefix": "ND", "from": 1, "to": 99999999},
]

# Document type folder -> (API route, file prefix used by ApiDian for the signed XML).
DOCUMENT_TYPES = {
    "invoice": ("invoice", "FES"),
    "credit-note": ("credit-note", "NCS"),
    "debit-note": ("debit-note", "NDS"),
}

SIGNATURE_RE = re.compile(r"<ds:Signature\b.*?</ds:Signature>", re.DOTALL)
UUID_RE = re.compile(r"<cbc:UUID\b[^>]*>([0-9a-f]{96})</cbc:UUID>")


class ApiError(RuntimeError):
    pass


def call(base_url: str, method: str, path: str, body: dict, token: str | None = None) -> dict:
    data = json.dumps(body).encode("utf-8")
    request = urllib.request.Request(f"{base_url}/api/ubl2.1/{path}", data=data, method=method)
    request.add_header("Content-Type", "application/json")
    request.add_header("Accept", "application/json")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            payload = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        raise ApiError(f"{method} {path} -> HTTP {error.code}: {error.read().decode('utf-8', 'replace')[:800]}") from error
    try:
        return json.loads(payload)
    except json.JSONDecodeError as error:
        raise ApiError(f"{method} {path} returned non-JSON: {payload[:800]}") from error


def make_test_certificate(workdir: Path) -> str:
    """Create a throwaway self-signed PKCS#12 certificate and return it as base64."""
    key, cert, p12 = workdir / "key.pem", workdir / "cert.pem", workdir / "cert.p12"
    subject = f"/C=CO/O=EMPRESA DE PRUEBA GOLDEN SAS/CN={COMPANY_NIT}"
    subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "3650",
                    "-subj", subject, "-keyout", str(key), "-out", str(cert)],
                   check=True, capture_output=True)
    subprocess.run(["openssl", "pkcs12", "-export", "-inkey", str(key), "-in", str(cert),
                    "-out", str(p12), "-passout", f"pass:{CERT_PASSWORD}"],
                   check=True, capture_output=True)
    return base64.b64encode(p12.read_bytes()).decode("ascii")


def setup(base_url: str, software_url: str) -> str:
    """Create or update the test company in ApiDian and return its API token."""
    company = call(base_url, "POST", f"config/{COMPANY_NIT}/{COMPANY_DV}", COMPANY)
    token = company.get("token")
    if not token:
        raise ApiError(f"company setup returned no token: {company}")
    call(base_url, "PUT", "config/software",
         {"id": SOFTWARE_ID, "pin": SOFTWARE_PIN, "url": software_url}, token)
    with tempfile.TemporaryDirectory() as workdir:
        certificate = make_test_certificate(Path(workdir))
    call(base_url, "PUT", "config/certificate",
         {"certificate": certificate, "password": CERT_PASSWORD}, token)
    for resolution in RESOLUTIONS:
        call(base_url, "PUT", "config/resolution", resolution, token)
    return token


def capture_case(base_url: str, token: str, apidian_dir: Path, doc_type: str, case_dir: Path) -> str:
    route, file_prefix = DOCUMENT_TYPES[doc_type]
    request = json.loads((case_dir / "input.json").read_text(encoding="utf-8"))
    signed_path = apidian_dir / "storage" / "app" / "public" / COMPANY_NIT / f"{file_prefix}-{request['prefix']}{request['number']}.xml"
    signed_path.unlink(missing_ok=True)

    response = None
    if doc_type == "invoice":
        # query_uuid makes ApiDian return right after signing, before any SOAP call.
        request["query_uuid"] = True
        response = call(base_url, "POST", route, request, token)
        if not response.get("success") or not response.get("uuid"):
            raise ApiError(f"{case_dir.name}: ApiDian did not return a CUFE: {json.dumps(response)[:800]}")
    else:
        # Notes have no query_uuid: ApiDian signs, writes the XML and then posts the SOAP
        # request to the software URL, which points to the local nginx stub. That call is
        # expected to fail; the signed XML is already on disk by then.
        try:
            call(base_url, "POST", route, request, token)
        except ApiError:
            pass

    if not signed_path.exists():
        raise ApiError(f"{case_dir.name}: signed XML not found at {signed_path}")
    signed = signed_path.read_bytes().decode("utf-8")
    match = UUID_RE.search(signed)
    if not match:
        raise ApiError(f"{case_dir.name}: no cbc:UUID in the signed XML")
    uuid = match.group(1)
    if response is not None and response["uuid"] != uuid:
        raise ApiError(f"{case_dir.name}: response CUFE {response['uuid']} differs from XML UUID {uuid}")
    unsigned, removed = SIGNATURE_RE.subn("", signed)
    if removed != 1:
        raise ApiError(f"{case_dir.name}: expected exactly one ds:Signature, found {removed}")

    # newline="" keeps the bytes exactly as ApiDian produced them on every OS.
    (case_dir / "expected.xml").write_text(unsigned, encoding="utf-8", newline="")
    (case_dir / "expected.cufe").write_text(uuid + "\n", encoding="utf-8", newline="")
    if response is not None:
        (case_dir / "response.json").write_text(
            json.dumps(response, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="")
    return uuid


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--base-url", default="http://127.0.0.1:8081", help="ApiDian reference URL")
    parser.add_argument("--software-url", default="http://nginx/robots.txt",
                        help="URL ApiDian pings before signing; points to the local nginx so no DIAN traffic is sent")
    parser.add_argument("--apidian-dir", default=str(REPO_ROOT.parent / "apidian"),
                        help="ApiDian checkout mounted in the reference stack")
    parser.add_argument("--golden-dir", default=str(REPO_ROOT / "tests" / "golden"))
    parser.add_argument("--type", choices=sorted(DOCUMENT_TYPES), help="capture only this document type")
    args = parser.parse_args()

    golden_dir, apidian_dir = Path(args.golden_dir), Path(args.apidian_dir)
    types = [args.type] if args.type else sorted(DOCUMENT_TYPES)
    cases = [(t, d) for t in types for d in sorted((golden_dir / t).glob("*/")) if (d / "input.json").exists()]
    if not cases:
        print(f"No cases found under {golden_dir}", file=sys.stderr)
        return 1

    try:
        token = setup(args.base_url, args.software_url)
        failures = 0
        for doc_type, case_dir in cases:
            try:
                code = capture_case(args.base_url, token, apidian_dir, doc_type, case_dir)
                print(f"OK    {doc_type}/{case_dir.name}  {code[:16]}...")
            except ApiError as error:
                failures += 1
                print(f"FAIL  {doc_type}/{case_dir.name}  {error}", file=sys.stderr)
    except (ApiError, urllib.error.URLError, subprocess.CalledProcessError) as error:
        print(f"Setup failed: {error}", file=sys.stderr)
        return 1
    print(f"{len(cases) - failures}/{len(cases)} cases captured")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
