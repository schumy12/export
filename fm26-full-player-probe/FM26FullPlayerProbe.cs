using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FM26FullPlayerProbe
{
    [BepInPlugin("com.schumy12.fm26.fullplayerprobe", "FM26 Full Player Probe", "0.9.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static new BepInEx.Logging.ManualLogSource Log;
        private static ProbeBehaviour _behaviour;

        public override void Load()
        {
            Log = base.Log;
            Log.LogInfo("[FM26FullProbe] Loaded v0.9 SAFE - F7 records selected row; click player normally; F8 probes opened profile");
            _behaviour = AddComponent<ProbeBehaviour>();
        }

        public override bool Unload()
        {
            if (_behaviour != null) UnityEngine.Object.Destroy(_behaviour);
            return base.Unload();
        }
    }

    public sealed class ProbeBehaviour : MonoBehaviour
    {
        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        private int _selectedRow = -1;

        private void Update()
        {
            try
            {
                if (Keyboard.current == null) return;
                if (Keyboard.current.f7Key.wasPressedThisFrame) RecordSelectedRow();
                if (Keyboard.current.f8Key.wasPressedThisFrame) ProbeCurrentScreen();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] " + ex);
            }
        }

        private void RecordSelectedRow()
        {
            _selectedRow = -1;
            var root = MainRoot();
            var table = root == null ? null : (Find(root, "playertable") ?? Find(root, "client-object-viewer-table"));
            var view = table == null ? null : Find(table, "View");
            if (view == null)
            {
                Plugin.Log.LogError("[FM26FullProbe] F7: player table/View not found");
                return;
            }

            int selectedCount = 0;
            int count = SafeChildCount(view);
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
                    _selectedRow = i;
                }
            }

            if (selectedCount != 1)
            {
                _selectedRow = -1;
                Plugin.Log.LogError("[FM26FullProbe] F7: select exactly ONE player. Selected rows=" + selectedCount);
                return;
            }

            Plugin.Log.LogInfo("[FM26FullProbe] F7 OK. Selected row=" + _selectedRow + ". Now click the player's name normally, wait for profile to open, then press F8.");
        }

        private void ProbeCurrentScreen()
        {
            var sb = new StringBuilder();
            Line(sb, "=== FM26 FULL PLAYER PROBE 0.9 PROFILE SCREEN ===");
            Line(sb, "Selected row previously recorded: " + _selectedRow);

            var root = MainRoot();
            if (root == null)
            {
                Line(sb, "ERROR: PanelManager-container not found");
                Save(sb);
                return;
            }

            int interesting = 0;
            Scan(root, sb, "root", 0, ref interesting, 800);
            Line(sb, "Interesting elements logged: " + interesting);
            Save(sb);
        }

        private static void Scan(VisualElement el, StringBuilder sb, string path, int depth, ref int interesting, int max)
        {
            if (el == null || interesting >= max || depth > 18) return;

            string name = SafeName(el);
            string type = SafeType(el);
            string dsType = "";
            string dsPath = "";
            object ds = null;
            object ud = null;
            int bc = 0;

            try { dsType = el.dataSourceTypeString ?? ""; } catch { }
            try { dsPath = el.dataSourcePathString ?? ""; } catch { }
            try { ds = el.dataSource; } catch { }
            try { ud = el.userData; } catch { }
            try { if (el.bindings != null) bc = el.bindings.Count; } catch { }

            bool nameHit = ContainsAny(name, "player", "person", "profile", "uid", "id", "data", "reference");
            bool typeHit = ContainsAny(type, "player", "person", "profile", "reference");
            bool hasData = !string.IsNullOrEmpty(dsType) || !string.IsNullOrEmpty(dsPath) || ds != null || ud != null || bc > 0;

            if (nameHit || typeHit || hasData)
            {
                Line(sb, path + " name='" + name + "' type=" + type + " bindings=" + bc +
                    " dsType='" + dsType + "' dsPath='" + dsPath + "' ds=" + SafeType(ds) + " userData=" + SafeType(ud));
                interesting++;
            }

            int n = SafeChildCount(el);
            for (int i = 0; i < n && interesting < max; i++)
            {
                VisualElement child = null;
                try { child = el.ElementAt(i); } catch { }
                if (child != null) Scan(child, sb, path + "/" + i, depth + 1, ref interesting, max);
            }
        }

        private static bool ContainsAny(string s, params string[] needles)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var n in needles)
                if (s.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private VisualElement MainRoot()
        {
            try
            {
                var docs = FindObjectsOfType<UIDocument>();
                foreach (var doc in docs)
                {
                    if (doc == null) continue;
                    VisualElement r = null;
                    try { r = doc.rootVisualElement; } catch { }
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
                VisualElement c = null;
                try { c = root.ElementAt(i); } catch { }
                var x = Find(c, name);
                if (x != null) return x;
            }
            return null;
        }

        private static string SafeName(VisualElement el) { try { return el?.name ?? ""; } catch { return ""; } }
        private static int SafeChildCount(VisualElement el) { try { return el?.childCount ?? 0; } catch { return 0; } }
        private static string SafeType(object o)
        {
            try { return o == null ? "<null>" : (o.GetType().FullName ?? o.GetType().Name); }
            catch { return "<unknown>"; }
        }

        private static void Line(StringBuilder sb, string s)
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
                string file = Path.Combine(dir, "profileprobe_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                Plugin.Log.LogInfo("[FM26FullProbe] Saved profile probe: " + file);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[FM26FullProbe] Save failed: " + ex);
            }
        }
    }
}
