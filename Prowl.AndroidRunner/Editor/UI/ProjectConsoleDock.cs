using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Prowl.AndroidRunner.Editor.UI
{
    public class ProjectConsoleDock : LinearLayout
    {
        private readonly Activity _activity;
        private readonly LinearLayout _logContainer;

        public ProjectConsoleDock(Activity activity, int rightMarginPx) : base(activity)
        {
            _activity = activity;
            Orientation = Orientation.Horizontal;
            LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, EditorTheme.DpToPx(activity, 135))
            {
                Gravity = GravityFlags.Bottom,
                RightMargin = rightMarginPx,
                BottomMargin = EditorTheme.DpToPx(activity, 22)
            };
            SetBackgroundColor(EditorTheme.BgDark);

            // Project Sub-Panel
            var projectPanel = new LinearLayout(activity) { Orientation = Orientation.Vertical, LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f) };
            projectPanel.AddView(EditorTheme.CreateHeaderBar(activity, "📁 Project ✕  |  Assets >"));

            var projScroll = new HorizontalScrollView(activity) { LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            var projList = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
            projList.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 6), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 6));

            string[] assets = { "📁 textures", "📁 lightmaps", "📁 Scripts", "📄 Player.cs", "🎨 Material", "🗿 banana_man" };
            foreach (var a in assets)
            {
                var card = new TextView(activity) { Text = a, TextSize = 10 };
                card.SetTextColor(Color.White);
                card.SetBackgroundColor(EditorTheme.BgHover);
                card.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 12), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 12));
                var lp = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = EditorTheme.DpToPx(activity, 6) };
                card.LayoutParameters = lp;
                projList.AddView(card);
            }
            projScroll.AddView(projList);
            projectPanel.AddView(projScroll);
            AddView(projectPanel);

            AddView(EditorTheme.CreateDivider(activity, horizontal: false));

            // Console Sub-Panel
            var consolePanel = new LinearLayout(activity) { Orientation = Orientation.Vertical, LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1.2f) };
            consolePanel.AddView(EditorTheme.CreateHeaderBar(activity, "📟 Console ✕"));

            var conScroll = new ScrollView(activity) { LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _logContainer = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            _logContainer.SetPadding(EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4), EditorTheme.DpToPx(activity, 8), EditorTheme.DpToPx(activity, 4));
            conScroll.AddView(_logContainer);
            consolePanel.AddView(conScroll);
            AddView(consolePanel);

            AddLog("ℹ info: Prowl Engine mobile environment ready.");
        }

        public void AddLog(string msg)
        {
            _activity.RunOnUiThread(() =>
            {
                var tv = new TextView(_activity) { Text = msg, TextSize = 10 };
                tv.SetTextColor(msg.Contains("⚠") ? EditorTheme.AccentYellow : EditorTheme.TextMuted);
                _logContainer.AddView(tv, 0);
            });
        }

        public void Clear() => _logContainer.RemoveAllViews();
    }
}
