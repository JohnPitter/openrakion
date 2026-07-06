"""Anexa o Frida no CLIENTE rodando e carrega frida_roombtn.js — loga a criacao dos botoes ao
entrar numa sala, p/ LOCALIZAR a funcao da tela do game room (em gamemp.dll) a patchar.

Uso:
  1) Abra o jogo (launcher) e fique no LOBBY (lista de salas).
  2) python tools/frida_roombtn.py            # acha o cliente por nome
     (ou)  python tools/frida_roombtn.py 1234  # PID do cliente
  3) ENTRE numa sala (game room). Os 6 botoes serao logados aqui com 'caller=gamemp.dll+0x....'.
  4) Me mande a saida (as linhas SetText/CTOR/SetPos/SetSize com os callers).

Precisa: pip install frida   (o projeto ja usa frida em rakion-work).
"""
import frida, sys, time, os

JS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "frida_roombtn.js")
CLIENT_NAMES = ("rakion.exe", "rakion.bin", "Rakion.exe")

def on_message(msg, data):
    t = msg.get("type")
    if t == "send":
        print(msg.get("payload"), flush=True)
    elif t == "error":
        print("JS-ERROR:", msg.get("description"), flush=True)
    else:
        print(msg, flush=True)

def find_client_pid():
    # frida 16: enumerate_processes saiu do modulo -> via device local
    dev = frida.get_local_device()
    for p in dev.enumerate_processes():
        if p.name in CLIENT_NAMES or "rakion" in p.name.lower():
            return p.pid, p.name
    return None, None

def main():
    if len(sys.argv) > 1:
        pid = int(sys.argv[1]); name = "(pid)"
    else:
        pid, name = find_client_pid()
        if not pid:
            print("[erro] cliente Rakion nao encontrado. Abra o jogo primeiro (ou passe o PID).")
            return
    print("[driver] anexando no %s pid=%d ..." % (name, pid), flush=True)
    session = frida.attach(pid)
    with open(JS, "r", encoding="utf-8") as f:
        src = f.read()
    script = session.create_script(src)
    script.on("message", on_message)
    script.load()
    print("[driver] hooks ativos. ENTRE numa SALA no jogo. (capturando ~30min; Ctrl+C p/ sair)", flush=True)
    try:
        time.sleep(1800)
    except KeyboardInterrupt:
        pass
    print("[driver] fim.", flush=True)

if __name__ == "__main__":
    main()
