# -*- coding: utf-8 -*-
# Mapeia a transição especial executada pelo callback de criação do primeiro personagem.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


OUTPUT = r"C:\temp\client_character_create_tutorial.txt"
CREATE_CALLBACK = 0x0047C4D0
WORLD_CALLBACK_VTABLE = 0x004DDC08
FIRST_CHARACTER_TRANSITION_SLOT = 0x40
TUTORIAL_CLEAR_THUNK = 0x004BF81A
MESSAGE_BOX = 0x0043D1E0
MESSAGE_BOX_CONSTRUCTOR = 0x0043D0E0
MESSAGE_BOX_OWNER_NOTIFY = 0x00471300
CHARACTER_LIST_REFRESH = 0x00468540
CHARACTER_UI_MESSAGE = 0x00468A10
CHARACTER_UI_MESSAGE_ALT = 0x0046A0F0
MESSAGE_BOX_LIST = 0x0046FDE0
MESSAGE_BOX_PROCESS = 0x00471470
MESSAGE_BOX_REMOVE = 0x00471580
MESSAGE_BOX_DESTROY = 0x0043D0A0
MESSAGE_BOX_UNLINK = 0x0043D1A0
MAIN_UI_MESSAGE_LOOP = 0x0044C030
CHARACTER_CREATION_CONSTRUCTOR = 0x00444F60
CHARACTER_CREATION_CLOSE = 0x004133F0
CHARACTER_CREATION_DESTRUCTOR = 0x004448F0


def decompile(function, decompiler, monitor):
    result = decompiler.decompileFunction(function, 300, monitor)
    if result and result.decompileCompleted():
        return result.getDecompiledFunction().getC()
    return "(falha na decompilacao)"


def ascii_text(value):
    return value.encode("ascii", "replace").decode("ascii")


manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
listing = currentProgram.getListing()
symbols = currentProgram.getSymbolTable()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

slot_address = toAddr(WORLD_CALLBACK_VTABLE + FIRST_CHARACTER_TRANSITION_SLOT)
transition_address = getInt(slot_address) & 0xffffffff
targets = (
    (CREATE_CALLBACK, "callback S->C 0x12"),
    (transition_address, "slot virtual +0x40 do callback World"),
    (TUTORIAL_CLEAR_THUNK, "thunk SendCharacterTutorialClear"),
    (MESSAGE_BOX, "construtor/dispatcher de message box"),
    (MESSAGE_BOX_CONSTRUCTOR, "construtor do objeto de message box"),
    (MESSAGE_BOX_OWNER_NOTIFY, "notificacao do message box ao owner da UI"),
    (CHARACTER_LIST_REFRESH, "reconstrucao da lista de personagens"),
    (CHARACTER_UI_MESSAGE, "handler proximo da UI de personagem"),
    (CHARACTER_UI_MESSAGE_ALT, "handler alternativo da UI de personagem"),
    (MESSAGE_BOX_LIST, "acesso a lista de message boxes da UI"),
    (MESSAGE_BOX_PROCESS, "processamento da fila de message boxes"),
    (MESSAGE_BOX_REMOVE, "remocao da fila de message boxes"),
    (MESSAGE_BOX_DESTROY, "destrutor de message box"),
    (MESSAGE_BOX_UNLINK, "desvinculo de message box"),
    (MAIN_UI_MESSAGE_LOOP, "loop principal de resposta dos message boxes"),
    (CHARACTER_CREATION_CONSTRUCTOR, "construtor da janela de criacao"),
    (CHARACTER_CREATION_CLOSE, "fechamento virtual +0x0C da criacao"),
    (CHARACTER_CREATION_DESTRUCTOR, "destrutor interno da criacao"),
)

