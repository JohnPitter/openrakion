'use strict';
// Hook das criacoes de botao da UI (uitoolkit.dll csButton) p/ LOCALIZAR a tela do GAME ROOM em
// gamemp.dll. Ao entrar numa sala, loga cada botao: texto, posicao e o CALLER (modulo+offset = a
// funcao que cria a tela). Com isso eu cravo onde injetar o botao "Adicionar Bot".
// x86 __thiscall: this=ECX; args na pilha a partir de [esp+4]; retaddr = this.returnAddress.

function out(m) { send(m); }

function callerOf(ret) {
  try {
    var m = Process.findModuleByAddress(ret);
    if (!m) return ret.toString();
    return m.name + '+0x' + ret.sub(m.base).toString(16);
  } catch (e) { return '?'; }
}

function hook(mangled, label, onEnter) {
  try {
    var addr = Module.getExportByName('uitoolkit.dll', mangled);
    Interceptor.attach(addr, { onEnter: onEnter });
    out('[hook] ' + label + ' @ ' + addr);
  } catch (e) { out('[ERRO] ' + label + ': ' + e.message); }
}

// csButton::SetText(char* text)  -> this=ECX, text=[esp+4]
hook('?SetText@csButton@@UAEXPBD@Z', 'csButton::SetText', function () {
  var self = this.context.ecx;
  var p = this.context.esp.add(4).readPointer();
  var txt = p.isNull() ? '' : p.readCString();
  out('SetText  btn=' + self + ' text="' + txt + '"  caller=' + callerOf(this.returnAddress));
});

// csButton::ctor(csComponent* parent, int a, int b, frameStyle) -> this=ECX, parent=[esp+4], a=[esp+8], b=[esp+12]
hook('??0csButton@@QAE@PAVcsComponent@@HHW4csComponentFrameStyle@@@Z', 'csButton::ctor', function () {
  var self = this.context.ecx;
  var parent = this.context.esp.add(4).readPointer();
  var a = this.context.esp.add(8).readS32();
  var b = this.context.esp.add(12).readS32();
  out('CTOR     btn=' + self + ' parent=' + parent + ' id?=' + a + '/' + b + '  caller=' + callerOf(this.returnAddress));
});

// csComponent::SetPos(long x, long y) -> this=ECX, x=[esp+4], y=[esp+8]
hook('?SetPos@csComponent@@QAEXJJ@Z', 'csComponent::SetPos', function () {
  var self = this.context.ecx;
  var x = this.context.esp.add(4).readS32();
  var y = this.context.esp.add(8).readS32();
  out('SetPos   btn=' + self + ' x=' + x + ' y=' + y + '  caller=' + callerOf(this.returnAddress));
});

// csComponent::SetSize(long w, long h)
hook('?SetSize@csComponent@@QAEXJJ@Z', 'csComponent::SetSize', function () {
  var self = this.context.ecx;
  var w = this.context.esp.add(4).readS32();
  var h = this.context.esp.add(8).readS32();
  out('SetSize  btn=' + self + ' w=' + w + ' h=' + h + '  caller=' + callerOf(this.returnAddress));
});

out('[roombtn] hooks armados. ENTRE numa SALA (game room) — os 6 botoes (Previous/Messenger/Invite/');
out('[roombtn] Game setting/Team change/Start) serao logados com o caller = a funcao da tela a patchar.');
