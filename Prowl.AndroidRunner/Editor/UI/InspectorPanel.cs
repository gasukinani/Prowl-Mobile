using System;
using System.Numerics;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Prowl.AndroidRunner.Editor.Scripting;
using Prowl.Runtime;

namespace Prowl.AndroidRunner.Editor.UI
{
    public class InspectorPanel : LinearLayout
    {
        private readonly Activity _activity;
        private readonly LinearLayout _body;
        public event Action<Vector3>? OnFocusRequested;
        public event Action? OnComponentUpdated;

        public InspectorPanel(Activity activity) : base(activity)
        {
            _activity = activity;
            Orientation = Orientation.Vertical;
            LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

            AddView(EditorTheme.CreateHeaderBar(activity, "❖ Inspector ✕"));

            var scroll = new ScrollView(activity) { LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _body = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            _body.SetPadding(
                EditorTheme.DpToPx(activity, 10),
                EditorTheme.DpToPx(activity, 6),
                EditorTheme.DpToPx(activity, 10),
                EditorTheme.DpToPx(activity, 6)
            );
            scroll.AddView(_body);
            AddView(scroll);
        }

        public void Rebuild(ProwlNode? selectedNode)
        {
            _body.RemoveAllViews();

            if (selectedNode == null)
            {
                var empty = new TextView(_activity) { Text = "No GameObject Selected", TextSize = 11 };
                empty.SetTextColor(EditorTheme.TextMuted);
                _body.AddView(empty);
                return;
            }

            // Top Header: Checkbox + Name + Dynamic Dropdown
            var headerRow = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
            var chk = new CheckBox(_activity) { Checked = selectedNode.IsActive };
            chk.CheckedChange += (s, e) => { selectedNode.IsActive = e.IsChecked; OnComponentUpdated?.Invoke(); };
            headerRow.AddView(chk);

            var editName = new EditText(_activity) { Text = selectedNode.Name, TextSize = 12 };
            editName.SetTextColor(Color.White);
            editName.SetBackgroundColor(EditorTheme.BgDark);
            editName.LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            editName.TextChanged += (s, e) => { selectedNode.Name = editName.Text ?? ""; };
            headerRow.AddView(editName);

            var dynBadge = new TextView(_activity) { Text = "Dynamic ▼", TextSize = 10 };
            dynBadge.SetTextColor(EditorTheme.TextMuted);
            dynBadge.SetPadding(EditorTheme.DpToPx(_activity, 4), 0, 0, 0);
            headerRow.AddView(dynBadge);
            _body.AddView(headerRow);

            var tagLayer = new TextView(_activity) { Text = $"Tag: {selectedNode.Tag}       Layer: {selectedNode.Layer}", TextSize = 10 };
            tagLayer.SetTextColor(EditorTheme.TextMuted);
            tagLayer.SetPadding(0, EditorTheme.DpToPx(_activity, 2), 0, EditorTheme.DpToPx(_activity, 6));
            _body.AddView(tagLayer);

            // Transform Drawer
            var tfHdr = new TextView(_activity) { Text = "▼ ❖ Transform", TextSize = 11 };
            tfHdr.SetTextColor(EditorTheme.AccentBlue);
            _body.AddView(tfHdr);

            _body.AddView(CreateVectorRow("Position", selectedNode.Transform.Position, v => { selectedNode.Transform.Position = v; OnComponentUpdated?.Invoke(); }));
            _body.AddView(CreateVectorRow("Rotation", selectedNode.Transform.Rotation, v => { selectedNode.Transform.Rotation = v; OnComponentUpdated?.Invoke(); }));
            _body.AddView(CreateVectorRow("Scale", selectedNode.Transform.Scale, v => { selectedNode.Transform.Scale = v; OnComponentUpdated?.Invoke(); }));

            // Other Drawers
            foreach (var comp in selectedNode.Components)
            {
                if (comp is MeshRendererComponent mesh)
                {
                    _body.AddView(EditorTheme.CreateDivider(_activity, true, 1));
                    var mrHdr = new TextView(_activity) { Text = "▼ ❖ MeshRenderer", TextSize = 11 };
                    mrHdr.SetTextColor(EditorTheme.AccentBlue);
                    _body.AddView(mrHdr);

                    var txtMesh = new TextView(_activity) { Text = $"Mesh: 📦 {mesh.Shape} (Mesh)\nMaterials: 🎨 1 elements >", TextSize = 10 };
                    txtMesh.SetTextColor(EditorTheme.TextMuted);
                    _body.AddView(txtMesh);
                }
                else if (comp is LightComponent light)
                {
                    _body.AddView(EditorTheme.CreateDivider(_activity, true, 1));
                    var lHdr = new TextView(_activity) { Text = "▼ 💡 Light", TextSize = 11 };
                    lHdr.SetTextColor(EditorTheme.AccentYellow);
                    _body.AddView(lHdr);

                    var txtL = new TextView(_activity) { Text = $"Type: {light.Type}\nIntensity: {light.Intensity}", TextSize = 10 };
                    txtL.SetTextColor(EditorTheme.TextMuted);
                    _body.AddView(txtL);
                }
                else if (comp is ScriptComponent script)
                {
                    _body.AddView(EditorTheme.CreateDivider(_activity, true, 1));
                    var scHdr = new TextView(_activity) { Text = $"▼ 📜 Script ({script.ScriptName})", TextSize = 11 };
                    scHdr.SetTextColor(EditorTheme.AccentGreen);
                    _body.AddView(scHdr);

                    var btnEditScript = new Button(_activity) { Text = "✏ Edit C# Script", TextSize = 10 };
                    btnEditScript.SetTextColor(Color.White);
                    btnEditScript.SetBackgroundColor(EditorTheme.BgHover);
                    btnEditScript.Click += (s, e) =>
                    {
                        ScriptEditorDialog.Show(_activity, script.ScriptName, script.SourceCode, newCode =>
                        {
                            script.SourceCode = newCode;
                        });
                    };
                    _body.AddView(btnEditScript);
                }
            }

            // Add Component Button
            var btnAddComp = new Button(_activity) { Text = "+ Add Component", TextSize = 11 };
            btnAddComp.SetTextColor(Color.White);
            btnAddComp.SetBackgroundColor(EditorTheme.BgHover);
            var lpAdd = new LayoutParams(ViewGroup.LayoutParams.MatchParent, EditorTheme.DpToPx(_activity, 32))
            {
                TopMargin = EditorTheme.DpToPx(_activity, 12)
            };
            btnAddComp.LayoutParameters = lpAdd;
            btnAddComp.Click += (s, e) =>
            {
                var pop = new PopupMenu(_activity, btnAddComp);
                pop.Menu?.Add("C# Script Component");
                pop.Menu?.Add("Mesh Renderer");
                pop.Menu?.Add("Directional Light");
                pop.MenuItemClick += (sender, args) =>
                {
                    string title = args.Item?.TitleFormatted?.ToString() ?? "";
                    if (title.Contains("Script")) selectedNode.AddComponent<ScriptComponent>();
                    else if (title.Contains("Mesh")) selectedNode.AddComponent<MeshRendererComponent>();
                    else if (title.Contains("Light")) selectedNode.AddComponent<LightComponent>();
                    Rebuild(selectedNode);
                };
                pop.Show();
            };
            _body.AddView(btnAddComp);
        }

        private LinearLayout CreateVectorRow(string label, Vector3 val, Action<Vector3> onChange)
        {
            var row = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
            row.SetPadding(0, EditorTheme.DpToPx(_activity, 2), 0, EditorTheme.DpToPx(_activity, 2));

            var lbl = new TextView(_activity) { Text = label, TextSize = 10, LayoutParameters = new LayoutParams(EditorTheme.DpToPx(_activity, 52), ViewGroup.LayoutParams.WrapContent) };
            lbl.SetTextColor(EditorTheme.TextMuted);
            row.AddView(lbl);

            row.AddView(CreateChip("X", val.X, "#d63031", v => { val.X = v; onChange(val); }));
            row.AddView(CreateChip("Y", val.Y, "#00b894", v => { val.Y = v; onChange(val); }));
            row.AddView(CreateChip("Z", val.Z, "#0984e3", v => { val.Z = v; onChange(val); }));
            return row;
        }

        private TextView CreateChip(string axis, float value, string colorHex, Action<float> onApply)
        {
            var tv = new TextView(_activity) { Text = $"{axis} {value:F2}", TextSize = 10 };
            tv.SetTextColor(Color.White);
            tv.SetBackgroundColor(Color.ParseColor(colorHex));
            tv.SetPadding(EditorTheme.DpToPx(_activity, 4), EditorTheme.DpToPx(_activity, 2), EditorTheme.DpToPx(_activity, 4), EditorTheme.DpToPx(_activity, 2));
            var lp = new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = EditorTheme.DpToPx(_activity, 3) };
            tv.LayoutParameters = lp;

            tv.Click += (s, e) =>
            {
                var input = new EditText(_activity) { Text = value.ToString("F2") };
                input.SetRawInputType(Android.Text.InputTypes.NumberFlagDecimal | Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagSigned);

                new AlertDialog.Builder(_activity)
                    .SetTitle($"Edit {axis}")
                    .SetView(input)
                    .SetPositiveButton("Apply", (dlg, ev) =>
                    {
                        if (float.TryParse(input.Text, out float parsed)) { onApply(parsed); Rebuild(null); }
                    })
                    .SetNegativeButton("Cancel", (dlg, ev) => { })
                    .Show();
            };
            return tv;
        }
    }
}
