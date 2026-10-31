using System;
using System.Diagnostics;
using System.Drawing;

namespace RaskladWindows {
    static class RuntimeChecks {
        internal static void Run() {
            string[] compoundInput = { "кто-нибуть", "well-knwon", "don'tt", "don’tt", "'helllo'", "(well-knwon)!", "helllo-", "WELL-KNWON", "hello—wrld", "foo/bar", "foo--bar", "abc123" };
            string[] compoundSuggestion = { "кто-нибудь", "well-known", "don't", "don't", "hello", "well-known", "hello", "well-known", "hello-world", "foo-bar", "foo-bar", "abc" };
            string[] compoundExpected = { "кто-нибудь", "well-known", "don't", "don’t", "'hello'", "(well-known)!", "hello-", null, null, null, null, null };
            for (int i = 0; i < compoundInput.Length; i++)
                if (TrayApp.TypoCorrection(compoundInput[i], compoundSuggestion[i]) != compoundExpected[i])
                    throw new Exception("Compound spelling/punctuation regression " + i);
            CodeCaret.CheckInterop();
            if (!CodeCaret.CodeRoleForTest("code") || CodeCaret.CodeRoleForTest("textbox") || CodeCaret.CodeRoleForTest("code-editor"))
                throw new Exception("Code semantic role recognition failed");
            if (TrayApp.InsideMarkdownCodeForTest("`ghbdtn rfr ltkf ghbdtn", "`ghbdtn rfr ltkf` ghbdtn "))
                throw new Exception("Closed inline code must release ordinary text");
            if (!TrayApp.ClosedInlineCodeForTest("`ghbdtn rfr ltkf` ghbdtn ") ||
                TrayApp.ClosedInlineCodeForTest("`ghbdtn") || TrayApp.ClosedInlineCodeForTest("ghbdtn") ||
                TrayApp.ClosedInlineCodeForTest("``` `ghbdtn` ") ||
                !TrayApp.InsideMarkdownCodeForTest("``` block", "`ghbdtn` text"))
                throw new Exception("Inline closing must preserve real fenced blocks");
            if (!TrayApp.InsideMarkdownCodeForTest("code", "``` code") ||
                TrayApp.InsideMarkdownCodeForTest("", "``` old") ||
                TrayApp.InsideMarkdownCodeForTest("new message", "``` old") ||
                !TrayApp.InsideMarkdownCodeForTest(null, "``` code") ||
                !TrayApp.InsideMarkdownCodeForTest("```\ncode", "") ||
                TrayApp.InsideMarkdownCodeForTest("```\ncode\n```\ntext", "") ||
                !TrayApp.InsideMarkdownCodeForTest("let x = 1", "```\nlet x = 1 ") ||
                TrayApp.InsideMarkdownCodeForTest("new text", "```\nlet x = 1 ") ||
                !TrayApp.CodeMarkerForTest("monaco-editor", true) || TrayApp.CodeMarkerForTest("code-block", true) ||
                !TrayApp.CodeMarkerForTest("code-block", false) || TrayApp.CodeMarkerForTest("editor", false))
                throw new Exception("Rich code block protection failed");
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
