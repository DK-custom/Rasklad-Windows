using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace RaskladWindows {
    // .NET Framework's managed UIA wrapper does not expose AriaRole (30101).
    // Read only semantic properties through the system UIA COM interfaces.
    static class CodeCaret {
        [StructLayout(LayoutKind.Sequential)] struct ScreenPoint { public int X, Y; }
        [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Automation {
            bool CompareElements(Element a, Element b);
            bool CompareRuntimeIds([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] a,
                [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] b);
            Element GetRootElement();
            Element ElementFromHandle(IntPtr window);
            Element ElementFromPoint(ScreenPoint point);
            Element GetFocusedElement();
            Element GetRootElementBuildCache(IntPtr request);
            Element ElementFromHandleBuildCache(IntPtr window, IntPtr request);
            Element ElementFromPointBuildCache(ScreenPoint point, IntPtr request);
            Element GetFocusedElementBuildCache(IntPtr request);
            Walker CreateTreeWalker(IntPtr condition);
            Walker ControlViewWalker { get; }
            Walker ContentViewWalker { get; }
            Walker RawViewWalker { get; }
            IntPtr RawViewCondition { get; }
            IntPtr ControlViewCondition { get; }
            IntPtr ContentViewCondition { get; }
            IntPtr CreateCacheRequest();
            IntPtr CreateTrueCondition();
            IntPtr CreateFalseCondition();
            IntPtr CreatePropertyCondition(int id, [MarshalAs(UnmanagedType.Struct)] object value);
        }
        [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Element {
            void SetFocus();
            [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] GetRuntimeId();
            Element FindFirst(int scope, IntPtr condition);
            IntPtr FindAll(int scope, IntPtr condition);
            Element FindFirstBuildCache(int scope, IntPtr condition, IntPtr request);
            IntPtr FindAllBuildCache(int scope, IntPtr condition, IntPtr request);
            Element BuildUpdatedCache(IntPtr request);
            [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int id);
        }
        [ComImport, Guid("4042C624-389C-4AFC-A630-9DF854A541FC"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Walker { Element GetParentElement(Element element); }

        private static Automation Create() {
            return (Automation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")));
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        private static bool Same(int[] a, int[] b) {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        internal static bool CodeRoleForTest(string role) {
            foreach (string token in (role ?? "").Split(' '))
                if (token.Equals("code", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        internal static bool AtPoint(Point point, AutomationElement field, out bool block) {
            block = false; Automation automation = null; Walker walker = null; Element node = null;
            try {
                int[] boundary = field.GetRuntimeId();
                automation = Create(); walker = automation.RawViewWalker;
                node = automation.ElementFromPoint(new ScreenPoint { X = point.X, Y = point.Y });
                return WalkCode(ref node, walker, boundary, out block);
            } catch (Exception) { DiagnosticLog.Write("context.code_role_unavailable"); }
            finally { Release(node); Release(walker); Release(automation); }
            return false;
        }
        private static bool WalkCode(ref Element node, Walker walker, int[] boundary, out bool block) {
            bool code = false, codeBlock = false; block = false;
            for (int depth = 0; node != null && depth < 20; depth++) {
                bool atBoundary = Same(node.GetRuntimeId(), boundary);
                object password = node.GetCurrentPropertyValue(30019);
                if (password is bool && (bool)password) return false;
                string role = node.GetCurrentPropertyValue(30101) as string;
                string marker = ((node.GetCurrentPropertyValue(30012) as string) + " " +
                    (node.GetCurrentPropertyValue(30011) as string)).Trim().ToLowerInvariant();
                bool explicitBlock = marker.Contains("code-block") || marker.Contains("code_block") || marker.Contains("codeblock") || marker == "pre";
                code |= CodeRoleForTest(role) || explicitBlock || !atBoundary && TrayApp.CodeMarkerForTest(marker, false);
                codeBlock |= explicitBlock;
                if (atBoundary) { block = codeBlock; return code; }
                Element parent = walker.GetParentElement(node); Release(node); node = parent;
            }
            return false;
        }
        internal static bool AtElement(AutomationElement atCaret, AutomationElement field, out bool block) {
            block = false; Automation automation = null; Walker walker = null; Element nativeField = null, node = null;
            IntPtr condition = IntPtr.Zero;
            try {
                int[] boundary = field.GetRuntimeId(), target = atCaret.GetRuntimeId();
                automation = Create(); nativeField = automation.GetFocusedElement();
                if (!Same(nativeField.GetRuntimeId(), boundary)) return false;
                walker = automation.RawViewWalker;
                if (Same(boundary, target)) { node = nativeField; nativeField = null; }
                else {
                    condition = automation.CreatePropertyCondition(30000, target);
                    node = nativeField.FindFirst(7, condition); // TreeScope_Subtree, bounded to this field.
                }
                return WalkCode(ref node, walker, boundary, out block);
            } catch (Exception) { DiagnosticLog.Write("context.code_element_unavailable"); }
            finally { Release(node); Release(nativeField); Release(walker); Release(automation); if (condition != IntPtr.Zero) Marshal.Release(condition); }
            return false;
        }
        internal static void CheckInterop() {
            Automation automation = null; Element root = null, found = null; Walker walker = null; IntPtr condition = IntPtr.Zero;
            try {
                automation = Create(); root = automation.GetRootElement(); walker = automation.RawViewWalker;
                if (root.GetRuntimeId() == null || !(root.GetCurrentPropertyValue(30003) is int) || walker == null)
                    throw new Exception("Native UIA property access failed");
                condition = automation.CreatePropertyCondition(30000, root.GetRuntimeId());
                found = root.FindFirst(1, condition);
                if (found == null || !Same(found.GetRuntimeId(), root.GetRuntimeId())) throw new Exception("Native UIA identity lookup failed");
            } finally { Release(found); Release(root); Release(walker); Release(automation); if (condition != IntPtr.Zero) Marshal.Release(condition); }
        }
    }
}
