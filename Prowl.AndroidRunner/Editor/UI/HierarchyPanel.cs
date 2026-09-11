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

            AddView(EditorTheme.CreateHeaderBar(activity, "Hierarchy ✕"));

            var hSearch = new EditText(activity) { Hint = "🔍 Search...", TextSize = 10 };
            hSearch.SetTextColor(Color.White);
            hSearch.SetHintTextColor(EditorTheme.TextMuted);
            hSearch.SetBackgroundColor(EditorTheme.BgHeader);
            hSearch.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4));
            AddView(hSearch);

            var scroll = new ScrollView(activity)
            {
                LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, EditorTheme.DpToPx(activity, 140))
            };
            _treeContainer = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            scroll.AddView(_treeContainer);
            AddView(scroll);
        }

        public void Rebuild(IEnumerable<ProwlNode> nodes, ProwlNode? selectedNode)
        {
            _treeContainer.RemoveAllViews();

            var sceneHeader = CreateItem("📁 Untitled Scene", false);
            _treeContainer.AddView(sceneHeader);

            foreach (var node in nodes)
            {
                bool isSel = (node == selectedNode);
                string icon = node.GetComponent<LightComponent>() != null ? "💡 " :
                              node.GetComponent<MeshRendererComponent>()?.Shape == MeshShape.Tree ? "🌲 " : "📦 ";

                var item = CreateItem($"  {icon}{node.Name}", isSel);
                item.Click += (s, e) => OnNodeSelected?.Invoke(node);
                _treeContainer.AddView(item);
            }
        }

        private TextView CreateItem(string label, bool selected)
        {
            var tv = new TextView(_activity) { Text = label, TextSize = 11 };
            tv.SetTextColor(Color.White);
            tv.SetBackgroundColor(selected ? EditorTheme.AccentBlue : Color.Transparent);
            tv.SetPadding(EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 4), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 4));
            return tv;
        }
    }
}
