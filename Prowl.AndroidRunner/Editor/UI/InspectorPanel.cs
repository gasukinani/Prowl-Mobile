using System;
using System.Numerics;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
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

            AddView(EditorTheme.CreateHeaderBar(activity, "Inspector ✕"));

            var scroll = new ScrollView(activity) { LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _body = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            _body.SetPadding(EditorTheme.DpToPx(activity, 10), EditorTheme.DpToPx(activity, 6), EditorTheme.DpToPx(activity, 10), EditorTheme.DpToPx(activity, 6));
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

            var title = new TextView(_activity) { Text = $"☑ {selectedNode.Name}   [Dynamic ▼]", TextSize = 12 };
            title.SetTextColor(Color.White);
            _body.AddView(title);

            var tag = new TextView(_activity) { Text = "Tag: Untagged       Layer: Default", TextSize = 10 };
            tag.SetTextColor(EditorTheme.TextMuted);
            tag.SetPadding(0, EditorTheme.DpToPx(_activity, 2), 0, EditorTheme.DpToPx(_activity, 6));
            _body.AddView(tag);

            // Transform Drawer
            var tfHdr = new TextView(_activity) { Text = "▼ Transform", TextSize = 11 };
            tfHdr.SetTextColor(EditorTheme.AccentBlue);
            _body.AddView(tfHdr);

            _body.AddView(CreateVectorRow("Position", selectedNode.Transform.Position, v => { selectedNode.Transform.Position = v; OnComponentUpdated?.Invoke(); }));
            _body.AddView(CreateVectorRow("Rotation", selectedNode.Transform.Rotation, v => { selectedNode.Transform.Rotation = v; OnComponentUpdated?.Invoke(); }));
            _body.AddView(CreateVectorRow("Scale", selectedNode.Transform.Scale, v => { selectedNode.Transform.Scale = v; OnComponentUpdated?.Invoke(); }));

            // MeshRenderer Drawer
            var mesh = selectedNode.GetComponent<MeshRendererComponent>();
            if (mesh != null)
            {
                var mrHdr = new TextView(_activity) { Text = "▼ MeshRenderer", TextSize = 11 };
                mrHdr.SetTextColor(EditorTheme.AccentBlue);
                mrHdr.SetPadding(0, EditorTheme.DpToPx(_activity, 6), 0, EditorTheme.DpToPx(_activity, 2));
                _body.AddView(mrHdr);

                var txtMesh = new TextView(_activity) { Text = $"Mesh: {mesh.Shape}\nMaterials: 1 element", TextSize = 10 };
                txtMesh.SetTextColor(EditorTheme.TextMuted);
                _body.AddView(txtMesh);
            }

            // Focus Button
            var btnFocus = new Button(_activity) { Text = "🎯 Focus Camera Target", TextSize = 11 };
            btnFocus.SetTextColor(Color.White);
            btnFocus.SetBackgroundColor(EditorTheme.BgHover);
            var lp = new LayoutParams(ViewGroup.LayoutParams.MatchParent, EditorTheme.DpToPx(_activity, 32))
            {
                TopMargin = EditorTheme.DpToPx(_activity, 10)
            };
            btnFocus.LayoutParameters = lp;
            btnFocus.Click += (s, e) => OnFocusRequested?.Invoke(selectedNode.Transform.Position);
            _body.AddView(btnFocus);
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
