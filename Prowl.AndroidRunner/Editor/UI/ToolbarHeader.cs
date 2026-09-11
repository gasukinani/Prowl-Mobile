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
            AddMenu("File", new[] { "New Scene", "Open Scene...", "Save Scene", "Build Settings..." });
            AddMenu("Edit", new[] { "Undo", "Redo", "Project Settings..." });
            AddMenu("Assets", new[] { "Create C# Script", "Create Material", "Create Shader", "Import Asset..." });
            AddMenu("GameObject", new[] { "3D Object -> Cube", "3D Object -> Low-Poly Tree", "Light -> Directional Light", "Create Empty", "Delete Selected" });
            AddMenu("Window", new[] { "Toggle Sidebar Panel", "Toggle Bottom Dock", "Reset Editor Camera", "Clear Console Logs" });

            // Center Pill Buttons
            AddView(new View(_activity) { LayoutParameters = new LayoutParams(0, 1, 1f) });

            var pill = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
            pill.SetBackgroundColor(EditorTheme.BgHover);
            pill.SetPadding(EditorTheme.DpToPx(_activity, 4), EditorTheme.DpToPx(_activity, 2), EditorTheme.DpToPx(_activity, 4), EditorTheme.DpToPx(_activity, 2));

            _btnPlay = new Button(_activity) { Text = "▶", TextSize = 12 };
            _btnPlay.SetTextColor(Color.White);
            _btnPlay.SetBackgroundColor(Color.Transparent);
            _btnPlay.LayoutParameters = new LayoutParams(EditorTheme.DpToPx(_activity, 36), EditorTheme.DpToPx(_activity, 28));
            _btnPlay.Click += (s, e) => OnPlayToggleRequested?.Invoke();
            pill.AddView(_btnPlay);

            var btnPause = new Button(_activity) { Text = "⏸", TextSize = 11 };
            btnPause.SetTextColor(EditorTheme.TextMuted);
            btnPause.SetBackgroundColor(Color.Transparent);
            btnPause.LayoutParameters = new LayoutParams(EditorTheme.DpToPx(_activity, 32), EditorTheme.DpToPx(_activity, 28));
            btnPause.Click += (s, e) => Toast.MakeText(_activity, "Game Paused", ToastLength.Short)?.Show();
            pill.AddView(btnPause);

            var btnStep = new Button(_activity) { Text = "⏭", TextSize = 11 };
            btnStep.SetTextColor(EditorTheme.TextMuted);
            btnStep.SetBackgroundColor(Color.Transparent);
            btnStep.LayoutParameters = new LayoutParams(EditorTheme.DpToPx(_activity, 32), EditorTheme.DpToPx(_activity, 28));
            btnStep.Click += (s, e) => Toast.MakeText(_activity, "Step 1 Frame", ToastLength.Short)?.Show();
            pill.AddView(btnStep);

            AddView(pill);

            AddView(new View(_activity) { LayoutParameters = new LayoutParams(0, 1, 1f) });

            // Stats
            _txtFps = new TextView(_activity) { Text = "🟢 212 FPS 4.7ms", TextSize = 11 };
            _txtFps.SetTextColor(EditorTheme.AccentGreen);
            _txtFps.SetPadding(EditorTheme.DpToPx(_activity, 6), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 6), EditorTheme.DpToPx(_activity, 8));
            AddView(_txtFps);

            var txtVer = new TextView(_activity) { Text = "v1.0-preview | MyGame5 ⚙", TextSize = 11 };
            txtVer.SetTextColor(EditorTheme.TextMuted);
            txtVer.SetPadding(EditorTheme.DpToPx(_activity, 6), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 12), EditorTheme.DpToPx(_activity, 8));
            AddView(txtVer);
        }

        private void AddMenu(string title, string[] items)
        {
            var btn = new TextView(_activity) { Text = title, TextSize = 12 };
            btn.SetTextColor(EditorTheme.TextMuted);
            btn.SetPadding(EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 8), EditorTheme.DpToPx(_activity, 8));
            btn.Click += (s, e) =>
            {
                var popup = new PopupMenu(_activity, btn);
                var menu = popup.Menu;
                if (menu != null)
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        menu.Add(0, i, i, items[i]);
                    }
                }
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