with open(OUTPUT, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    output.write("vtable=%08x slot=%08x target=%08x\n" % (
        WORLD_CALLBACK_VTABLE, FIRST_CHARACTER_TRANSITION_SLOT, transition_address))
    for address, purpose in targets:
        function = manager.getFunctionContaining(toAddr(address))
        if not function:
            disassemble(toAddr(address))
            function = createFunction(toAddr(address), None)
        output.write("\n===== %s @ %08x - %s =====\n" % (
            function.getName() if function else "funcao ausente", address, purpose))
        if function:
            output.write(ascii_text(decompile(function, decompiler, monitor)))
            output.write("\n--- callers/references ---\n")
            seen = set()
            for reference in references.getReferencesTo(function.getEntryPoint()):
                caller = manager.getFunctionContaining(reference.getFromAddress())
                if caller and caller.getEntryPoint() not in seen:
                    seen.add(caller.getEntryPoint())
                    output.write("%s @ %s via %s\n" % (
                        caller.getName(), caller.getEntryPoint(), reference.getFromAddress()))
    output.write("\n===== instrucoes do callback 0x12 =====\n")
    instruction = listing.getInstructionAt(toAddr(CREATE_CALLBACK))
    while instruction and instruction.getAddress().getOffset() < CREATE_CALLBACK + 0x220:
        output.write("%s  %s\n" % (instruction.getAddress(), instruction))
        instruction = instruction.getNext()
    output.write("\n===== inicio da reconstrucao da lista =====\n")
    instruction = listing.getInstructionAt(toAddr(CHARACTER_LIST_REFRESH))
    while instruction and instruction.getAddress().getOffset() < CHARACTER_LIST_REFRESH + 0x100:
        output.write("%s  %s\n" % (instruction.getAddress(), instruction))
        instruction = instruction.getNext()
    output.write("\n===== callers de SendCharacterTutorialClear =====\n")
    emitted = set()
    for symbol in symbols.getAllSymbols(True):
        if "SendCharacterTutorialClear" not in symbol.getName(True):
            continue
        output.write("symbol %s @ %s\n" % (symbol.getName(True), symbol.getAddress()))
        for reference in references.getReferencesTo(symbol.getAddress()):
            caller = manager.getFunctionContaining(reference.getFromAddress())
            if not caller or caller.getEntryPoint() in emitted:
                continue
            emitted.add(caller.getEntryPoint())
            output.write("\n--- %s @ %s via %s ---\n" % (
                caller.getName(), caller.getEntryPoint(), reference.getFromAddress()))
            output.write(ascii_text(decompile(caller, decompiler, monitor)))
    output.write("\n===== chamadas indiretas ao import tutorial =====\n")
    tutorial_addresses = ("004d006c", "004bf81a")
    emitted = set()
    for instruction in listing.getInstructions(True):
        if not any(address in str(instruction).lower() for address in tutorial_addresses):
            continue
        caller = manager.getFunctionContaining(instruction.getAddress())
        output.write("%s  %s\n" % (instruction.getAddress(), instruction))
        if caller and caller.getEntryPoint() not in emitted:
            emitted.add(caller.getEntryPoint())
            output.write(ascii_text(decompile(caller, decompiler, monitor)))
    output.write("\n===== funcoes que acessam o elo +0x474 do message box =====\n")
    emitted = set()
    for instruction in listing.getInstructions(True):
        if "0x474" not in str(instruction).lower():
            continue
        function = manager.getFunctionContaining(instruction.getAddress())
        if not function or function.getEntryPoint() in emitted:
            continue
        emitted.add(function.getEntryPoint())
        output.write("%s @ %s via %s\n" % (
            function.getName(), function.getEntryPoint(), instruction.getAddress()))
    output.write("\n===== comparacoes de eventos relevantes =====\n")
    event_values = (0x11a, 0x11b, 0x10c)
    for instruction in listing.getInstructions(True):
        scalars = []
        for operand_index in range(instruction.getNumOperands()):
            scalar = instruction.getScalar(operand_index)
            if scalar:
                scalars.append(scalar.getUnsignedValue())
        if not any(value in scalars for value in event_values):
            continue
        function = manager.getFunctionContaining(instruction.getAddress())
        if not function or function.getEntryPoint().getOffset() not in (
                MAIN_UI_MESSAGE_LOOP, CHARACTER_UI_MESSAGE):
            continue
        cursor = instruction
        for unused in range(8):
            previous = cursor.getPrevious()
            if not previous:
                break
            cursor = previous
        output.write("\n--- contexto %s em %s ---\n" % (
            instruction.getAddress(), function.getName()))
        for unused in range(24):
            if not cursor:
                break
            output.write("%s  %s\n" % (cursor.getAddress(), cursor))
            cursor = cursor.getNext()
    for address, label, length in (
            (MAIN_UI_MESSAGE_LOOP, "dispatch inicial dos dialogs", 0x300),
            (CHARACTER_UI_MESSAGE, "dispatch da tela de personagens", 0x220)):
        output.write("\n===== %s =====\n" % label)
        instruction = listing.getInstructionAt(toAddr(address))
        while instruction and instruction.getAddress().getOffset() < address + length:
            output.write("%s  %s\n" % (instruction.getAddress(), instruction))
            instruction = instruction.getNext()
    output.write("\n===== epilogo do handler de personagens =====\n")
    instruction = listing.getInstructionAfter(toAddr(0x00468DCF))
    while instruction and instruction.getAddress().getOffset() < 0x00468E30:
        output.write("%s  %s\n" % (instruction.getAddress(), instruction))
        instruction = instruction.getNext()

print("Fluxo create/tutorial extraido: %s" % OUTPUT)
