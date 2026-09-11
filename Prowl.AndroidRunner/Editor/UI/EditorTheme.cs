using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace Prowl.AndroidRunner.Editor.UI
{
    public static class EditorTheme
    {
        // Colors
        public static readonly Color BgDarkest   = Color.ParseColor("#0e1017");
        public static readonly Color BgDark      = Color.ParseColor("#14171e");
        public static readonly Color BgPanel     = Color.ParseColor("#161922");
        public static readonly Color BgHeader    = Color.ParseColor("#1b1f2b");
        public static readonly Color BgHover     = Color.ParseColor("#202530");
        public static readonly Color BorderColor = Color.ParseColor("#252a38");
        public static readonly Color AccentBlue  = Color.ParseColor("#3884ff");
        public static readonly Color AccentGreen = Color.ParseColor("#2ecc71");
        public static readonly Color AccentYellow= Color.ParseColor("#f1c40f");
        public static readonly Color TextPrimary = Color.ParseColor("#ffffff");
        public static readonly Color TextMuted   = Color.ParseColor("#858b98");

        public static int DpToPx(Context ctx, int dp) =>
            (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, dp, ctx.Resources!.DisplayMetrics);

        public static TextView CreateHeaderBar(Context ctx, string title)
        {
            var tv = new TextView(ctx) { Text = title, TextSize = 11 };
            tv.SetTextColor(Color.ParseColor("#9da4b4"));
            tv.SetBackgroundColor(BgHeader);
            tv.SetPadding(DpToPx(ctx, 8), DpToPx(ctx, 4), DpToPx(ctx, 8), DpToPx(ctx, 4));
            return tv;
        }

        public static View CreateDivider(Context ctx, bool horizontal = true, int thicknessDp = 1)
        {
            var div = new View(ctx);
            div.SetBackgroundColor(BorderColor);
            div.LayoutParameters = horizontal
                ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(ctx, thicknessDp))
                : new LinearLayout.LayoutParams(DpToPx(ctx, thicknessDp), ViewGroup.LayoutParams.MatchParent);
            return div;
        }
    }
}
