using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Prowl.AndroidRunner.Editor.UI;
using Prowl.Runtime;

namespace Prowl.AndroidRunner.Editor.Scripting
{
    public class ScriptEditorDialog
    {
        public static void Show(Context ctx, string fileName, string initialCode, Action<string> onSaved)
        {
            var dialogView = new LinearLayout(ctx)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
            };
            dialogView.SetBackgroundColor(Color.ParseColor("#12141a"));

            // Title Bar
            var titleBar = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
            titleBar.SetBackgroundColor(Color.ParseColor("#1a1d26"));
            titleBar.SetPadding(EditorTheme.DpToPx(ctx, 12), EditorTheme.DpToPx(ctx, 8), EditorTheme.DpToPx(ctx, 12), EditorTheme.DpToPx(ctx, 8));

            var lblTitle = new TextView(ctx) { Text = $"📝 C# Script Editor — {fileName}", TextSize = 12 };
            lblTitle.SetTextColor(Color.White);
            lblTitle.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            titleBar.AddView(lblTitle);
            dialogView.AddView(titleBar);

            // Code Editor Box
            var scroll = new ScrollView(ctx) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f) };
            var editorContainer = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
            editorContainer.SetPadding(EditorTheme.DpToPx(ctx, 8), EditorTheme.DpToPx(ctx, 8), EditorTheme.DpToPx(ctx, 8), EditorTheme.DpToPx(ctx, 8));

            var editCode = new EditText(ctx)
            {
                Text = initialCode,
                TextSize = 11,
                Typeface = Typeface.Monospace,
                Gravity = GravityFlags.Top | GravityFlags.Left
            };
            editCode.SetTextColor(Color.ParseColor("#9cdcfe"));
            editCode.SetBackgroundColor(Color.Transparent);
            editCode.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            editorContainer.AddView(editCode);
            scroll.AddView(editorContainer);
            dialogView.AddView(scroll);

            // Action Buttons
            var btnContainer = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
            btnContainer.SetBackgroundColor(Color.ParseColor("#161922"));
            btnContainer.SetPadding(EditorTheme.DpToPx(ctx, 10), EditorTheme.DpToPx(ctx, 6), EditorTheme.DpToPx(ctx, 10), EditorTheme.DpToPx(ctx, 6));

            AlertDialog? dialog = null;

            var btnCancel = new Button(ctx) { Text = "Close", TextSize = 11 };
            btnCancel.SetTextColor(EditorTheme.TextMuted);
            btnCancel.SetBackgroundColor(Color.Transparent);
            btnCancel.Click += (s, e) => dialog?.Dismiss();
            btnContainer.AddView(btnCancel);

            btnContainer.AddView(new View(ctx) { LayoutParameters = new LinearLayout.LayoutParams(0, 1, 1f) });

            var btnSave = new Button(ctx) { Text = "💾 Save & Compile", TextSize = 11 };
            btnSave.SetTextColor(Color.White);
            btnSave.SetBackgroundColor(EditorTheme.AccentBlue);
            btnSave.Click += (s, e) =>
            {
                onSaved(editCode.Text ?? "");
                Toast.MakeText(ctx, $"✔ Compiled '{fileName}' successfully!", ToastLength.Short)?.Show();
                dialog?.Dismiss();
            };
            btnContainer.AddView(btnSave);

            dialogView.AddView(btnContainer);

            dialog = new AlertDialog.Builder(ctx)
                .SetView(dialogView)
                .Create();
            dialog.Show();

            dialog.Window?.SetLayout(
                (int)(ctx.Resources!.DisplayMetrics.WidthPixels * 0.85f),
                (int)(ctx.Resources!.DisplayMetrics.HeightPixels * 0.88f)
            );
        }
    }
}
