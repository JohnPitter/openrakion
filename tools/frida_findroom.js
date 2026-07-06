'use strict';
// SEGURO: hooka so a ENTRADA de algumas funcoes-tela (chamadas 1x quando a tela abre) p/ descobrir
// QUAL constroi o game room. NAO hooka SetPos/SetSize (era isso que travava — chamados por frame).
// Quando voce ENTRAR numa sala, a(s) funcao(oes) que dispararem = a tela da sala.

function out(m){ send(m); }
var base = Module.getBaseAddress('rakion.exe') || Module.getBaseAddress('rakion.bin');
out('[findroom] base rakion = ' + base);

// candidatas a tela (RVA a partir de 0x400000): as que passam "Previous" (0xe1) + criam botoes,
// + a lista de telas com varios botoes. A do game room esta aqui.
var CANDS = {
  '0x44f080': 'FUN_0044f080',
  '0x4673b0': 'FUN_004673b0',
  '0x483e20': 'FUN_00483e20',
  '0x462e40': 'FUN_00462e40 (slots?)',
  '0x466440': 'FUN_00466440',
  '0x436780': 'FUN_00436780',
  '0x434890': 'FUN_00434890',
  '0x4698b0': 'FUN_004698b0 (LOBBY ref)'
};
for (var rva in CANDS) {
  (function(rva, name){
    try {
      var addr = base.add(ptr(rva).sub(0x400000));
      Interceptor.attach(addr, { onEnter: function(){ out('>> ENTROU ' + name + ' (' + rva + ')  this=' + this.context.ecx); } });
      out('[hook] ' + name + ' @ ' + addr);
    } catch(e){ out('[ERRO] ' + name + ': ' + e.message); }
  })(rva, CANDS[rva]);
}
out('[findroom] armado. ENTRE numa SALA — vou logar qual funcao-tela dispara.');
