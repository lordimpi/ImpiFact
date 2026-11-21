"""Captura casos de referencia (XML + CUFE/CUDE/CUDS) desde una copia local de ApiDian, el sistema PHP original.

Un caso de referencia ("golden") es el XML y el código CUFE/CUDE/CUDS que ApiDian genera para
una entrada; ImpiFact debe reproducirlos byte a byte. El CUFE (facturas), el CUDE (notas
crédito y débito, contingencia y eventos) y el CUDS (documento soporte y su nota de ajuste) son
los códigos únicos que la DIAN exige en cada documento electrónico.

Para cada carpeta de caso en tests/golden/<type>/<case>/ que contenga un input.json,
este script envía ese JSON a la copia local de ApiDian, que construye el XML, lo firma y
calcula el código sin enviar nada a la DIAN. Hay dos modos (ver DOCUMENT_TYPES):

    query_uuid  solo InvoiceController (factura nacional y del sector salud): con
                "query_uuid": true, ApiDian responde justo después de firmar.
    envío       el resto de controladores (notas, exportación, contingencia, AIU, mandato,
                transporte, documento soporte) no lo admiten: ApiDian intenta enviar el
                documento a la URL del software, que apunta al nginx local en lugar de la
                DIAN; ese envío falla, pero el XML firmado ya quedó en disco.

Luego guarda:

    expected.xml   XML firmado sin el elemento <ds:Signature> (el resto, idéntico byte a byte)
    expected.cufe  CUFE, CUDE o CUDS generado por ApiDian
    response.json  respuesta JSON de ApiDian (solo el modo query_uuid)

y recalcula el código a partir del XML (ver check_code). Si el caso tiene un case.json (los
metadatos: variante, solicitud de Postman de la que sale y tarea de ImpiFact que lo usa), el
resultado queda en su campo code_check.

Dos tipos no pasan por la API y se generan con un script PHP que corre dentro del contenedor:

    attached-document  AttachedDocument (el contenedor electrónico que se entrega al comprador con
                       el documento firmado y el ApplicationResponse, la respuesta de validación de
                       la DIAN): ApiDian solo lo arma después de que la DIAN acepta el documento.
                       attached_document.php recibe el signed-document.xml y el
                       application-response.xml (inventado) del caso y se guarda
                       expected-attached-document.xml (sin <ds:Signature>). No se llama
                       expected.xml para que las pruebas de facturas y notas no lo tomen como uno
                       de sus casos.
    event              Evento RADIAN (ApplicationResponse, tipo 88): la API primero descarga de la
                       DIAN el XML de la factura referenciada (GetXmlByDocumentKey), así que no se
                       puede usar sin la DIAN. event.php repite el resto de
                       SendEventController::sendeventdata con los datos de esa factura tomados de
                       input.json y se guarda expected.xml y expected.cufe como en los demás tipos.

Uso (desde la carpeta backend/, con ApiDian levantado por start-reference.sh):

    python tools/golden-capture/capture.py              # configuración + captura de todos los casos
    python tools/golden-capture/capture.py --type invoice-export

Solo se requieren la biblioteca estándar de Python y la herramienta de línea de comandos
openssl (genera el certificado de prueba).
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
BACKEND_ROOT = HERE.parents[1]
REPO_ROOT = BACKEND_ROOT.parent

# Identidad de prueba fija. Todo aquí es ficticio excepto la resolución pública de pruebas de la
# DIAN (18760000001, prefijo SETP, rango 990000000-995000000) y su clave técnica. NIT es el número
# de identificación tributaria de la empresa y DV su dígito de verificación.
COMPANY_NIT = "900000001"
COMPANY_DV = "2"
SOFTWARE_ID = "3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47"
SOFTWARE_PIN = "12345"
# El documento soporte y su nota de ajuste se firman con un software propio (PUT
# config/softwaresupportdocument). Su PIN es distinto del de facturación para que un CUDS calculado
# con el PIN equivocado no pase inadvertido.
SD_SOFTWARE_ID = "7c1e5a90-2b4d-4f3a-8e6c-1d9b0a2f3c58"
SD_SOFTWARE_PIN = "54321"
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

SETP_RANGE = {
    "prefix": "SETP", "resolution": "18760000001", "resolution_date": "2019-01-19", "technical_key": TECHNICAL_KEY,
    "from": 990000000, "to": 995000000, "date_from": "2019-01-19", "date_to": "2030-01-19",
}

# Las facturas (1 nacional, 2 exportación, 3 contingencia tipo 03, 12 contingencia tipo 04) comparten el rango
# de pruebas SETP, como en la colección de Postman; type_document_id es el id de la tabla type_documents de
# ApiDian, no el código de la DIAN. El documento soporte (11) usa el rango de pruebas SEDS de Postman, sin
# clave técnica: si la tuviera, ApiDian calcularía un CUFE en lugar del CUDS (SignInvoice::setUUID).
RESOLUTIONS = [
    {"type_document_id": 1, **SETP_RANGE},
    {"type_document_id": 2, **SETP_RANGE},
    {"type_document_id": 3, **SETP_RANGE},
    {"type_document_id": 12, **SETP_RANGE},
    {"type_document_id": 4, "prefix": "NC", "from": 1, "to": 99999999},
    {"type_document_id": 5, "prefix": "ND", "from": 1, "to": 99999999},
    {
        "type_document_id": 11, "prefix": "SEDS", "resolution": "18760000001", "resolution_date": "2022-01-01",
        "from": 984000000, "to": 985000000, "date_from": "2022-01-01", "date_to": "2030-01-19",
    },
    {"type_document_id": 13, "prefix": "NDS", "from": 1, "to": 99999999},
]


@dataclass(frozen=True)
class DocumentType:
    """Cómo se captura un tipo de documento por la API de ApiDian."""

    route: str
    """Ruta de la API, después de /api/ubl2.1/."""

    file_prefix: str
    """Prefijo del nombre de archivo con que ApiDian guarda el XML firmado: <prefijo>-<resolución><número>.xml."""

    query_uuid: bool
    """True si el controlador admite "query_uuid" (solo InvoiceController); si no, se usa el modo de envío fallido."""

    stores_document: bool = False
    """True si ApiDian guarda el documento como ya enviado antes de llamar a la DIAN y rechaza el mismo número la
    vez siguiente (contingencia tipo 04, que queda pendiente de transmitir): la captura borra ese registro antes."""


# Carpeta del tipo de documento en tests/golden -> cómo se captura. Las variantes de factura tienen su propia
# carpeta, aunque compartan ruta (invoice-health usa la de invoice), para que las pruebas de factura nacional no
# las tomen como sus casos.
DOCUMENT_TYPES = {
    "invoice": DocumentType("invoice", "FES", query_uuid=True),
    "invoice-health": DocumentType("invoice", "FES", query_uuid=True),
    "invoice-export": DocumentType("invoice-export", "FES", query_uuid=False),
    "invoice-contingency": DocumentType("invoice-contingency", "FES", query_uuid=False),
    "invoice-contingency-4": DocumentType("invoice-contingency-4", "FES", query_uuid=False, stores_document=True),
    "invoice-aiu": DocumentType("invoice-aiu", "FES", query_uuid=False),
    "invoice-mandate": DocumentType("invoice-mandate", "FES", query_uuid=False),
    "invoice-transport": DocumentType("invoice-transport", "FES", query_uuid=False),
    "credit-note": DocumentType("credit-note", "NCS", query_uuid=False),
    "debit-note": DocumentType("debit-note", "NDS", query_uuid=False),
    "support-document": DocumentType("support-document", "DSS", query_uuid=False),
    "sd-credit-note": DocumentType("sd-credit-note", "NDSNS", query_uuid=False),
}

# Carpetas de los casos que se capturan sin la API, con un script PHP dentro del contenedor.
ATTACHED_TYPE = "attached-document"
EVENT_TYPE = "event"
SCRIPT_TYPES = (ATTACHED_TYPE, EVENT_TYPE)

UBL_NS = {
    "cbc": "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2",
    "cac": "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",
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
    """Crea un certificado PKCS#12 autofirmado desechable y lo devuelve en base64."""
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
    """Crea o actualiza la empresa de prueba en ApiDian y devuelve su token de API."""
    company = call(base_url, "POST", f"config/{COMPANY_NIT}/{COMPANY_DV}", COMPANY)
    token = company.get("token")
    if not token:
        raise ApiError(f"company setup returned no token: {company}")
    # Las tres URL (facturación, documento soporte y eventos) apuntan al nginx local. ConfigurationController::
    # storeSoftware atiende las dos rutas y, al actualizar, vuelve a poner la URL de habilitación de la DIAN en
    # "url" si no llega: por eso cada llamada repite todas las URL.
    local_urls = {"url": software_url, "urlsupportdocument": software_url, "urlevent": software_url}
    call(base_url, "PUT", "config/software", {"id": SOFTWARE_ID, "pin": SOFTWARE_PIN, **local_urls}, token)
    call(base_url, "PUT", "config/softwaresupportdocument",
         {"idsupportdocument": SD_SOFTWARE_ID, "pinsupportdocument": SD_SOFTWARE_PIN, **local_urls}, token)
    with tempfile.TemporaryDirectory() as workdir:
        certificate = make_test_certificate(Path(workdir))
    call(base_url, "PUT", "config/certificate",
         {"certificate": certificate, "password": CERT_PASSWORD}, token)
    for resolution in RESOLUTIONS:
        call(base_url, "PUT", "config/resolution", resolution, token)
    return token


