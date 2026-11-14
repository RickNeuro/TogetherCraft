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
        grep -qi "Minecraft · Vanilla" "$TMP_HTML" || fail "homepage does not render the Vanilla direction"
        grep -qi "Minecraft · Create" "$TMP_HTML" || fail "homepage does not render the Create direction"
        grep -q "Factorio" "$TMP_HTML" || fail "homepage does not render the Factorio direction"
        for address in "127.0.0.1" "fried-newer.tun.ply.gg" "shrimp-crafts.tun.ply.gg" "25565" "25566"; do
            ! grep -q "$address" "$TMP_HTML" || fail "homepage exposes a connection address: $address"
        done
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
    if "Заявки" not in body:
        raise SystemExit("Admin page did not render its expected content")

# A signed-in administrator sees a selector and two truly separate Minecraft pages.
with opener.open(base_url.rstrip("/") + "/Home/Play", timeout=10) as response:
    page = response.read().decode("utf-8", errors="replace")
    if response.status != 200 or "Minecraft · Vanilla" not in page or "Minecraft · Create" not in page:
        raise SystemExit("The connection selector did not show both independent Minecraft worlds")
    if "/Home/Vanilla" not in page or "/Home/Create" not in page:
        raise SystemExit("The selector does not link to separate Vanilla and Create pages")
    if "vanilla-ci.example.invalid" in page or "create-ci.example.invalid" in page:
        raise SystemExit("The selector unexpectedly contains a server address")

for path, own_address, other_address in (
    ("/Home/Vanilla", "vanilla-ci.example.invalid", "create-ci.example.invalid"),
    ("/Home/Create", "create-ci.example.invalid", "vanilla-ci.example.invalid"),
):
    with opener.open(base_url.rstrip("/") + path, timeout=10) as response:
        page = response.read().decode("utf-8", errors="replace")
        if response.status != 200 or own_address not in page:
            raise SystemExit(f"{path} did not render its own configured address")
        if other_address in page:
            raise SystemExit(f"{path} contains the other server's address")

with opener.open(base_url.rstrip("/") + "/Admin/Users", timeout=10) as response:
    page = response.read().decode("utf-8", errors="replace")
    if response.status != 200 or "Пользователи" not in page:
        raise SystemExit("/Admin/Users did not render for the administrator")

# The private status API remains available to administrators.
with opener.open(base_url.rstrip("/") + "/api/minecraft/servers", timeout=10) as response:
    import json
    status = json.loads(response.read().decode("utf-8"))
    if response.status != 200 or not isinstance(status.get("fabric"), dict) or not isinstance(status.get("create"), dict):
        raise SystemExit("Admin Minecraft status API response is incomplete")

print("Authentication OK: admin can open applications, connections, user roles and private status API")
PY

echo "Smoke tests passed: PostgreSQL health/permissions, backend routes/authentication, homepage and local CSS/JS assets."


# Game addresses, technical status and host metrics must not be available anonymously.
for route in "/Home/Play" "/Home/Vanilla" "/Home/Create" "/Home/Server" "/Admin" "/Admin/Users" "/api/minecraft/servers" "/api/minecraft/status" "/api/metrics"; do
    status_code="$(curl --silent --show-error --output "$TMP_HTML" --write-out '%{http_code}' "$BASE_URL$route")" \
        || fail "request failed for $route"
    [[ "$status_code" == "302" ]] || fail "$route should redirect anonymous users to login, got HTTP $status_code"
done
echo "Private connection page and technical APIs correctly reject anonymous visitors."
