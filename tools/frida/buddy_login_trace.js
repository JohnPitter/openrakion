"use strict";

const buddy = Process.getModuleByName("Buddy2.dll");
const copySecureString = buddy.base.add(0x1060);
const initializeAes = buddy.base.add(0xb490);
const updateSha0 = buddy.base.add(0xdaf0);

function toHex(pointer, length) {
    const bytes = new Uint8Array(pointer.readByteArray(length));
    return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
}

Interceptor.attach(copySecureString, {
    onEnter(args) {
        this.destination = args[0];
        this.capacity = args[1].toInt32();
        this.caller = this.returnAddress.sub(buddy.base).toUInt32();
    },
    onLeave(result) {
        if ((result.toInt32() & 0xff) === 0 || this.capacity !== 0x15) return;
        if (this.caller !== 0x7822 && this.caller !== 0x783c) return;
        console.log(`buddy-login caller=0x${this.caller.toString(16)} ` +
            `hex=${toHex(this.destination, this.capacity)}`);
    }
});

Interceptor.attach(initializeAes, {
    onEnter() {
        const caller = this.returnAddress.sub(buddy.base).toUInt32();
        if (caller !== 0xae69) return;
        console.log(`buddy-login session-key=${toHex(this.context.ecx, 16)}`);
    }
});

Interceptor.attach(updateSha0, {
    onEnter(args) {
        const caller = this.returnAddress.sub(buddy.base).toUInt32();
        if (caller !== 0xae14 && caller !== 0xae34 && caller !== 0xae4b) return;
        const length = this.context.eax.toUInt32();
        console.log(`buddy-login sha caller=0x${caller.toString(16)} length=${length} ` +
            `hex=${toHex(args[1], length)}`);
    }
});
