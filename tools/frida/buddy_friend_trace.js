"use strict";

const buddy = Process.getModuleByName("Buddy2.dll");
const receiveHandler = buddy.base.add(0x7420);
function logCallbackTable(instance) {
  const callbackOwner = instance.add(0x1399c).readPointer();
  if (callbackOwner.isNull()) return;
  const callbackTable = callbackOwner.readPointer();
  const callback = callbackTable.add(0x1c).readPointer();
  console.log(JSON.stringify({
    stateCallback: callback.toString(),
    loginCallback: callbackTable.add(8).readPointer().toString(),
    module: Process.findModuleByAddress(callback)?.name ?? "unknown",
  }));
}

Interceptor.attach(receiveHandler, {
  onEnter(args) {
    const frame = args[0];
    const size = frame.readU16();
    const command = frame.add(2).readU16();
    if (command !== 0x1011 && command !== 0x3fff) return;

    const payloadLength = Math.max(0, size - 4);
    const payload = frame.add(4);
    if (command === 0x3fff) logCallbackTable(this.context.ecx);
    const result = payloadLength >= 2 ? payload.readU16() : -1;
    const friendCount = command === 0x1011 && payloadLength >= 8
      ? payload.add(6).readU16()
      : -1;
    console.log(JSON.stringify({
      command: `0x${command.toString(16)}`,
      size,
      result,
      friendCount,
      payload: hexdump(payload, {
        length: Math.min(payloadLength, 192),
        header: false,
        ansi: false,
      }),
    }));
  },
});

console.log(JSON.stringify({
  module: buddy.name,
  base: buddy.base.toString(),
  receiveHandler: receiveHandler.toString(),
}));