def capture_case(base_url: str, token: str, apidian_dir: Path, doc_type: str, case_dir: Path) -> str:
    kind = DOCUMENT_TYPES[doc_type]
    request = json.loads((case_dir / "input.json").read_text(encoding="utf-8"))
    signed_path = apidian_dir / "storage" / "app" / "public" / COMPANY_NIT / f"{kind.file_prefix}-{request['prefix']}{request['number']}.xml"
    signed_path.unlink(missing_ok=True)
    if kind.stores_document:
        forget_document(request["prefix"], request["number"])

    response = None
    if kind.query_uuid:
        # query_uuid hace que ApiDian responda justo después de firmar, antes de llamar al servicio web
        # de la DIAN (SOAP).
        request["query_uuid"] = True
        response = call(base_url, "POST", kind.route, request, token)
        if not response.get("success") or not response.get("uuid"):
            raise ApiError(f"{case_dir.name}: ApiDian did not return a CUFE: {json.dumps(response)[:800]}")
    else:
        # Sin query_uuid, ApiDian firma, escribe el XML y luego envía la solicitud al servicio web (SOAP)
        # en la URL del software. Esa URL apunta al nginx local, que responde en lugar de la DIAN, así que
        # se espera que la llamada falle; para entonces el XML firmado ya está en disco. Si la respuesta
        # sí es JSON, se conserva para el mensaje de error en caso de que no haya XML.
        try:
            failure = json.dumps(call(base_url, "POST", kind.route, request, token), ensure_ascii=False)[:800]
        except ApiError as error:
            failure = str(error)
        if not signed_path.exists():
            raise ApiError(f"{case_dir.name}: signed XML not found at {signed_path}; ApiDian answered: {failure}")

    if not signed_path.exists():
        raise ApiError(f"{case_dir.name}: signed XML not found at {signed_path}")
    signed = signed_path.read_bytes().decode("utf-8")
    uuid = save_signed(case_dir, signed)
    if response is not None and response["uuid"] != uuid:
        raise ApiError(f"{case_dir.name}: response CUFE {response['uuid']} differs from XML UUID {uuid}")
    if response is not None:
        (case_dir / "response.json").write_text(
            json.dumps(response, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="")
    return uuid


def forget_document(prefix: str, number: int) -> None:
    """Borra de la base de datos de ApiDian el registro de un documento de la empresa de prueba, para poder
    capturarlo de nuevo con el mismo número."""
    if not re.fullmatch(r"[A-Z0-9]{1,10}", prefix) or not isinstance(number, int):
        raise ApiError(f"unexpected prefix or number: {prefix!r} {number!r}")
    sql = (f"DELETE FROM documents WHERE identification_number = '{COMPANY_NIT}' "
           f"AND prefix = '{prefix}' AND number = {number}")
    result = subprocess.run(
        ["docker", "compose", "-f", str(HERE / "docker-compose.yml"), "exec", "-T", "db",
         "mariadb", "-uuser_api", "-ppassword_api", "db_api", "-e", sql], capture_output=True)
    if result.returncode != 0:
        raise ApiError(f"could not delete {prefix}{number}: {result.stderr.decode('utf-8', 'replace')[-400:]}")


def save_signed(case_dir: Path, signed: str) -> str:
    """Guarda expected.xml (sin <ds:Signature>) y expected.cufe y devuelve el código del cbc:UUID."""
    match = UUID_RE.search(signed)
    if not match:
        raise ApiError(f"{case_dir.name}: no cbc:UUID in the signed XML")
    uuid = match.group(1)
    unsigned, removed = SIGNATURE_RE.subn("", signed)
    if removed != 1:
        raise ApiError(f"{case_dir.name}: expected exactly one ds:Signature, found {removed}")

    # newline="" conserva los bytes exactamente como los produjo ApiDian en cualquier sistema operativo.
    (case_dir / "expected.xml").write_text(unsigned, encoding="utf-8", newline="")
    (case_dir / "expected.cufe").write_text(uuid + "\n", encoding="utf-8", newline="")
    return uuid


def run_php_script(apidian_dir: Path, script: str, case_dir: Path, request: dict, files: tuple[str, ...]) -> str:
    """Ejecuta un script de esta carpeta dentro del contenedor php_api y devuelve el output.xml que escribe.

    El script lee input.json (request) y los archivos indicados del caso desde
    storage/app/public/golden-script/, la carpeta que el contenedor ve en /var/www/html.
    """
    staging = apidian_dir / "storage" / "app" / "public" / "golden-script"
    shutil.rmtree(staging, ignore_errors=True)
    staging.mkdir(parents=True)
    try:
        (staging / "input.json").write_text(json.dumps(request, ensure_ascii=False), encoding="utf-8")
        for name in files:
            (staging / name).write_bytes((case_dir / name).read_bytes())

        # El script se pasa por la entrada estándar.
        result = subprocess.run(
            ["docker", "compose", "-f", str(HERE / "docker-compose.yml"), "exec", "-T", "php_api", "php"],
            input=(HERE / script).read_bytes(), capture_output=True)
        if result.returncode != 0 or not (staging / "output.xml").exists():
            raise ApiError(f"{case_dir.name}: {script} failed: "
                           f"{(result.stdout + result.stderr).decode('utf-8', 'replace')[-800:]}")
        return (staging / "output.xml").read_bytes().decode("utf-8")
    finally:
        shutil.rmtree(staging, ignore_errors=True)


def capture_attached_case(apidian_dir: Path, case_dir: Path) -> str:
    """Genera con ApiDian el AttachedDocument de un caso, sin llamar a la DIAN (ver attached_document.php)."""
    request = json.loads((case_dir / "input.json").read_text(encoding="utf-8"))
    request["company_nit"] = COMPANY_NIT
    request["document_number"] = f"{request['prefix']}{request['number']}"
    signed = run_php_script(apidian_dir, "attached_document.php", case_dir, request,
                            ("signed-document.xml", "application-response.xml"))

    unsigned, removed = SIGNATURE_RE.subn("", signed)
    if removed != 1:
        raise ApiError(f"{case_dir.name}: expected exactly one ds:Signature, found {removed}")
    (case_dir / "expected-attached-document.xml").write_text(unsigned, encoding="utf-8", newline="")
    return request["cufe"]


def capture_event_case(apidian_dir: Path, case_dir: Path) -> str:
    """Genera con ApiDian el ApplicationResponse de un evento RADIAN, sin llamar a la DIAN (ver event.php)."""
    request = json.loads((case_dir / "input.json").read_text(encoding="utf-8"))
    request["company_nit"] = COMPANY_NIT
    return save_signed(case_dir, run_php_script(apidian_dir, "event.php", case_dir, request, ()))


# Fórmulas del código único, como las aplica la DIAN y como las escribe SignInvoice de ApiDian (cufe, cude, cuds y
# cudeevent): el SHA-384 de la concatenación de valores del XML. Cada impuesto va como código + valor (01 IVA, 04
# INC, 03 ICA), con 0.00 si el documento no lo tiene; el CUDS solo lleva el IVA.
SECRETS = {
    "technical key": TECHNICAL_KEY,
    "software PIN": SOFTWARE_PIN,
    "support-document software PIN": SD_SOFTWARE_PIN,
}

# schemeName del cbc:UUID -> (fórmula, secreto) que exige la DIAN. El evento (ApplicationResponse) se reconoce por
# su elemento raíz.
EXPECTED_FORMULA = {
    "CUFE-SHA384": ("CUFE", "technical key"),
    "CUDE-SHA384": ("CUDE", "software PIN"),
    "CUDS-SHA384": ("CUDS", "support-document software PIN"),
}
EVENT_FORMULA = ("EVENT", "software PIN")

# Combinaciones que se prueban para averiguar cuál usó el PHP cuando la esperada no coincide. El CUFE y el CUDE
# concatenan los mismos campos y solo difieren en el secreto, así que se nombran por él.
ALTERNATIVE_FORMULAS = (
    ("CUFE", "technical key"),
    ("CUDE", "software PIN"),
    ("CUDE", "support-document software PIN"),
    ("CUDS", "support-document software PIN"),
    ("CUDS", "software PIN"),
    ("EVENT", "software PIN"),
)


def _text(root: ET.Element, path: str, default: str | None = None) -> str:
    element = root.find(path, UBL_NS)
    if element is None or element.text is None:
        if default is None:
            raise ApiError(f"{path} not found in the XML")
        return default
    return element.text


def recompute_code(root: ET.Element, formula: str, secret: str) -> str:
    head = [_text(root, "cbc:ID"), _text(root, "cbc:IssueDate"), _text(root, "cbc:IssueTime")]
    if formula == "EVENT":
        parts = head + [
            _text(root, "cac:SenderParty/cac:PartyTaxScheme/cbc:CompanyID"),
            _text(root, "cac:ReceiverParty/cac:PartyTaxScheme/cbc:CompanyID"),
            _text(root, "cac:DocumentResponse/cac:Response/cbc:ResponseCode"),
            _text(root, "cac:DocumentResponse/cac:DocumentReference/cbc:ID"),
            _text(root, "cac:DocumentResponse/cac:DocumentReference/cbc:DocumentTypeCode"),
            secret,
        ]
        return hashlib.sha384("".join(parts).encode("utf-8")).hexdigest()

    totals = root.find("cac:LegalMonetaryTotal", UBL_NS)
    if totals is None:
        totals = root.find("cac:RequestedMonetaryTotal", UBL_NS)
    if totals is None:
        raise ApiError("no cac:LegalMonetaryTotal or cac:RequestedMonetaryTotal in the XML")
    taxes: dict[str, str] = {}
    for tax_total in root.findall("cac:TaxTotal", UBL_NS):
        code = _text(tax_total, "cac:TaxSubtotal/cac:TaxCategory/cac:TaxScheme/cbc:ID", "")
        taxes.setdefault(code, _text(tax_total, "cbc:TaxAmount"))
    tax_parts = ["01", taxes.get("01", "0.00")]
    if formula != "CUDS":
        tax_parts += ["04", taxes.get("04", "0.00"), "03", taxes.get("03", "0.00")]
    parts = head + [_text(totals, "cbc:LineExtensionAmount")] + tax_parts + [
        _text(totals, "cbc:PayableAmount"),
        _text(root, "cac:AccountingSupplierParty/cac:Party/cac:PartyTaxScheme/cbc:CompanyID"),
        _text(root, "cac:AccountingCustomerParty/cac:Party/cac:PartyTaxScheme/cbc:CompanyID"),
        secret,
        _text(root, "cbc:ProfileExecutionID"),
    ]
    return hashlib.sha384("".join(parts).encode("utf-8")).hexdigest()


def check_code(case_dir: Path) -> dict:
    """Recalcula el código de expected.xml con la fórmula que exige la DIAN para su schemeName.

    Si no coincide con el cbc:UUID que escribió ApiDian, prueba las demás fórmulas y secretos para decir cuál usó
    el PHP (un error del PHP que el golden conserva tal cual: ImpiFact debe decidir si lo reproduce).
    """
    xml = (case_dir / "expected.xml").read_bytes()
    root = ET.fromstring(xml)
    uuid_element = root.find("cbc:UUID", UBL_NS)
    uuid, scheme = uuid_element.text, uuid_element.get("schemeName")
    if root.tag.endswith("}ApplicationResponse"):
        expected = EVENT_FORMULA
    elif scheme in EXPECTED_FORMULA:
        expected = EXPECTED_FORMULA[scheme]
    else:
        raise ApiError(f"{case_dir.name}: unknown UUID schemeName {scheme}")

    result = {"scheme_name": scheme, "formula": expected[0], "secret": expected[1]}
    if recompute_code(root, expected[0], SECRETS[expected[1]]) == uuid:
        return {**result, "matches": True}
    for formula, secret_name in ALTERNATIVE_FORMULAS:
        try:
            if recompute_code(root, formula, SECRETS[secret_name]) == uuid:
                return {**result, "matches": False, "php_formula": formula, "php_secret": secret_name}
        except ApiError:
            continue
    return {**result, "matches": False, "php_formula": None, "php_secret": None}


def record_code_check(case_dir: Path) -> dict:
    """Recalcula el código y, si el caso tiene case.json, guarda el resultado en su campo code_check."""
    check = check_code(case_dir)
    metadata_path = case_dir / "case.json"
    if metadata_path.exists():
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        metadata["code_check"] = check
        metadata_path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return check


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--base-url", default="http://127.0.0.1:8081", help="ApiDian reference URL")
    parser.add_argument("--software-url", default="http://nginx/robots.txt",
                        help="URL ApiDian pings before signing; points to the local nginx so no DIAN traffic is sent")
    parser.add_argument("--apidian-dir", default=str(REPO_ROOT.parent / "apidian"),
                        help="ApiDian checkout mounted in the reference stack")
    parser.add_argument("--golden-dir", default=str(BACKEND_ROOT / "tests" / "golden"))
    parser.add_argument("--type", choices=sorted([*DOCUMENT_TYPES, *SCRIPT_TYPES]), help="capture only this document type")
    args = parser.parse_args()

    golden_dir, apidian_dir = Path(args.golden_dir), Path(args.apidian_dir)
    types = [args.type] if args.type else sorted([*DOCUMENT_TYPES, *SCRIPT_TYPES])
    cases = [(t, d) for t in types for d in sorted((golden_dir / t).glob("*/")) if (d / "input.json").exists()]
    if not cases:
        print(f"No cases found under {golden_dir}", file=sys.stderr)
        return 1

    try:
        token = setup(args.base_url, args.software_url)
        failures = 0
        for doc_type, case_dir in cases:
            try:
                if doc_type == ATTACHED_TYPE:
                    code = capture_attached_case(apidian_dir, case_dir)
                    print(f"OK    {doc_type}/{case_dir.name}  {code[:16]}...")
                    continue
                if doc_type == EVENT_TYPE:
                    code = capture_event_case(apidian_dir, case_dir)
                else:
                    code = capture_case(args.base_url, token, apidian_dir, doc_type, case_dir)
                check = record_code_check(case_dir)
                if check["matches"]:
                    verdict = f"{check['formula']} recomputed: match"
                else:
                    verdict = (f"{check['formula']} recomputed: MISMATCH (PHP used {check['php_formula']} "
                               f"with the {check['php_secret']})")
                print(f"OK    {doc_type}/{case_dir.name}  {code[:16]}...  {verdict}")
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
