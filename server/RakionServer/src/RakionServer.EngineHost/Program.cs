using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace RakionServer.EngineHost;

// Headless bot peer — carrega engine.dll, faz handshake P2P via Start_AtClient_t,
// e lê comandos "move x y z yaw" do stdin para enviar 0x30a via SetAction+SendAction.
// Cada bot roda em processo x86 separado (engine.dll usa globals).
//
// Protocolo stdin/stdout:
//   EngineHost -> stdout: "READY" (handshake completo), "FAIL" (erro), "OK" (comando executado)
//   stdin -> EngineHost:   "move x y z yaw", "quit"
internal static class Program
{
    const uint SEM_FAILCRITICALERRORS = 0x0001, SEM_NOGPFAULTERRORBOX = 0x0002, SEM_NOOPENFILEERRORBOX = 0x8000;
    const long PREFERRED_BASE = 0x36000000;
    const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
    const uint DONT_RESOLVE_DLL_REFERENCES = 0x00000001;
    // Busca MODERNA (o host .NET usa LOAD_LIBRARY_SEARCH_* restrito → ALTERED_SEARCH_PATH é ignorada e as deps
    // da engine no bin não são achadas = ERROR_MOD_NOT_FOUND 126). DLL_LOAD_DIR adiciona o dir da DLL à busca.
    const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
    const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
    const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

