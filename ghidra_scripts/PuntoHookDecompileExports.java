//@author Codex
//@category Punto

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileOptions;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Program;

import java.io.File;
import java.io.PrintWriter;
import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.Set;

public class PuntoHookDecompileExports extends GhidraScript {
    private Function resolveFunction(String spec) {
        if (spec.startsWith("0x")) {
            try {
                Address a = toAddr(spec);
                Function fn = getFunctionAt(a);
                if (fn == null) {
                    fn = getFunctionContaining(a);
                }
                return fn;
            } catch (Exception e) {
                println("Bad address spec: " + spec + " (" + e.getMessage() + ")");
                return null;
            }
        }
        return getGlobalFunctions(spec).isEmpty() ? null : getGlobalFunctions(spec).get(0);
    }

    @Override
    protected void run() throws Exception {
        Program p = currentProgram;
        if (p == null) {
            println("No current program");
            return;
        }

        Set<String> targets = new LinkedHashSet<>(Arrays.asList(
            "SetHook",
            "SwitchLayout",
            "ReloadLowLevelHooks",
            "SetHookTimeout",
            "IsPasswordField",
            "GetCaretRect",
            "EnableCapsLock",
            "GetCapsLockState",
            // internals referenced by exports
            "0x1800036a0", // SetHook backend
            "0x180003730", // EnableCapsLock backend
            "0x1800033a0", // focus fallback helper
            "0x180001d20", // low-level hook reload backend
            "0x1800020d0", // hook callback (likely keyboard ll)
            "0x180001eb0", // hook callback (likely mouse ll)
            "0x180003420", // keyboard hook gate/filter by foreground window
            "0x180002dd0", // keyboard event processor (may consume)
            "0x180002ee0", // event fast-path / classifier
            "0x1800028a0", // main key processing/state machine
            "0x1800032c0", // process gate helper
            "0x1800043a0", // message dispatch helper to host window/thread
            "0x1800034a0", // focused target resolver
            "0x180002780", // reset/flush input buffer helper
            "0x1800027c0", // translate current key into char/action
            "0x1800026a0", // append/store key char in buffer/state
            "0x180002200"  // synthetic key helper (Win key combos)
        ));

        DecompInterface ifc = new DecompInterface();
        DecompileOptions opts = new DecompileOptions();
        ifc.setOptions(opts);
        ifc.toggleCCode(true);
        ifc.toggleSyntaxTree(false);
        if (!ifc.openProgram(p)) {
            println("Failed to open decompiler");
            return;
        }

        File outFile = new File(System.getProperty("user.dir"), "ghidra_pshook64_decomp.txt");
        try (PrintWriter out = new PrintWriter(outFile, "UTF-8")) {
            for (String name : targets) {
                Function fn = resolveFunction(name);
                if (fn == null) {
                    println("Missing function: " + name);
                    out.println("==== " + name + " ====");
                    out.println("<missing>");
                    out.println();
                    continue;
                }
                println("Decompiling: " + name + " -> " + fn.getName() + " @ " + fn.getEntryPoint());
                out.println("==== " + name + " -> " + fn.getName() + " @ " + fn.getEntryPoint() + " ====");
                DecompileResults res = ifc.decompileFunction(fn, 60, monitor);
                if (!res.decompileCompleted()) {
                    out.println("<decompile failed> " + res.getErrorMessage());
                } else {
                    out.println(res.getDecompiledFunction().getC());
                }
                out.println();
                out.println();
            }
        } finally {
            ifc.dispose();
        }

        println("Decompile written: " + outFile.getAbsolutePath());
    }
}
