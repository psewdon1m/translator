#@author Codex
#@category Punto
#@keybinding
#@menupath
#@toolbar

import os


def log(msg):
    print(msg)
    out.append(msg)


out = []
prog = currentProgram
fm = prog.getFunctionManager()

log("Program: %s" % prog.getName())
log("Language: %s" % str(prog.getLanguage()))
log("")

interesting_tokens = [
    "hook", "switch", "layout", "caps", "diary", "password",
    "caret", "copy", "paste", "mouse", "escape", "f12"
]

log("== Functions (interesting names) ==")
for fn in fm.getFunctions(True):
    name = fn.getName()
    lower = name.lower()
    if any(t in lower for t in interesting_tokens):
        log("%s @ %s" % (name, fn.getEntryPoint()))

log("")
log("== External Functions (imports) ==")
extmgr = prog.getExternalManager()
for lib in extmgr.getExternalLibraryNames():
    log("[%s]" % lib)
    syms = extmgr.getExternalLibrary(lib)
    if syms is None:
        continue
    for s in syms.getSymbols():
        name = s.getName()
        lower = name.lower()
        if any(t in lower for t in interesting_tokens) or lib.lower() in ["user32.dll", "kernel32.dll", "shlwapi.dll"]:
            log("  %s" % name)

log("")
log("== Exported Symbols ==")
symtab = prog.getSymbolTable()
for sym in symtab.getExternalSymbols():
    pass

# Simpler export pass: functions in EXTERNAL namespace are imports; exports usually in global namespace and referenced by entry points.
for fn in fm.getFunctions(True):
    sym = fn.getSymbol()
    if sym is None:
        continue
    if fn.isThunk():
        continue
    # In this DLL, exports are mostly near image base and Ghidra tags them with no namespace qualifiers.
    if fn.getEntryPoint().getOffset() >= prog.getImageBase().getOffset():
        name = fn.getName()
        if not name.startswith("FUN_"):
            log("%s @ %s" % (name, fn.getEntryPoint()))

log("")

report_path = os.path.join(os.getcwd(), "ghidra_pshook64_report.txt")
with open(report_path, "w") as f:
    f.write("\n".join(out))

log("Report written: %s" % report_path)