    // VAs fixos (base 0x36000000, sem ASLR)
    const long VA_START_ATCLIENT = 0x3610ea00;

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryExW(string f, IntPtr h, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);
    [DllImport("kernel32")] static extern uint SetErrorMode(uint m);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectoryW(string d);
    [DllImport("kernel32", SetLastError = true)] static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr AddDllDirectory(string dir);
    const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetCurrentDirectoryW(string d);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string n);
    [DllImport("kernel32", SetLastError = true)] static extern bool VirtualProtect(IntPtr addr, uint size, uint newProt, out uint oldProt);
    const uint PAGE_EXECUTE_READWRITE = 0x40;

    // VEH p/ cravar o endereço do AV nativo (o runtime .NET engole o 0xC0000005 e só reporta "at Main" — sem
    // o offset em engine.dll). Loga ExceptionAddress + endereço faltante e deixa crashar normal.
    [DllImport("kernel32")] static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandlerDel handler);
    delegate int VectoredHandlerDel(IntPtr exceptionInfo);
    static VectoredHandlerDel? _veh;   // referência viva (anti-GC do delegate nativo)
    static int OnVectoredException(IntPtr info)
    {
        // EXCEPTION_POINTERS.ExceptionRecord @ +0. EXCEPTION_RECORD (x86): code@0, flags@4, next@8, address@0xC,
        // nParams@0x10, Information[0]@0x14 (0=read/1=write/8=exec), Information[1]@0x18 (endereço faltante).
        try
        {
            IntPtr rec = Marshal.ReadIntPtr(info, 0);
            uint code = (uint)Marshal.ReadInt32(rec, 0);
            if (code == 0xC0000005)
            {
                long addr = Marshal.ReadIntPtr(rec, 0x0C).ToInt64();
                long rw = Marshal.ReadIntPtr(rec, 0x14).ToInt64();
                long fault = Marshal.ReadIntPtr(rec, 0x18).ToInt64();
                Console.Error.WriteLine($"[VEH] AV @0x{addr:x} (engine.dll+0x{addr - 0x36000000:x}) op={(rw == 1 ? "WRITE" : rw == 8 ? "EXEC" : "READ")} endereco=0x{fault:x}");
                Console.Error.Flush();
            }
        }
        catch { }
        return 0;   // EXCEPTION_CONTINUE_SEARCH
    }

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate IntPtr CtorCharPtr(IntPtr self, IntPtr cstr);
    [StructLayout(LayoutKind.Sequential)] struct CTStringV { public IntPtr p; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SEInitDel(CTStringV s);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int PrepareForUseDel(IntPtr self, int useNet, int client);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void VoidThisDel(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint SetAbortBehaviorDel(uint f, uint m);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void SetActionDel(IntPtr self, IntPtr action);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint CrcDel(uint init, byte[] data, int len);
    // CONNECT de baixo nível REAL da engine: void Client_OpenNet_t(ULONG addr) — captura o formato do connect.
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void ClientOpenNetDel(IntPtr self, uint addr);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int ClientUpdateDel(IntPtr self);
    // dumpnpc: dec_New (Cdecl, void->CEntity*) e GetCreateInfo (ThisCall, vtable[1], escreve num CNetworkMessage*)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr NewEntityDel();
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void GetCreateInfoDel(IntPtr self, IntPtr msg);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void NetMsgCtorDel(IntPtr self, int mtType);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int GetterDel(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GameCreateDel();          // ?GAME_Create@@YAPAVCGame@@XZ (gamemp.dll)
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void GameInitDel(IntPtr self, CTStringV gms);  // CGame::Initialize

    static IntPtr _pNetwork, _pTimer;
    static VoidThisDel _mainLoop, _handleTimers;
    static SetActionDel _setAction;
    static VoidThisDel _sendAction;
    static IntPtr _localPlayer;   // CPlayerSource*

    static int Main(string[] args)
    {
        string bin = args.Length > 0 ? args[0] : @"C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin";
        string hostEp = args.Length > 1 ? args[1] : "127.0.0.1:2301";

        Console.Error.WriteLine($"[bot] x86={IntPtr.Size == 4} bin={bin} host={hostEp}");
        if (IntPtr.Size != 4) { Console.Error.WriteLine("[FATAL] precisa x86"); return 3; }

        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
        _veh = OnVectoredException; AddVectoredExceptionHandler(1, _veh);   // crava o offset do AV nativo
        SetDllDirectoryW(bin);
        SetCurrentDirectoryW(bin);   // CWD = bin ANTES do LoadLibrary (DllMain pode depender)
        // Opta pelo modo de busca de DLL MODERNO (necessário p/ as flags LOAD_LIBRARY_SEARCH_* e p/ AddDllDirectory
        // funcionarem) e adiciona o bin como USER_DIR — fix do ERROR_MOD_NOT_FOUND no host .NET (busca restrita).
        bool sdd = SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        IntPtr add = AddDllDirectory(bin);
        Console.Error.WriteLine($"[bot] SetDefaultDllDirectories={sdd} AddDllDirectory(bin)=0x{add.ToInt64():x}");

        string dllPath = Path.Combine(bin, "engine.dll");
        Console.Error.WriteLine($"[bot] tentando carregar: {dllPath} exists={File.Exists(dllPath)} CWD={Environment.CurrentDirectory}");

        // 1ª tentativa: busca MODERNA (dir da DLL + System32 + default dirs) — fix do ERROR_MOD_NOT_FOUND no host .NET.
        IntPtr he = LoadLibraryExW(dllPath, IntPtr.Zero,
            LOAD_LIBRARY_SEARCH_USER_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR);
        int loadErr = Marshal.GetLastWin32Error();
        Console.Error.WriteLine($"[bot] LoadLibraryExW(USER_DIRS) = 0x{he.ToInt64():x} err={loadErr}");
        if (he == IntPtr.Zero)
        {
            he = LoadLibraryExW(dllPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
            loadErr = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[bot] LoadLibraryExW(ALTERED) = 0x{he.ToInt64():x} err={loadErr}");
        }
        if (he == IntPtr.Zero)
        {
            he = LoadLibraryExW(dllPath, IntPtr.Zero, 0);
            loadErr = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[bot] LoadLibraryExW(0) = 0x{he.ToInt64():x} err={loadErr}");
        }
        if (he == IntPtr.Zero)
        {
            he = LoadLibraryExW("engine.dll", IntPtr.Zero, DONT_RESOLVE_DLL_REFERENCES);
            loadErr = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[bot] LoadLibraryExW(DONT_RESOLVE) = 0x{he.ToInt64():x} err={loadErr}");
        }
        if (he == IntPtr.Zero) { Console.Error.WriteLine($"[FATAL] engine.dll não carregou (tentou 3 flags). WinError={loadErr}"); return 2; }

        // _bDedicatedServer = 1
        IntPtr pDed = GetProcAddress(he, "?_bDedicatedServer@@3HA");
        if (pDed != IntPtr.Zero) Marshal.WriteInt32(pDed, 1);

        // SE_InitEngine("Rakion")
        var ctorStr = GetDel<CtorCharPtr>(he, "??0CTString@@QAE@PBD@Z");
        var seInit = GetDel<SEInitDel>(he, "?SE_InitEngine@@YAXVCTString@@@Z");
        IntPtr ctbuf = Marshal.AllocHGlobal(8); Marshal.WriteIntPtr(ctbuf, IntPtr.Zero);
        ctorStr(ctbuf, Marshal.StringToHGlobalAnsi("Rakion"));
        CTStringV gid; gid.p = Marshal.ReadIntPtr(ctbuf);
        seInit(gid);
        Console.Error.WriteLine("[bot] SE_InitEngine OK");

        // Resolver globals
        IntPtr ppNet = GetProcAddress(he, "?_pNetwork@@3PAVCNetworkLibrary@@A");
        IntPtr ppTimer = GetProcAddress(he, "?_pTimer@@3PAVCTimer@@A");
        _pNetwork = Marshal.ReadIntPtr(ppNet);
        _pTimer = ppTimer != IntPtr.Zero ? Marshal.ReadIntPtr(ppTimer) : IntPtr.Zero;
        if (_pNetwork == IntPtr.Zero) { Console.Error.WriteLine("[FATAL] _pNetwork null"); return 5; }

        // Resolver funções
        _mainLoop = GetDel<VoidThisDel>(he, "?MainLoop@CNetworkLibrary@@QAEXXZ");
        _handleTimers = ppTimer != IntPtr.Zero && _pTimer != IntPtr.Zero
            ? GetDel<VoidThisDel>(he, "?HandleTimerHandlers@CTimer@@QAEXXZ") : null;

        // PrepareForUse(1, 1) — client mode
        IntPtr pComm = GetProcAddress(he, "?_cmiComm@@3VCCommunicationInterface@@A");
        var prep = GetDel<PrepareForUseDel>(he, "?PrepareForUse@CCommunicationInterface@@QAEHHH@Z");
        int r = prep(pComm, 1, 1);
        if (r == 0) { Console.Error.WriteLine("[FATAL] PrepareForUse falhou"); return 7; }
        Console.Error.WriteLine("[bot] socket ativo (client mode)");

        // MODO CAPTURA: chama o Client_OpenNet_t REAL apontado a um listener nosso (arg "capture <ip-hex>") e
        // bombeia Client_Update — captura o FORMATO REAL do connect da engine (CONNECT_REQUEST) p/ validar o
        // 0x0203 sintético. Sai depois (não tenta o handshake completo).
        if (args.Length > 2 && args[2] == "capture")
        {
            uint addr = args.Length > 3 ? Convert.ToUInt32(args[3], 16) : 0x0100007fu;   // default 127.0.0.1
            var openNet = GetDel<ClientOpenNetDel>(he, "?Client_OpenNet_t@CCommunicationInterface@@QAEXK@Z");
            var clientUpd = GetDel<ClientUpdateDel>(he, "?Client_Update@CCommunicationInterface@@QAEHXZ");
            if (openNet == null) { Console.Error.WriteLine("[capture] Client_OpenNet_t não resolvido"); return 11; }
            // net_iPort @ 0x36294e60 (disasm: Client_OpenNet_t lê WORD daqui p/ a porta destino). Força 25600 p/
            // o connect cair no nosso listener. Lê o valor original antes (diagnóstico).
            IntPtr pPort = (IntPtr)0x36294e60;
            int origPort = Marshal.ReadInt32(pPort);
            Marshal.WriteInt32(pPort, 25700);   // DESTINO != porta local da engine (25600) p/ não conflitar c/ o listener
            // PATCH: Client_OpenNet_t crasha no bloco de progress-hook (@0x360f89ca, gated por ctRetries>1) ANTES
            // de formar/enviar o connect (@0x360f8a07). Patcho o jle @0x360f89c8 (7e 3d) -> jmp (eb 3d) p/ pular
            // o progress e cair direto no envio. Disasm cravado.
            PatchByte(0x360f89c8, 0xeb, "pula progress-hook (jle->jmp)");
            // Client_Update @0x360f8210: o check 0x360ed610 (@0x360f8257) retorna 0 no headless -> jne @0x360f8263
            // pula p/ a saida FALSE (@0x360f8265) -> retry loop quebra ANTES de enviar (0 datagramas). Força o jne
            // (75) -> jmp (eb) p/ SEMPRE processar/enviar.
            PatchByte(0x360f8263, 0xeb, "Client_Update sempre processa (jne->jmp)");
            Console.Error.WriteLine($"[capture] net_iPort original={origPort} -> destino forçado 25700; Client_OpenNet_t(0x{addr:x8}) DIRETO");
            // Chamada DIRETA (sem thread/pump externo): openNet se auto-bombeia via Client_Update interno; o
            // pump externo só disputava o cm_csComm. Bloqueia ~300ms (3 retries) e lança timeout — o connect
            // já foi ENVIADO no 1º Client_Update interno. Pump algumas vezes ANTES p/ a comm assentar.
            for (int i = 0; i < 5; i++) { try { clientUpd?.Invoke(pComm); } catch { } Thread.Sleep(20); }
            try { openNet(pComm, addr); Console.Error.WriteLine("[capture] OpenNet retornou (sem timeout?!)"); }
            catch (Exception ex) { Console.Error.WriteLine($"[capture] OpenNet lançou (esperado: timeout p/ host falso): {ex.Message}"); }
            for (int i = 0; i < 10; i++) { try { clientUpd?.Invoke(pComm); } catch { } Thread.Sleep(30); }
            Console.Error.WriteLine("[capture] fim");
            return 0;
        }

        // MODO DUMPNPC: carrega a entitiesmp (rakion-new, desempacotada), cria um CNpc via dec_New e chama
        // GetCreateInfo (vtable[1]) num CNetworkMessage forjado -> dumpa os bytes EXATOS do blob do 0x307.
        // arg2="dumpnpc" [arg3=export do _DLLClass, default CNpcGolem1_DLLClass] [arg4=path entitiesmp].
        if (args.Length > 2 && args[2] == "dumpnpc")
            return DumpNpc(args.Length > 3 ? args[3] : "CNpcGolem1_DLLClass",
                           args.Length > 4 ? args[4] : @"C:\Users\joaop\Desenvolvimento\Rakion\rakion-new\Bin\entitiesmp.dll");

        // GAME_Create: carrega a gamemp.dll e cria o objeto de JOGO do Rakion (CGame). SEM isto o CGame global
        // (engine.dll 0x3636F260) fica NULL e QUALQUER caminho que toca game-state (handshake, AddPlayer) deref
        // null → AV @engine+0x10cde1 (`mov ecx,[0x3636F260]; mov edx,[ecx]`). Segue o template do DedicatedServer
        // da SE1 (init engine → GAME_Create → session). gamemp.dll exporta `?GAME_Create@@YAPAVCGame@@XZ` (C++
        // mangled — Rakion não usou extern "C"). Escrevemos o CGame no global que a engine lê.
        IntPtr hGame = LoadLibraryExW(Path.Combine(bin, "gamemp.dll"), IntPtr.Zero,
            LOAD_LIBRARY_SEARCH_USER_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR);
        Console.Error.WriteLine($"[bot] LoadLibrary gamemp.dll = 0x{hGame.ToInt64():x} err={Marshal.GetLastWin32Error()}");
        if (hGame != IntPtr.Zero)
        {
            IntPtr pGameCreate = GetProcAddress(hGame, "?GAME_Create@@YAPAVCGame@@XZ");
            if (pGameCreate != IntPtr.Zero)
            {
                IntPtr pGame = Marshal.GetDelegateForFunctionPointer<GameCreateDel>(pGameCreate)();
                Marshal.WriteIntPtr((IntPtr)0x3636F260, pGame);   // seta o CGame global que a engine lê (0x3636F260)
                Console.Error.WriteLine($"[bot] GAME_Create -> CGame=0x{pGame.ToInt64():x}; _pGame(0x3636F260) setado");
            }
            else Console.Error.WriteLine("[bot] GAME_Create NÃO resolvido (export ausente?)");
        }

        // HIPÓTESE H3 (2026-07-01): o crash do AddPlayer (docs/headless-engine-re §12.1) é o caminho SERVIDOR —
        // ga_IsServer=1 (via _bDedicatedServer=1, necessário p/ o SE_InitEngine headless SEM renderer) faz a criação
        // do player COPIAR o singleton de match `0x3636F338` (zerado headless) → AV. O caminho CLIENTE (ga_IsServer=0)
        // NÃO copia — só REFERENCIA a entidade, que é criada pelo MASTER (o humano) quando um peer entra. Zeramos
        // ga_IsServer @ _pNetwork+0x14 DEPOIS da init (que precisava do server-flag) e ANTES do connect, p/ o bot
        // ser criado como um 2º peer real pelo host humano. Ângulo NÃO testado pelo doc (que só mediu server=1).
        int prevIsServer = Marshal.ReadInt32(_pNetwork, 0x14);
        Marshal.WriteInt32(_pNetwork, 0x14, 0);
        Console.Error.WriteLine($"[bot] ga_IsServer(_pNetwork+0x14) {prevIsServer} -> 0 (caminho CLIENTE do AddPlayer; evita o crash do singleton de match)");

        // Handshake em thread separada (Start_AtClient_t bloqueia)
        IntPtr sessionState = Marshal.ReadIntPtr(_pNetwork, 0x24);
        var startClient = Marshal.GetDelegateForFunctionPointer<VoidThisDel>((IntPtr)VA_START_ATCLIENT);
        bool handshakeDone = false;
        Exception? handshakeError = null;

        var hsThread = new Thread(() =>
        {
            try { startClient(sessionState); handshakeDone = true; }
            catch (Exception ex) { handshakeError = ex; }
        });
        hsThread.IsBackground = true;
        hsThread.Start();

        // Pump main loop enquanto handshake roda
        Console.Error.WriteLine("[bot] handshake em andamento (pump)...");
        for (int t = 0; t < 600 && !handshakeDone && handshakeError == null; t++) // ~30s timeout
        {
            _handleTimers?.Invoke(_pTimer);
            _mainLoop?.Invoke(_pNetwork);
            Thread.Sleep(50);
        }

        if (handshakeError != null)
        {
            Console.Error.WriteLine($"[bot] handshake FALHOU: {handshakeError.Message}");
            Console.WriteLine("FAIL");
            return 9;
        }
        if (!handshakeDone)
        {
            Console.Error.WriteLine("[bot] handshake TIMEOUT (30s)");
            Console.WriteLine("FAIL");
            return 10;
        }

        Console.Error.WriteLine("[bot] handshake COMPLETO — peer registrado!");
        Console.Out.Flush();

        // Resolver SetAction/SendAction para enviar 0x30a
        _setAction = GetDel<SetActionDel>(he, "?SetAction@CPlayerSource@@QAEXABVCPlayerAction@@@Z");
        _sendAction = GetDel<VoidThisDel>(he, "?SendAction@CPlayerSource@@QAEXXZ");
        _localPlayer = Marshal.ReadIntPtr(_pNetwork, 0x2c);   // GetLocalPlayer

        if (_localPlayer == IntPtr.Zero || _setAction == null || _sendAction == null)
        {
            Console.Error.WriteLine("[bot] SetAction/SendAction/LocalPlayer não resolvido — sem movimento");
            Console.WriteLine("READY_NOACTION");
        }
        else
        {
            Console.Error.WriteLine($"[bot] LocalPlayer=0x{_localPlayer.ToInt64():x} SetAction/SendAction OK");
            Console.WriteLine("READY");
        }
        Console.Out.Flush();

        // Loop de comandos: "move x y z yaw" | "quit"
        while (true)
        {
            string? line = Console.In.ReadLine();
            if (line == null || line == "quit") break;

            if (line.StartsWith("move ") && _localPlayer != IntPtr.Zero)
            {
                var parts = line.Split(' ');
                if (parts.Length >= 5 &&
                    float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out float z) &&
                    float.TryParse(parts[4], System.Globalization.CultureInfo.InvariantCulture, out float yaw))
                {
                    SendMove(x, y, z, yaw);
                    Console.WriteLine("OK");
                }
                else Console.WriteLine("ERR parse");
            }
            else Console.WriteLine("ERR unknown");
            Console.Out.Flush();
        }

        Console.Error.WriteLine("[bot] encerrando");
        return 0;
    }

    // Cria um CNpc via dec_New e dumpa GetCreateInfo (vtable[1]) num CNetworkMessage forjado.
    static int DumpNpc(string dllClassExport, string entitiesPath)
    {
        Console.Error.WriteLine($"[dumpnpc] entitiesmp={entitiesPath} exists={File.Exists(entitiesPath)} class={dllClassExport}");
        IntPtr ent = LoadLibraryExW(entitiesPath, IntPtr.Zero,
            LOAD_LIBRARY_SEARCH_USER_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR);
        if (ent == IntPtr.Zero) ent = LoadLibraryExW(entitiesPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
        int err = Marshal.GetLastWin32Error();
        Console.Error.WriteLine($"[dumpnpc] entitiesmp base=0x{ent.ToInt64():x} err={err}");
        if (ent == IntPtr.Zero) { Console.Error.WriteLine("[FATAL] entitiesmp não carregou"); return 20; }

        IntPtr pCls = GetProcAddress(ent, dllClassExport);
        if (pCls == IntPtr.Zero) { Console.Error.WriteLine($"[FATAL] export {dllClassExport} não achado"); return 21; }
        int classId = Marshal.ReadInt32(pCls, 0x28);
        IntPtr decNew = Marshal.ReadIntPtr(pCls, 0x30);   // já relocado pelo loader
        Console.Error.WriteLine($"[dumpnpc] _DLLClass@0x{pCls.ToInt64():x} id=0x{classId:x} dec_New=0x{decNew.ToInt64():x}");
        if (decNew == IntPtr.Zero) { Console.Error.WriteLine("[FATAL] dec_New null"); return 22; }

        IntPtr entity;
        try { entity = Marshal.GetDelegateForFunctionPointer<NewEntityDel>(decNew)(); }
        catch (Exception ex) { Console.Error.WriteLine($"[FATAL] dec_New lançou: {ex.Message}"); return 23; }
        Console.Error.WriteLine($"[dumpnpc] entity=0x{entity.ToInt64():x}");
        if (entity == IntPtr.Zero) { Console.Error.WriteLine("[FATAL] entity null"); return 24; }

        IntPtr vtable = Marshal.ReadIntPtr(entity);
        IntPtr getCreateInfo = Marshal.ReadIntPtr(vtable, 4);   // vtable[1]
        Console.Error.WriteLine($"[dumpnpc] vtable=0x{vtable.ToInt64():x} GetCreateInfo=0x{getCreateInfo.ToInt64():x}");
        // DIAGNÓSTICO: faixa do engine.dll (p/ marcar slots inherited = Write_t/Read_t) + dump vtable[60..78]
        IntPtr engBase = GetModuleHandleW("engine.dll");
        long engLo = engBase.ToInt64(), engHi = engLo + 0x400000;
        Console.Error.WriteLine($"[diag] engine.dll base=0x{engLo:x}; vtable[60..78] (eng=inherited):");
        for (int s = 60; s <= 78; s++)
        {
            long fn = Marshal.ReadIntPtr(vtable, s * 4).ToInt64();
            string tag = (fn >= engLo && fn < engHi) ? " <ENGINE>" : "";
            Console.Error.WriteLine($"   vtable[{s}] +0x{s*4:x}: 0x{fn:x}{tag}");
        }
        Console.Error.WriteLine($"[diag] state(+0x100)={Marshal.ReadInt32(entity,0x100)} HP(+0x270)={Marshal.ReadInt32(entity,0x270):x} team(+0x26c)={Marshal.ReadByte(entity,0x26c):x} typeId(+0x3920)={Marshal.ReadByte(entity,0x3920):x}");

        // A entidade do dec_New tem en_pecClass(+0x78)=null -> GetCreateInfo crasha lendo [+0x78]->[+0x20]->classId.
        // O CreateEntity normalmente seta en_pecClass; aqui forjo um CEntityClass cujo +0x20 aponta ao _DLLClass.
        IntPtr fakeClass = Marshal.AllocHGlobal(0x40);
        for (int i = 0; i < 0x40; i++) Marshal.WriteByte(fakeClass, i, 0);
        Marshal.WriteIntPtr(fakeClass, 0x20, pCls);   // CEntityClass+0x20 -> CDLLEntityClass (_DLLClass)
        Marshal.WriteIntPtr(entity, 0x78, fakeClass); // entity+0x78 = en_pecClass
        Console.Error.WriteLine($"[dumpnpc] en_pecClass(+0x78) forjado -> fakeClass=0x{fakeClass.ToInt64():x} (+0x20 -> _DLLClass)");

        // SetDefaultProperties = vtable slot 10 (escreve typeId +0x3920, HP, stats da classe). Sem ele a
        // entidade fica fresh (team/typeId=0) e o GetCreateInfo escreve mínimo. Pode tentar precache de modelo.
        IntPtr sdp = Marshal.ReadIntPtr(vtable, 10 * 4);
        try { Marshal.GetDelegateForFunctionPointer<VoidThisDel>(sdp)(entity); Console.Error.WriteLine("[dumpnpc] SetDefaultProperties(slot10) OK"); }
        catch (Exception ex) { Console.Error.WriteLine($"[dumpnpc] SetDefaultProperties lançou (segue): {ex.Message}"); }
        // team de combate (+0x26c) p/ faction inimiga do humano (BLUE=0x14) — req do IsEnemy/HIT×N
        Marshal.WriteByte(entity, 0x26c, 0x14);
        Console.Error.WriteLine($"[dumpnpc] pós-SDP: team(+0x26c)={Marshal.ReadByte(entity,0x26c):x} typeId(+0x3920)={Marshal.ReadByte(entity,0x3920):x}");

        // GetCreateInfo crasha lendo ponteiros de recurso (modelo/sub-objeto) ausentes no headless. Em vez de
        // chamá-lo, DUMPO a memória da entidade POPULADA (pós-SDP): tenho os offsets dos campos (§2.3c) e leio
        // os VALORES reais do Golem direto. Salvo o objeto inteiro (0x3ad8) p/ extrair qualquer campo offline.
        int SIZE = 0x3ad8;
        byte[] mem = new byte[SIZE];
        Marshal.Copy(entity, mem, 0, SIZE);
        try { File.WriteAllBytes(@"C:\temp\golem_entity.bin", mem); } catch { }
        // getter 0x35024700 (RVA 0x24700) lê o valor REAL dos sub-objetos (HP+0x270, +0x3a04)
        IntPtr getter = (IntPtr)(ent.ToInt64() + 0x24700);
        var gdel = Marshal.GetDelegateForFunctionPointer<GetterDel>(getter);
        try {
            int hp = gdel((IntPtr)(entity.ToInt64() + 0x270));
            int f3a04 = gdel((IntPtr)(entity.ToInt64() + 0x3a04));
            Console.Error.WriteLine($"[getter] HP(+0x270)={BitConverter.Int32BitsToSingle(hp)} (0x{hp:x}) | +0x3a04={BitConverter.Int32BitsToSingle(f3a04)} (0x{f3a04:x})");
        } catch (Exception ex) { Console.Error.WriteLine($"[getter] lançou: {ex.Message}"); }
        // imprime as regiões dos campos do §2.3c p/ leitura imediata
        void Region(int off, int len, string lbl)
        {
            byte[] r = new byte[len]; Array.Copy(mem, off, r, 0, len);
            Console.Error.WriteLine($"[fields] +0x{off:x4} {lbl}: {Convert.ToHexString(r)}");
        }
        Region(0x26c, 8, "team/sub");
        Region(0x270, 0x10, "HP-subobj");
        Region(0x380, 0x10, "nome CTString(ptr)");
        Region(0x648, 8, "+0x648");
        Region(0x7d0, 0x10, "+0x7d0..");
        Region(0x84c, 8, "+0x84c");
        Region(0x808, 4, "+0x808"); Region(0x818, 4, "+0x818"); Region(0x888, 8, "+0x888"); Region(0x898, 8, "+0x898");
        Region(0x3920, 8, "typeId +0x3920");
        Region(0x3a00, 0x10, "+0x3a00..3a10");
        Region(0x3a60, 0x10, "+0x3a60..3a70");
        Region(0x3aa0, 0x14, "+0x3aa0..3ab4 (tail)");
        Console.Error.WriteLine($"[dumpnpc] entidade populada (0x{SIZE:x}B) -> C:\\temp\\golem_entity.bin");
        Console.WriteLine($"DUMP class=0x{classId:x} entity-mem -> golem_entity.bin");
        return 0;
    }

    static void SendMove(float x, float y, float z, float yaw)
    {
        // CPlayerAction layout = corpo 0x30a (19B + padding)
        // +00 u16 dt | +02 u16 actState | +04 s16 x | +06 s16 y | +08 s16 z
        // +0a s16 heading | +0c u8 subFrame | +0d s16 ax | +0f s16 ay | +11 s16 az
        IntPtr action = Marshal.AllocHGlobal(64);
        for (int i = 0; i < 64; i++) Marshal.WriteByte(action, i, 0);

        const float scale = 0.01f;
        ushort dt = 100;
        ushort actState = 0x0020;  // move flag (slot 0 — local player)
        short sx = (short)(x / scale);
        short sy = (short)(y / scale);
        short sz = (short)(z / scale);
        short heading = (short)NormalizeDeg(yaw);

        Marshal.WriteInt16(action, 0, (short)dt);
        Marshal.WriteInt16(action, 2, (short)actState);
        Marshal.WriteInt16(action, 4, sx);
        Marshal.WriteInt16(action, 6, sy);
        Marshal.WriteInt16(action, 8, sz);
        Marshal.WriteInt16(action, 10, heading);

        _setAction(_localPlayer, action);
        _sendAction(_localPlayer);
        Marshal.FreeHGlobal(action);
    }

    static int NormalizeDeg(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        if (deg < -180f) deg += 360f;
        return (int)deg;
    }

    // Patch in-memory de 1 byte na .text da engine (VirtualProtect RWX). Para destravar caminhos headless
    // (progress-hook, early-return do Client_Update) cravados no disasm. Diagnóstico no stderr.
    static void PatchByte(long va, byte val, string why)
    {
        IntPtr p = (IntPtr)va;
        if (!VirtualProtect(p, 1, PAGE_EXECUTE_READWRITE, out _))
        { Console.Error.WriteLine($"[capture] VirtualProtect 0x{va:x} falhou"); return; }
        byte before = Marshal.ReadByte(p);
        Marshal.WriteByte(p, val);
        Console.Error.WriteLine($"[capture] patch @0x{va:x}: 0x{before:x2}->0x{val:x2} ({why})");
    }

    static T GetDel<T>(IntPtr h, string name) where T : Delegate
    {
        IntPtr p = GetProcAddress(h, name);
        if (p == IntPtr.Zero) { Console.Error.WriteLine($"   [!] {name} não encontrado"); return null!; }
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    static void SilenceCrtAbort()
    {
        IntPtr h = GetModuleHandleW("msvcr71.dll");
        if (h == IntPtr.Zero) return;
        IntPtr p = GetProcAddress(h, "_set_abort_behavior");
        if (p == IntPtr.Zero) return;
        Marshal.GetDelegateForFunctionPointer<SetAbortBehaviorDel>(p)(0u, 3u);
    }
}
