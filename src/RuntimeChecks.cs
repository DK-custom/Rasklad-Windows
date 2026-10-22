using System;
using System.Diagnostics;
using System.Drawing;

namespace RaskladWindows {
    static class RuntimeChecks {
        internal static void Run() {
            AppBranding.Check();
            UndoLearning memory = new UndoLearning();
            if (memory.Observe("en:helllo", "hello") ||
                memory.Observe("ru:helllo", "hello") ||
                memory.Observe("en:helllo", "help") ||
                !memory.Observe("en:HELLLO", "HELLO"))
                throw new Exception("Learning must require two identical corrections, independently per language.");
            if (!InputAccess.LevelsAllow(0x2000, 0x2000) ||
                !InputAccess.LevelsAllow(0x3000, 0x2000) ||
                InputAccess.LevelsAllow(0x2000, 0x3000) ||
                InputAccess.LevelsAllow(-1, 0x2000) || InputAccess.LevelsAllow(0x2000, -1))
                throw new Exception("Integrity boundary failed.");
            int level = InputAccess.ReadLevel((uint)Process.GetCurrentProcess().Id);
            if (level < 0 || !InputAccess.LevelsAllow(level, level) || InputAccess.ReadLevel(0) >= 0)
                throw new Exception("Native token query failed.");
            if (HintPosition.Visible(new Rectangle(0, 0, 10, 0)))
                throw new Exception("Empty caret rectangles must be ignored.");
            Console.WriteLine("runtime checks: learning and integrity PASS; self integrity=" + level);
        }
    }
}
