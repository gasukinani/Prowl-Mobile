using System;
using System.Collections.Generic;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Prowl.Runtime;

namespace Prowl.AndroidRunner.Editor.UI
{
    public class HierarchyPanel : LinearLayout
    {
        private readonly Activity _activity;
        private readonly LinearLayout _treeContainer;
        public event Action<ProwlNode>? OnNodeSelected;

        public HierarchyPanel(Activity activity) : base(activity)
        {
            _activity = activity;
            Orientation = Orientation.Vertical;

            AddView(EditorTheme.CreateHeaderBar(activity, "❖ Hierarchy ✕"));

            var hSearch = new EditText(activity) { Hint = "🔍 Search...", TextSize = 10 };
            hSearch.SetTextColor(Color.White);
            hSearch.SetHintTextColor(EditorTheme.TextMuted);
            hSearch.SetBackgroundColor(EditorTheme.BgHeader);
            hSearch.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4));
            AddView(hSearch);

            var scroll = new ScrollView(activity)
            {
                LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, EditorTheme.DpToPx(activity, 150))
            };
            _treeContainer = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            scroll.AddView(_treeContainer);
            AddView(scroll);
        }

        public void Rebuild(IEnumerable<ProwlNode> nodes, ProwlNode? selectedNode)
        {
            _treeContainer.RemoveAllViews();

            var sceneHeader = new TextView(_activity) { Text = "▼ 📁 Untitled Scene", TextSize = 11 };
            sceneHeader.SetTextColor(Color.ParseColor("#e67e22"));
            sceneHeader.SetPadding(EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 4), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 4));
            _treeContainer.AddView(sceneHeader);

            foreach (var node in nodes)
            {
                bool isSel = (node == selectedNode);
                string icon = node.GetComponent<LightComponent>() != null ? "💡 " :
                              node.GetComponent<MeshRendererComponent>()?.Shape == MeshShape.Tree ? "🌲 " :
                              node.GetComponent<ScriptComponent>() != null ? "📜 " : "📦 ";

                var row = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
                row.SetBackgroundColor(isSel ? EditorTheme.AccentBlue : Color.Transparent);
                row.SetPadding(EditorTheme.DpToPx(_activity, 14), EditorTheme.DpToPx(_activity, 3), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 3));

                var label = new TextView(_activity) { Text = $"{icon}{node.Name}", TextSize = 11 };
                label.SetTextColor(node.IsActive ? Color.White : EditorTheme.TextMuted);
                label.LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                row.AddView(label);

                var eye = new TextView(_activity) { Text = node.IsActive ? "👁" : "🕶", TextSize = 11 };
                eye.SetTextColor(EditorTheme.TextMuted);
                eye.Click += (s, e) =>
                {
                    node.IsActive = !node.IsActive;
                    Rebuild(nodes, selectedNode);
                };
                row.AddView(eye);

                row.Click += (s, e) => OnNodeSelected?.Invoke(node);
                _treeContainer.AddView(row);
            }
        }
    }
}
