//@author Codex
//@category Punto

import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Program;
import ghidra.program.model.symbol.ExternalLocation;
import ghidra.program.model.symbol.ExternalLocationIterator;
import ghidra.program.model.symbol.ExternalManager;
import ghidra.util.exception.CancelledException;

import java.io.File;
import java.io.PrintWriter;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Set;

public class PuntoHookReport extends GhidraScript {

    private static final Set<String> TOKENS = new HashSet<>(Arrays.asList(
        "hook", "switch", "layout", "caps", "diary", "password",
        "caret", "copy", "paste", "mouse", "escape", "f12"
    ));

    @Override
    protected void run() throws Exception {
        Program p = currentProgram;
        if (p == null) {
            println("No current program");
            return;
        }

        File outFile = new File(System.getProperty("user.dir"), "ghidra_pshook64_report.txt");
        try (PrintWriter out = new PrintWriter(outFile, "UTF-8")) {
            writeLine(out, "Program: " + p.getName());
            writeLine(out, "Language: " + p.getLanguage());
            writeLine(out, "");

            writeLine(out, "== Functions (interesting names) ==");
            FunctionIterator it = p.getFunctionManager().getFunctions(true);
            while (it.hasNext()) {
                Function fn = it.next();
                String name = fn.getName();
                if (matches(name)) {
                    writeLine(out, name + " @ " + fn.getEntryPoint());
                }
            }

            writeLine(out, "");
            writeLine(out, "== Imports (interesting) ==");
            ExternalManager em = p.getExternalManager();
            for (String lib : em.getExternalLibraryNames()) {
                writeLine(out, "[" + lib + "]");
                ExternalLocationIterator locIt = em.getExternalLocations(lib);
                while (locIt.hasNext()) {
                    ExternalLocation loc = locIt.next();
                    if (loc == null || loc.getLabel() == null) continue;
                    String n = loc.getLabel();
                    if (matches(n) || lib.toLowerCase().contains("user32") || lib.toLowerCase().contains("kernel32")) {
                        writeLine(out, "  " + n);
                    }
                }
            }

            writeLine(out, "");
            writeLine(out, "== Candidate Exports ==");
            it = p.getFunctionManager().getFunctions(true);
            while (it.hasNext()) {
                Function fn = it.next();
                String name = fn.getName();
                if (!name.startsWith("FUN_") && !fn.isThunk()) {
                    writeLine(out, name + " @ " + fn.getEntryPoint());
                }
            }
        }

        println("Report written: " + outFile.getAbsolutePath());
    }

    private boolean matches(String name) {
        String lower = name.toLowerCase();
        for (String t : TOKENS) {
            if (lower.contains(t)) return true;
        }
        return false;
    }

    private void writeLine(PrintWriter out, String s) {
        println(s);
        out.println(s);
    }
}
