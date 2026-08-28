using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FM.UI;
using SI.Bindable;
using SI.Core;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.8.2")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        internal static Harmony Harmony;
        internal static bool HookInstalled;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.8.2 SAFE - no click hook at startup; F7 arms and installs it temporarily");
            Harmony = new Harmony("com.schumy12.fm26.fullplayerprobe.harmony");
            HookInstalled = false;
            _behaviour = AddComponent<ProbeBehaviour>();
        }

        internal static bool InstallClickHook()
        {
            if (HookInstalled) return true;
            try
            {
                Harmony.PatchAll(typeof(EmbeddedClickPatch));
                HookInstalled = true;
                Log.LogInfo("[FM26FullProbe] Temporary click hook installed.");
                return true;
            }
            catch (Exception ex)
            {
                Log.LogError("[FM26FullProbe] Could not install temporary click hook: " + ex);
                return false;
            }
        }

        internal static void RemoveClickHook()
        {
            if (!HookInstalled) return;
            try
            {
                Harmony.UnpatchSelf();
                Log.LogInfo("[FM26FullProbe] Temporary click hook removed.");
            }
            catch (Exception ex)
            {
                Log.LogError("[FM26FullProbe] Could not remove temporary click hook: " + ex);
            }
            finally
            {
                HookInstalled = false;
            }
        }

        public override bool Unload()
        {
            RemoveClickHook();
            if (_behaviour != null) UnityEngine.Object.Destroy(_behaviour);
            return base.Unload();
        }
    }

    internal static class CaptureState
    {
        internal static bool Armed;
        internal static int SelectedRow = -1;

        internal static void Capture(EmbeddedDataClickedEvent evt)
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.8.2 CLICK CAPTURE ===");
            Line(sb, "Selected row when armed: " + SelectedRow);

            if (evt == null)
            {
                Line(sb, "ERROR: EmbeddedDataClickedEvent is null");
                Save(sb);
                return;
            }

            try { Line(sb, "ViewKey: " + evt.ViewKey.ToString()); }
            catch (Exception ex) { Line(sb, "ViewKey read failed: " + ex.GetType().Name + " - " + ex.Message); }

            Record record = null;
            try { record = evt.Record; }
            catch (Exception ex) { Line(sb, "Record getter failed: " + ex.GetType().Name + " - " + ex.Message); }

            if (record == null)
            {
                Line(sb, "ERROR: event Record is null");
                Save(sb);
                return;
            }

            int count = -1;
            try { count = record.Count; } catch { }
            Line(sb, "Record.Count=" + count);

            try
            {
                var en = record.GetEnumerator();
                int i = 0;
                while (en.MoveNext())
                {
                    var kv = en.Current;
                    uint key = kv.Key;
                    TypedValue tv = kv.Value;
                    Line(sb, "\nENTRY[" + i + "] key=" + key);
                    DumpTypedValue(tv, sb, "  ");
                    i++;
                    if (i >= 200) { Line(sb, "Stopped at 200 entries."); break; }
                }
            }
            catch (Exception ex)
            {
                Line(sb, "Record enumeration failed: " + ex.GetType().FullName + " - " + ex.Message);
            }

            Save(sb);
        }

        private static void DumpTypedValue(TypedValue tv, StringBuilder sb, string pad)
        {
            if (tv == null) { Line(sb, pad + "TypedValue=<null>"); return; }

            Line(sb, pad + "wrapperType=" + SafeManagedType(tv));
            try { Line(sb, pad + "IsNull=" + tv.IsNull); } catch (Exception ex) { Line(sb, pad + "IsNull=<" + ex.GetType().Name + ">"); }
            try { Line(sb, pad + "DataType=" + (tv.DataType == null ? "<null>" : tv.DataType.FullName)); } catch (Exception ex) { Line(sb, pad + "DataType=<" + ex.GetType().Name + ">"); }
            try { Line(sb, pad + "AsString=" + SafeText(tv.AsString())); } catch (Exception ex) { Line(sb, pad + "AsString=<" + ex.GetType().Name + ": " + ex.Message + ">"); }

            Il2CppSystem.Object value = null;
            try { value = tv.Get(); }
            catch (Exception ex) { Line(sb, pad + "Get()=<" + ex.GetType().Name + ": " + ex.Message + ">"); }

            if (value == null)
            {
                Line(sb, pad + "Value=<null>");
                return;
            }

            Line(sb, pad + "ValueManagedType=" + SafeManagedType(value));
            string il2cppTypeName = "";
            try
            {
                var il2cppType = value.GetIl2CppType();
                il2cppTypeName = il2cppType == null ? "" : (il2cppType.FullName ?? "");
                Line(sb, pad + "ValueIl2CppType=" + (il2cppTypeName.Length == 0 ? "<unknown>" : il2cppTypeName));
            }
            catch (Exception ex)
            {
                Line(sb, pad + "ValueIl2CppType=<" + ex.GetType().Name + ">");
            }

            if (string.Equals(il2cppTypeName, "FM.UI.PersonReference", StringComparison.Ordinal))
            {
                Line(sb, pad + "*** PERSON REFERENCE FOUND ***");
                try { Line(sb, pad + "NativePointer=0x" + value.Pointer.ToString("X")); } catch { }
            }
        }

        private static string SafeManagedType(object o)
        {
            try { return o == null ? "<null>" : (o.GetType().FullName ?? o.GetType().Name); }
            catch { return "<unknown>"; }
        }

        private static string SafeText(string s)
        {
            if (s == null) return "<null>";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
        }

        internal static void Line(StringBuilder sb, string s)
        {
            sb.AppendLine(s);
            try { Plugin.Log.LogInfo("[FM26FullProbe] " + s); } catch { }
        }

        private static void Save(StringBuilder sb)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 26", "FM26FullPlayerProbe");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "clickcapture_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved click capture: " + file);
            }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex); }
        }
    }

    public sealed class ProbeBehaviour : MonoBehaviour
    {
        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame)
                    ArmCapture();
            }
            catch (Exception ex) { Plugin.Log.LogError("[FM26FullProbe] " + ex); }
        }

        private void ArmCapture()
        {
            CaptureState.Armed = false;
            CaptureState.SelectedRow = -1;
            Plugin.RemoveClickHook();

            var root = MainRoot();
            var table = root == null ? null : (Find(root, "playertable") ?? Find(root, "client-object-viewer-table"));
            var view = table == null ? null : Find(table, "View");
            if (view == null)
            {
                Plugin.Log.LogError("[FM26FullProbe] F7: player table/View not found");
                return;
            }

            int count = SafeChildCount(view);
            int selectedCount = 0;
            for (int i = 0; i < count; i++)
            {
                VisualElement row = null;
                try { row = view.ElementAt(i); } catch { }
                if (row == null) continue;
                bool selected = false;
                try { selected = row.ClassListContains("virtualised-list__item--selected"); } catch { }
                if (selected)
                {
                    selectedCount++;
                    CaptureState.SelectedRow = i;
                }
            }

            if (selectedCount != 1)
            {
                Plugin.Log.LogError("[FM26FullProbe] F7: select exactly ONE player first. Selected rows=" + selectedCount);
                CaptureState.SelectedRow = -1;
                return;
            }

            if (!Plugin.InstallClickHook())
            {
                Plugin.Log.LogError("[FM26FullProbe] F7: capture not armed because temporary hook install failed.");
                CaptureState.SelectedRow = -1;
                return;
            }

            CaptureState.Armed = true;
            Plugin.Log.LogInfo("[FM26FullProbe] CAPTURE ARMED for selected row " + CaptureState.SelectedRow + ". Now CLICK THE PLAYER NAME once.");
        }

        private VisualElement MainRoot()
        {
            try
            {
                var docs = FindObjectsOfType<UIDocument>();
                foreach (var doc in docs)
                {
                    if (doc == null) continue;
                    VisualElement r = null; try { r = doc.rootVisualElement; } catch { }
                    if (r != null && SafeName(r) == "PanelManager-container") return r;
                }
            }
            catch { }
            return null;
        }

        private static VisualElement Find(VisualElement root, string name)
        {
            if (root == null) return null;
            if (SafeName(root) == name) return root;
            int n = SafeChildCount(root);
            for (int i = 0; i < n; i++)
            {
                VisualElement c = null; try { c = root.ElementAt(i); } catch { }
                var x = Find(c, name); if (x != null) return x;
            }
            return null;
        }

        private static string SafeName(VisualElement el) { try { return el?.name ?? ""; } catch { return ""; } }
        private static int SafeChildCount(VisualElement el) { try { return el?.childCount ?? 0; } catch { return 0; } }
    }

    [HarmonyPatch(typeof(EmbeddedDataHandler), "OnClickedEvent")]
    internal static class EmbeddedClickPatch
    {
        [HarmonyPrefix]
        private static void Prefix(EmbeddedDataClickedEvent __0)
        {
            if (!CaptureState.Armed) return;

            CaptureState.Armed = false;
            Plugin.RemoveClickHook();

            try
            {
                Plugin.Log.LogInfo("[FM26FullProbe] EmbeddedData click intercepted; capturing Record...");
                CaptureState.Capture(__0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Capture exception: " + ex);
            }
        }
    }
}
