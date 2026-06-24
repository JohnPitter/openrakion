"""Anexa frida_findroom.js no cliente (PID arg) — descobre qual funcao-tela constroi o game room.
Uso: py tools/frida_findroom.py <PID>   (ou sem PID = auto-detecta)
"""
import frida, sys, time, os
JS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "frida_findroom.js")
def on_message(msg, data):
    if msg.get("type") == "send": print(msg.get("payload"), flush=True)
    elif msg.get("type") == "error": print("JS-ERROR:", msg.get("description"), flush=True)
def find_pid():
    for p in frida.get_local_device().enumerate_processes():
        if "rakion" in p.name.lower(): return p.pid
    return None
pid = int(sys.argv[1]) if len(sys.argv) > 1 else find_pid()
if not pid: print("[erro] cliente nao rodando"); sys.exit(1)
print("[driver] anexando no pid", pid, flush=True)
session = frida.attach(pid)
script = session.create_script(open(JS, encoding="utf-8").read())
script.on("message", on_message); script.load()
print("[driver] hook ativo. ENTRE numa SALA.", flush=True)
try: time.sleep(1800)
except KeyboardInterrupt: pass
