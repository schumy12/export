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
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.8.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;
        private Harmony _harmony;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.8 - F7 arms capture; then click selected player's name");
            _harmony = new Harmony("com.schumy12.fm26.fullplayerprobe.harmony");
            _harmony.PatchAll(typeof(EmbeddedClickPatch));
            _behaviour = AddComponent<ProbeBehaviour>();
        }

        public override bool Unload()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
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
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.8 CLICK CAPTURE ===");
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
            try { Line(sb, pad + "ValueIl2CppType=" + (value.GetIl2CppType() == null ? "<null>" : value.GetIl2CppType().FullName)); } catch { }

            try
            {
                var person = value.TryCast<PersonReference>();
                if (person != null)
                {
                    Line(sb, pad + "*** PERSON REFERENCE FOUND ***");
                    try { Line(sb, pad + "UID=" + person.UID); } catch (Exception ex) { Line(sb, pad + "UID=<" + ex.GetType().Name + ">"); }
                    try { Line(sb, pad + "Index=" + person.m_index); } catch { }
                    try { Line(sb, pad + "Type=" + person.Type); } catch { }
                    try { Line(sb, pad + "CombinedIndexAndType=" + person.CombinedIndexAndType); } catch { }
                    try { Line(sb, pad + "Data1=" + person.Data1); } catch { }
                }
            }
            catch (Exception ex)
            {
                Line(sb, pad + "PersonReference cast failed: " + ex.GetType().Name + " - " + ex.Message);
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
                string file = Path.Combine(dir, "clickcapture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
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
