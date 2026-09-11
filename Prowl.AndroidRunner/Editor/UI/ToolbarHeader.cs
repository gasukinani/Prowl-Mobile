using System;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Prowl.AndroidRunner.Editor.UI
{
    public class ToolbarHeader : LinearLayout
    {
        private readonly Activity _activity;
        private Button? _btnPlay;
        private TextView? _txtFps;
        public event Action? OnPlayToggleRequested;
        public event Action<string>? OnMenuActionSelected;

        public ToolbarHeader(Activity activity) : base(activity)
        {
            _activity = activity;
            Orientation = Orientation.Horizontal;
            LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, EditorTheme.DpToPx(activity, 38))
            {
                Gravity = GravityFlags.Top
            };
            SetBackgroundColor(EditorTheme.BgDark);
            BuildHeader();
        }

        private void BuildHeader()
        {
            // Menus
            AddMenu("File", new[] { "New Scene", "Open Scene...", "Save Scene", "Build Settings..." });
            AddMenu("Edit", new[] { "Undo", "Redo", "Project Settings..." });
            AddMenu("Assets", new[] { "Create C# Script", "Create Material", "Create Shader", "Import Asset..." });
            AddMenu("GameObject", new[] { "3D Object -> Cube", "3D Object -> Low-Poly Tree", "Light -> Directional Light", "Create Empty", "Delete Selected" });
            AddMenu("Window", new[] { "Toggle Sidebar Panel", "Toggle Bottom Dock", "Reset Editor Camera", "Clear Console Logs" });

            // Spacer
            AddView(new View(_activity) { LayoutParameters = new LayoutParams(0, 1, 1f) });

            // Center Play / Pause
            _btnPlay = new Button(_activity) { Text = "▶", TextSize = 13 };
            _btnPlay.SetTextColor(Color.White);
            _btnPlay.SetBackgroundColor(EditorTheme.BgHover);
            _btnPlay.LayoutParameters = new LayoutParams(EditorTheme.DpToPx(_activity, 44), EditorTheme.DpToPx(_activity, 30)) { Gravity = GravityFlags.CenterVertical };
            _btnPlay.Click += (s, e) => OnPlayToggleRequested?.Invoke();
            AddView(_btnPlay);

            var btnPause = new Button(_activity) { Text = "⏸", TextSize = 12 };
            btnPause.SetTextColor(EditorTheme.TextMuted);
            btnPause.SetBackgroundColor(EditorTheme.BgHeader);
            var lpPause = new LayoutParams(EditorTheme.DpToPx(_activity, 38), EditorTheme.DpToPx(_activity, 30))
            {
                Gravity = GravityFlags.CenterVertical,
                LeftMargin = EditorTheme.DpToPx(_activity, 4)
            };
            btnPause.LayoutParameters = lpPause;
            btnPause.Click += (s, e) => Toast.MakeText(_activity, "Game Paused", ToastLength.Short)?.Show();
            AddView(btnPause);

            // Spacer
            AddView(new View(_activity) { LayoutParameters = new LayoutParams(0, 1, 1f) });

            // Stats
            _txtFps = new TextView(_activity) { Text = "🟢 212 FPS 4.7ms", TextSize = 11 };
            _txtFps.SetTextColor(EditorTheme.AccentGreen);
            _txtFps.SetPadding(EditorTheme.DpToPx(_activity, 6), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 6), EditorTheme.DpToPx(_activity, 8));
            AddView(_txtFps);

            var txtVer = new TextView(_activity) { Text = "v1.0-preview | MyGame ⚙", TextSize = 11 };
            txtVer.SetTextColor(EditorTheme.TextMuted);
            txtVer.SetPadding(EditorTheme.DpToPx(_activity, 6), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 12), EditorTheme.DpToPx(_activity, 8));
            AddView(txtVer);
        }

        private void AddMenu(string title, string[] items)
        {
            var btn = new TextView(_activity) { Text = title, TextSize = 12 };
            btn.SetTextColor(EditorTheme.TextMuted);
            btn.SetPadding(EditorTheme.DpToPx(_activity, 10), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 10), EditorTheme.DpToPx(_activity, 8));
            btn.Click += (s, e) =>
            {
                var popup = new PopupMenu(_activity, btn);
                for (int i = 0; i < items.Length; i++) popup.Menu.Add(0, i, i, items[i]);
                popup.MenuItemClick += (send, args) => OnMenuActionSelected?.Invoke(args.Item?.TitleFormatted?.ToString() ?? "");
                popup.Show();
            };
            AddView(btn);
        }

        public void SetPlayState(bool isPlaying)
        {
            if (_btnPlay == null) return;
            _btnPlay.Text = isPlaying ? "⏹" : "▶";
            _btnPlay.SetTextColor(isPlaying ? EditorTheme.AccentGreen : Color.White);
        }

        public void UpdateFps(int fps, float ms)
        {
            _activity.RunOnUiThread(() =>
            {
                if (_txtFps != null) _txtFps.Text = $"🟢 {fps} FPS {ms:F1}ms";
            });
        }
    }
}
