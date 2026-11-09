#!/usr/bin/env bash
set -Eeuo pipefail

BASE_URL="${1:-http://127.0.0.1:8080}"
TMP_HTML="$(mktemp)"
trap 'rm -f "$TMP_HTML" "${TMP_HTML}.homepage"' EXIT

fail() {
    echo "SMOKE TEST FAILED: $*" >&2
    exit 1
}

HEALTH="$(curl --fail --silent --show-error --max-time 10 "$BASE_URL/healthz")" \
    || fail "/healthz did not respond successfully"
[[ "$HEALTH" == *'"status":"ok"'* ]] || fail "application health is not ok: $HEALTH"
[[ "$HEALTH" == *'"database":"connected"'* ]] || fail "PostgreSQL is not connected: $HEALTH"

for route in "/" "/Account/Login" "/Account/Register"; do
    status="$(curl --silent --show-error --output "$TMP_HTML" --write-out '%{http_code}' "$BASE_URL$route")" \
        || fail "request failed for $route"
    [[ "$status" == "200" ]] || fail "$route returned HTTP $status"
    if [[ "$route" == "/" ]]; then
        grep -q "TogetherCraft" "$TMP_HTML" || fail "homepage does not contain TogetherCraft"
        grep -q "Игровой мир 01" "$TMP_HTML" || fail "homepage does not render the first game world"
        grep -q "Игровой мир 02" "$TMP_HTML" || fail "homepage does not render the second game world"
        cp "$TMP_HTML" "${TMP_HTML}.homepage"
    fi
done

# Verify all local CSS and JavaScript assets referenced by the public homepage.
python3 - "$BASE_URL" "${TMP_HTML}.homepage" <<'PY'
from html.parser import HTMLParser
from urllib.parse import urljoin, urlparse
from urllib.request import urlopen
import sys

base_url, html_path = sys.argv[1:]

class Assets(HTMLParser):
    def __init__(self):
        super().__init__()
        self.urls = []
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "link" and "stylesheet" in attrs.get("rel", "").lower().split():
            value = attrs.get("href")
        elif tag == "script":
            value = attrs.get("src")
        else:
            value = None
        if value:
            parsed = urlparse(value)
            if not parsed.scheme and not value.startswith("//"):
                self.urls.append(value)

with open(html_path, encoding="utf-8") as stream:
    parser = Assets()
    parser.feed(stream.read())

if not parser.urls:
    raise SystemExit("No local CSS or JavaScript assets were found on the homepage")

for path in parser.urls:
    url = urljoin(base_url.rstrip("/") + "/", path.lstrip("/"))
    try:
        with urlopen(url, timeout=10) as response:
            if response.status != 200:
                raise SystemExit(f"Asset returned HTTP {response.status}: {url}")
    except Exception as error:
        raise SystemExit(f"Asset request failed: {url}: {error}") from error
    print(f"Asset OK: {path}")
PY

# Exercise cookie authentication, anti-forgery validation and the AdminOnly route.
python3 - "$BASE_URL" "${CI_ADMIN_USERNAME:-ci-admin}" "${CI_ADMIN_PASSWORD:?CI_ADMIN_PASSWORD is required}" <<'PY'
from html.parser import HTMLParser
from http.cookiejar import CookieJar
from urllib.parse import urlencode, urlparse
from urllib.request import HTTPCookieProcessor, Request, build_opener
import sys

base_url, username, password = sys.argv[1:]

class TokenParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.token = None
    def handle_starttag(self, tag, attrs):
        if tag != "input":
            return
        attrs = dict(attrs)
        if attrs.get("name") == "__RequestVerificationToken":
            self.token = attrs.get("value")

cookies = CookieJar()
opener = build_opener(HTTPCookieProcessor(cookies))
login_url = base_url.rstrip("/") + "/Account/Login"
with opener.open(login_url, timeout=10) as response:
    parser = TokenParser()
    parser.feed(response.read().decode("utf-8"))

if not parser.token:
    raise SystemExit("Login page did not render an anti-forgery token")

payload = urlencode({
    "Username": username,
    "Password": password,
    "__RequestVerificationToken": parser.token,
}).encode("utf-8")
request = Request(login_url, data=payload, headers={"Content-Type": "application/x-www-form-urlencoded"})
with opener.open(request, timeout=10) as response:
    final_path = urlparse(response.geturl()).path
    body = response.read().decode("utf-8", errors="replace")
    if response.status != 200 or final_path.rstrip("/") not in ("/Admin", "/Admin/Index"):
        raise SystemExit(f"Admin login failed: HTTP {response.status}, final path {final_path}")
    if "Заявки на доступ" not in body:
        raise SystemExit("Admin page did not render its expected content")

print("Authentication OK: test admin signed in and opened the AdminOnly page")
PY

echo "Smoke tests passed: PostgreSQL health/permissions, backend routes/authentication, homepage and local CSS/JS assets."


# Minecraft status is public; host metrics remain protected.
STATUS_CODE="$(curl --silent --show-error --output "$TMP_HTML" --write-out '%{http_code}' "$BASE_URL/api/minecraft/servers")" \
    || fail "public Minecraft status request failed"
[[ "$STATUS_CODE" == "200" ]] || fail "/api/minecraft/servers returned HTTP $STATUS_CODE"
python3 - "$TMP_HTML" <<'PY'
import json
import sys
with open(sys.argv[1], encoding="utf-8") as stream:
    data = json.load(stream)
for world in ("fabric", "create"):
    if not isinstance(data.get(world), dict):
        raise SystemExit(f"Public API did not return {world}")
    if "isOnline" not in data[world] or "address" not in data[world]:
        raise SystemExit(f"{world} status response is incomplete")
print("Public Minecraft status API OK")
PY
METRICS_CODE="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' "$BASE_URL/api/metrics")" \
    || fail "metrics authorization request failed"
[[ "$METRICS_CODE" == "302" ]] || fail "/api/metrics should redirect anonymous users to login, got HTTP $METRICS_CODE"
echo "Public/private API checks passed."
