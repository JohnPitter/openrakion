'use strict';
// Hook LEVE das criacoes de botao (uitoolkit.dll csButton) p/ LOCALIZAR a tela do GAME ROOM em
// rakion.exe. SO hooka o CONSTRUTOR e o SetText (chamados 1x por botao, na criacao) — NAO hooka
// SetPos/SetSize (chamados a cada frame no redraw -> floodavam o IPC e TRAVAVAM o jogo).
// As posicoes dos botoes saem do decompile estatico (a partir do caller capturado aqui).
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

// csButton::ctor(csComponent* parent, int a, int b, frameStyle) -> this=ECX (novo botao)
hook('??0csButton@@QAE@PAVcsComponent@@HHW4csComponentFrameStyle@@@Z', 'csButton::ctor', function () {
  var self = this.context.ecx;
  var parent = this.context.esp.add(4).readPointer();
  out('CTOR  btn=' + self + ' parent=' + parent + '  caller=' + callerOf(this.returnAddress));
});

// csButton::SetText(char* text) -> this=ECX, text=[esp+4]
hook('?SetText@csButton@@UAEXPBD@Z', 'csButton::SetText', function () {
  var self = this.context.ecx;
  var p = this.context.esp.add(4).readPointer();
  var txt = p.isNull() ? '' : p.readCString();
  out('TEXT  btn=' + self + ' "' + txt + '"  caller=' + callerOf(this.returnAddress));
});

out('[roombtn] hooks LEVES armados (so ctor + SetText). ENTRE numa SALA (game room) — os botoes');
out('[roombtn] Invite/Start/Game setting/Team change serao logados com o caller = a funcao da tela.');
