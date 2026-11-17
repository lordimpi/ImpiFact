"""Limpia la colección Postman pública de ApiDian antes de guardarla en el repositorio.

La colección publicada trae valores con aspecto de credenciales reales (tokens, un certificado .p12 con su
clave, contraseñas de correo) y datos de contacto. Este script deja la estructura y los cuerpos que usan los
contract tests y quita lo sensible. Uso (Python 3, sin dependencias):

    python -I sanitize.py <colección descargada.json> <salida.json>
"""

import json
import re
import sys

# Carpetas Enterprise (app, RIPS, WhatsApp, extractor): nunca se ejecutan, se guardan sin cuerpos ni respuestas.
ENTERPRISE_FOLDERS = ("11 ", "12 ", "13 ", "14 ")
SECRET_KEYS = {
    "api_token", "certificate", "mail_password", "mail_username", "password", "newpassword", "pin", "token",
    "token_apirips", "auth_secret_0dmt", "admin_token",
}
AUTH_HEADERS = {"authorization", "x-admin-token"}
DROPPED_HEADERS = {"cookie"}
MAX_STRING = 200

EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")
SECRET_PATTERN = r"[a-z_]*(?:token|password|secret)|%s" % "|".join(sorted(SECRET_KEYS))
SECRET_KEY = re.compile(r"^(?:%s)$" % SECRET_PATTERN, re.IGNORECASE)
SECRET_IN_RAW = re.compile(r'"(%s)"(\s*:\s*)"[^"]*"' % SECRET_PATTERN, re.IGNORECASE)
PHONE_IN_RAW = re.compile(r'"(phone|telephone)"(\s*:\s*)"?[0-9 +-]{5,}"?', re.IGNORECASE)
BEARER = re.compile(r"(Bearer\s+)[A-Za-z0-9._-]{16,}")
LONG_BASE64 = re.compile(r"[A-Za-z0-9+/]{%d,}={0,2}" % MAX_STRING)


def clean_text(text):
    text = EMAIL.sub("contacto@example.com", text)
    text = BEARER.sub(lambda m: m.group(1) + "{{token}}", text)
    text = SECRET_IN_RAW.sub(lambda m: '"%s"%s"REDACTED"' % (m.group(1), m.group(2)), text)
    text = PHONE_IN_RAW.sub(lambda m: '"%s"%s"3000000000"' % (m.group(1), m.group(2)), text)
    return LONG_BASE64.sub("REDACTED_BASE64", text)


def clean_value(key, value):
    if isinstance(value, dict):
        return {k: clean_value(k, v) for k, v in value.items()}
    if isinstance(value, list):
        return [clean_value(key, v) for v in value]
    if isinstance(value, str):
        if key and SECRET_KEY.match(key):
            return "REDACTED"
        if len(value) > MAX_STRING:
            return "REDACTED (%d chars)" % len(value)
        return EMAIL.sub("contacto@example.com", value)
    if key and key.lower() in ("phone", "telephone") and value is not None:
        return "3000000000"
    return value


def clean_response(response):
    body = response.get("body") or ""
    try:
        cleaned = json.dumps(clean_value(None, json.loads(body)), ensure_ascii=False, indent=4)
    except ValueError:
        cleaned = clean_text(body[:500])
    return {k: v for k, v in response.items() if k not in ("originalRequest", "header", "cookie")} | {"body": cleaned}


def clean_auth(node):
    bearer = (node.get("auth") or {}).get("bearer")
    entries = bearer.get("basicConfig", []) if isinstance(bearer, dict) else bearer or []
    for entry in entries:
        if entry.get("key") == "token":
            entry["value"] = "{{token}}"


def clean_request(request):
    request["header"] = [h for h in request.get("header") or [] if h.get("key", "").lower() not in DROPPED_HEADERS]
    for header in request["header"]:
        if header.get("key", "").lower() in AUTH_HEADERS:
            header["value"] = ""
    clean_auth(request)
    if isinstance(request.get("description"), str):
        request["description"] = BEARER.sub(lambda m: m.group(1) + "{{token}}", EMAIL.sub("contacto@example.com", request["description"]))
    body = request.get("body") or {}
    if isinstance(body.get("raw"), str):
        body["raw"] = clean_text(body["raw"])


def walk(items, enterprise):
    for item in items:
        if "item" in item:
            clean_auth(item)
            walk(item["item"], enterprise or item["name"].startswith(ENTERPRISE_FOLDERS))
            continue
        request = item.get("request") or {}
        clean_request(request)
        if enterprise:
            request.pop("body", None)
            item["response"] = []
        else:
            item["response"] = [clean_response(r) for r in item.get("response") or []]


def main(source, target):
    with open(source, encoding="utf-8") as handle:
        collection = json.load(handle)
    for variable in collection.get("variable") or []:
        if SECRET_KEY.match(variable.get("key", "")):
            variable["value"] = ""
    clean_auth(collection)
    walk(collection["item"], False)
    with open(target, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(collection, handle, ensure_ascii=False, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
