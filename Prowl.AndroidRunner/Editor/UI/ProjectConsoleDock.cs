using System;
using System.Collections.Generic;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Prowl.AndroidRunner.Editor.Scripting;

namespace Prowl.AndroidRunner.Editor.UI
{
    public class ProjectConsoleDock : LinearLayout
    {
        private readonly Activity _activity;
        private readonly LinearLayout _gridContainer;
        private readonly LinearLayout _logContainer;
        private readonly TextView _txtBreadcrumb;
        private string _currentPath = "Assets";

        private readonly Dictionary<string, List<(string name, bool isFolder, string type)>> _fileSystem = new()
        {
            ["Assets"] = new() {
                ("banana man", true, "folder"),
                ("BNistro_lightmaps", true, "folder"),
                ("Esper Zero", true, "folder"),
                ("Scripts", true, "folder"),
                ("PlayerController.cs", false, "cs"),
                ("MainMaterial.mat", false, "mat"),
                ("TerrainTexture.png", false, "tex")
            },
            ["Assets/Scripts"] = new() {
                ("PlayerController.cs", false, "cs"),
                ("CameraController.cs", false, "cs"),
                ("Rotator.cs", false, "cs")
            },
            ["Assets/banana man"] = new() {
                ("banana_man.fbx", false, "mesh"),
                ("textures", true, "folder")
            }
        };

        public ProjectConsoleDock(Activity activity, int rightMarginPx) : base(activity)
        {
            _activity = activity;
            Orientation = Orientation.Horizontal;
            LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, EditorTheme.DpToPx(activity, 145))
            {
                Gravity = GravityFlags.Bottom,
                RightMargin = rightMarginPx,
                BottomMargin = EditorTheme.DpToPx(activity, 22)
            };
            SetBackgroundColor(EditorTheme.BgDark);

            // 1. Project Panel
            var projectPanel = new LinearLayout(activity) { Orientation = Orientation.Vertical, LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1.1f) };

            var projHeader = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
            projHeader.SetBackgroundColor(EditorTheme.BgHeader);
            projHeader.SetPadding(EditorTheme.DpToPx(activity, 6), EditorTheme.DpToPx(activity, 3), EditorTheme.DpToPx(activity, 6), EditorTheme.DpToPx(activity, 3));

            var btnBack = new TextView(activity) { Text = "◀ ", TextSize = 11 };
            btnBack.SetTextColor(EditorTheme.TextMuted);
            btnBack.Click += (s, e) => NavigateBack();
            projHeader.AddView(btnBack);

            _txtBreadcrumb = new TextView(activity) { Text = "📁 Assets >", TextSize = 11 };
            _txtBreadcrumb.SetTextColor(Color.White);
            projHeader.AddView(_txtBreadcrumb);

            projectPanel.AddView(projHeader);

            var projScroll = new HorizontalScrollView(activity) { LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _gridContainer = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
            _gridContainer.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 6), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 6));
            projScroll.AddView(_gridContainer);
            projectPanel.AddView(projScroll);
            AddView(projectPanel);

            AddView(EditorTheme.CreateDivider(activity, horizontal: false));

            // 2. Console Panel
            var consolePanel = new LinearLayout(activity) { Orientation = Orientation.Vertical, LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1.2f) };

            var conHeader = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
            conHeader.SetBackgroundColor(EditorTheme.BgHeader);
            conHeader.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 3), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 3));

            var lblCon = new TextView(activity) { Text = "📟 Console ✕  ℹ 69  ⚠ 2", TextSize = 11 };
            lblCon.SetTextColor(EditorTheme.TextMuted);
            lblCon.LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
            conHeader.AddView(lblCon);

            var btnClear = new TextView(activity) { Text = "🗑 Clear", TextSize = 10 };
            btnClear.SetTextColor(EditorTheme.TextMuted);
            btnClear.Click += (s, e) => Clear();
            conHeader.AddView(btnClear);

            consolePanel.AddView(conHeader);

            var conScroll = new ScrollView(activity) { LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _logContainer = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            _logContainer.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4));
            conScroll.AddView(_logContainer);
            consolePanel.AddView(conScroll);
            AddView(consolePanel);

            RefreshFolderView();
            AddLog("ℹ VAO: [ID 0] Mesh uploaded successfully to VRAM (GPU) [DefaultRenderPipeline]");
            AddLog("ℹ Compiling shader pass Standard with Keywords [RenderPipeline]");
        }

        private void RefreshFolderView()
        {
            _gridContainer.RemoveAllViews();
            _txtBreadcrumb.Text = $"📁 {_currentPath} >";

            if (!_fileSystem.ContainsKey(_currentPath))
            {
                _fileSystem[_currentPath] = new() { ("(Empty Folder)", false, "none") };
            }

            foreach (var item in _fileSystem[_currentPath])
            {
                var card = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
                card.SetBackgroundColor(EditorTheme.BgHover);
                card.SetPadding(EditorTheme.DpToPx(_activity, 10), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 10), EditorTheme.DpToPx(_activity, 8));
                var lp = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = EditorTheme.DpToPx(_activity, 8) };
                card.LayoutParameters = lp;

                string icon = item.isFolder ? "📁" : (item.type == "cs" ? "📜 C#" : item.type == "mat" ? "🎨" : "🖼");
                var txtIcon = new TextView(_activity) { Text = icon, TextSize = 14, Gravity = GravityFlags.Center };
                txtIcon.SetTextColor(item.type == "cs" ? EditorTheme.AccentGreen : Color.White);
                card.AddView(txtIcon);

                var txtName = new TextView(_activity) { Text = item.name, TextSize = 9, Gravity = GravityFlags.Center };
                txtName.SetTextColor(Color.White);
                card.AddView(txtName);

                card.Click += (s, e) =>
                {
                    if (item.isFolder)
                    {
                        _currentPath = $"{_currentPath}/{item.name}";
                        RefreshFolderView();
                    }
                    else if (item.type == "cs")
                    {
                        ScriptEditorDialog.Show(_activity, item.name, "// Prowl Custom Script\nusing System;\n\npublic class " + item.name.Replace(".cs", "") + " {\n    public void Start() { }\n}", code =>
                        {
                            AddLog($"ℹ Compiled script: {item.name}");
                        });
                    }
                };

                _gridContainer.AddView(card);
            }
        }

        private void NavigateBack()
        {
            if (_currentPath.Contains("/"))
            {
                _currentPath = _currentPath.Substring(0, _currentPath.LastIndexOf('/'));
                RefreshFolderView();
            }
        }

        public void AddLog(string msg)
        {
            _activity.RunOnUiThread(() =>
            {
                var tv = new TextView(_activity) { Text = msg, TextSize = 9 };
                tv.SetTextColor(msg.Contains("⚠") ? EditorTheme.AccentYellow : EditorTheme.TextMuted);
                _logContainer.AddView(tv, 0);
            });
        }

        public void Clear() => _logContainer.RemoveAllViews();
    }
}
