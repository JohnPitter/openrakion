"use strict";

const rakion = Process.getModuleByName("rakion.exe");
const loginCallback = rakion.base.add(0x8a5d0);
const stateCallback = rakion.base.add(0x89590);

function readAnsi(pointer, capacity) {
  const bytes = new Uint8Array(pointer.readByteArray(capacity));
  const end = bytes.indexOf(0);
  return String.fromCharCode(...bytes.slice(0, end < 0 ? bytes.length : end));
}

function readWide(pointer, capacity) {
  const bytes = new Uint8Array(pointer.readByteArray(capacity * 2));
  const chars = [];
  for (let index = 0; index < bytes.length; index += 2) {
    const value = bytes[index] | (bytes[index + 1] << 8);
    if (value === 0) break;
    chars.push(value);
  }
  return String.fromCharCode(...chars);
}

function pointerRange(vector) {
  const begin = vector.add(4).readPointer();
  const end = vector.add(8).readPointer();
  const bytes = end.sub(begin).toInt32();
  return { begin, end, bytes };
}

Interceptor.attach(loginCallback, {
  onEnter(args) {
    this.model = this.context.ecx;
    this.result = args[0].toInt32() & 0xffff;
    this.vector = args[1];
    const range = pointerRange(this.vector);
    const count = range.bytes >= 0 && range.bytes % 0x116 === 0
      ? range.bytes / 0x116
      : -1;
    console.log(JSON.stringify({
      event: "login-callback",
      result: this.result,
      model: this.model.toString(),
      vector: this.vector.toString(),
      begin: range.begin.toString(),
      end: range.end.toString(),
      bytes: range.bytes,
      count,
    }));
    for (let index = 0; index < count; index++) {
      const friend = range.begin.add(index * 0x116);
      console.log(JSON.stringify({
        event: "friend-object",
        index,
        account: readAnsi(friend, 20),
        display: readWide(friend.add(0x16), 20),
        group: readWide(friend.add(0x7e), 20),
        bytes: hexdump(friend, { length: 0x116, header: false, ansi: false }),
      }));
    }
  },
  onLeave() {
    console.log(JSON.stringify({
      event: "login-callback-return",
      result: this.result,
      modelCount: this.model.add(0xb4).readU32(),
      onlineCount: this.model.add(0xe8).readU32(),
      modelBytes: hexdump(this.model, { length: 0x120, header: false, ansi: false }),
    }));
  },
});

Interceptor.attach(stateCallback, {
  onEnter(args) {
    this.model = this.context.ecx;
    this.state = args[0];
    console.log(JSON.stringify({
      event: "state-callback",
      account: readAnsi(this.state, 20),
      online: this.state.add(0x16).readU8() !== 0,
      modelCount: this.model.add(0xb4).readU32(),
      onlineCount: this.model.add(0xe8).readU32(),
    }));
  },
  onLeave() {
    console.log(JSON.stringify({
      event: "state-callback-return",
      modelCount: this.model.add(0xb4).readU32(),
      onlineCount: this.model.add(0xe8).readU32(),
    }));
  },
});

console.log(JSON.stringify({
  module: rakion.name,
  base: rakion.base.toString(),
  loginCallback: loginCallback.toString(),
  stateCallback: stateCallback.toString(),
}));
