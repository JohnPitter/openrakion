# -*- coding: utf-8 -*-
# Extrai contratos, produtores e consumidores do evento de Natal em entitiesmp.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


KEYWORDS = (
    "christmas",
    "eventitem",
    "santa",
    "eventmessage",
    "renderevent",
)
ASSET_KEYWORDS = (
    "christmas",
    "santa",
    "eventmessage.txt",
)
TARGETS = (
    0x35130590,  # EChristmasDestroy
    0x351305F0,  # EChristmasNoticeMessage
    0x35134E70,  # EChristmasSetting
    0x35134EF0,  # EEventItemSetting
    0x35134F70,  # EGetEventItem
    0x35130690,  # EDestroyEventItem
    0x35040800,  # ESpawnChristmasBox
    0x350407A0,  # ESpawnChristmasBox::MakeCopy
    0x350405E0,  # EChristmasBoxItemTouch
    0x35040620,  # EChristmasBoxReceive
    0x3508A3F0,  # ESpawnEventItem
    0x3508A4F0,  # ESpawnEventItem::MakeCopy
    0x35163420,  # handler ativo de CPlayer
    0x35166EF0,  # EChristmasSetting copy ctor
    0x35166FA0,  # EEventItemSetting copy ctor
    0x35167040,  # EGetEventItem copy ctor
    0x351670D0,  # EDestroyEventItem copy ctor
    0x35041140,  # ESpawnChristmasBox copy ctor
    0x35041230,  # EChristmasBoxItemTouch copy ctor
    0x350412D0,  # EChristmasBoxReceive copy ctor
    0x3508ACD0,  # ESpawnEventItem copy ctor
    0x35131A90,  # CPlayer::EventMessage
    0x35136D30,  # CPlayer::RenderEventMessage
    0x35132270,  # CPlayer::RenderEventTime
    0x35135480,  # CPlayer::SetChristmasBox
    0x35135520,  # CPlayer::SetEventItem
    0x35040CA0,  # CChristmasBoxItem::SetDefaultProperties
    0x3508A430,  # CEventItem::SetDefaultProperties
    0x351A20E0,  # CSanta::SetDefaultProperties
)
EVENT_IDS = (
    0x0191001D,
    0x0191001F,
    0x01910020,
    0x01910021,
    0x01910022,
    0x01910023,
    0x52B30000,
    0x52B30001,
    0x52B30002,
    0x52B50000,
)
OUTPUT = r"C:\temp\client_christmas_events.txt"

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}
asset_matches = []


def add_function(function):
    if function is not None:
        functions[str(function.getEntryPoint())] = function


iterator = manager.getFunctions(True)
while iterator.hasNext() and not monitor.isCancelled():
    function = iterator.next()
    name = function.getName().lower()
    if any(keyword in name for keyword in KEYWORDS):
        add_function(function)

for offset in TARGETS:
    address = toAddr(offset)
    function = manager.getFunctionAt(address)
    if function is None:
        function = manager.getFunctionContaining(address)
    if function is None:
        disassemble(address)
        function = createFunction(address, None)
    add_function(function)

instructions = currentProgram.getListing().getInstructions(True)
while instructions.hasNext() and not monitor.isCancelled():
    instruction = instructions.next()
    matched = False
    for operand_index in range(instruction.getNumOperands()):
        for value in instruction.getOpObjects(operand_index):
            if hasattr(value, "getValue") and value.getValue() in EVENT_IDS:
                matched = True
                break
        if matched:
            break
    if matched:
        add_function(manager.getFunctionContaining(instruction.getAddress()))

for data in currentProgram.getListing().getDefinedData(True):
    if not data.hasStringValue():
        continue
    value = str(data.getValue())
    if not any(keyword in value.lower() for keyword in ASSET_KEYWORDS):
        continue
    refs = list(references.getReferencesTo(data.getAddress()))
    asset_matches.append((data.getAddress(), value, refs))
    for reference in refs:
        add_function(manager.getFunctionContaining(reference.getFromAddress()))

for function in list(functions.values()):
    for reference in references.getReferencesTo(function.getEntryPoint()):
        add_function(manager.getFunctionContaining(reference.getFromAddress()))

with open(OUTPUT, "w") as output:
    output.write("functions=%d assets=%d\n" % (len(functions), len(asset_matches)))
    for address, value, refs in asset_matches:
        output.write("asset %s %s refs=%d\n" % (
            address, value.encode("ascii", "replace").decode("ascii"), len(refs)))
        for reference in refs:
            output.write("  ref=%s\n" % reference.getFromAddress())
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (
            function.getName(), function.getEntryPoint()))
        result = decompiler.decompileFunction(function, 240, monitor)
        if result and result.getDecompiledFunction():
            output.write(result.getDecompiledFunction().getC())

print("eventos natalinos extraidos em " + OUTPUT)
