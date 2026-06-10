"""Auth web do launcher (reimplementacao fiel em Python das paginas PHP da
rakion-tutorial/web). Serve launcherlogin.php + fetch + note + file na :80.
Loga cada request em C:\\temp\\web.log para a gente ver o que o launcher pede."""
import http.server, urllib.parse, hashlib, time, pymysql

LOG = r"C:\temp\web.log"
def log(msg):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(f"{time.strftime('%H:%M:%S')} {msg}\n")

def db():
    return pymysql.connect(host="127.0.0.1", user="root", password="123456",
                           database="rakion", connect_timeout=5)

class H(http.server.BaseHTTPRequestHandler):
    def _send(self, body):
        b = body.encode("latin1") if isinstance(body, str) else body
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def handle_req(self, query):
        u = urllib.parse.urlparse(self.path)
        path = u.path.lower()
        q = urllib.parse.parse_qs(query)
        log(f"{self.command} {self.path}  qs={query}")

        if "launcherlogin" in path:
            user = q.get("user", [""])[0]
            passhex = q.get("pass", [""])[0]
            try: pw = bytes.fromhex(passhex).decode("latin1")
            except Exception: pw = ""
            ok = False
            try:
                c = db(); cur = c.cursor()
                cur.execute("SELECT 1 FROM user WHERE id=%s AND password=%s", (user, pw))
                ok = cur.fetchone() is not None
                c.close()
            except Exception as e:
                log(f"  login DB err: {e}"); self._send("[Error]: " + str(e)); return
            if ok:
                h = hashlib.sha1((user + "freeclient" + passhex).encode("latin1")).hexdigest()
                log(f"  login OK user={user} -> {h}")
                self._send(h)
            else:
                log(f"  login FAIL user={user} pw={pw!r}")
                self._send("[Error]: 1")

        elif "fetch" in path:
            # QUERY_STRING "APP&VER"; VerLimit==ver -> resposta vazia (atualizado)
            log("  fetch -> vazio (atualizado)")
            self._send("")

        else:
            # note_rakion.htm, file.php, config.php, etc -> 200 vazio
            self._send("")

    def do_GET(self):
        self.handle_req(urllib.parse.urlparse(self.path).query)

    def do_POST(self):
        n = int(self.headers.get("Content-Length", 0) or 0)
        body = self.rfile.read(n).decode("latin1") if n else ""
        self.handle_req(body or urllib.parse.urlparse(self.path).query)

    def log_message(self, *a): pass

if __name__ == "__main__":
    open(LOG, "w").write(f"auth web iniciada {time.strftime('%H:%M:%S')}\n")
    srv = http.server.ThreadingHTTPServer(("0.0.0.0", 80), H)
    print("auth web :80 (log C:\\temp\\web.log)")
    srv.serve_forever()
