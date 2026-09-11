using System;
using System.Numerics;
using Android.Content;
using Android.Graphics;
using Android.Views;

namespace Prowl.AndroidRunner.Editor.Input
{
    public class VirtualJoystickView : View
    {
        private readonly Action<Vector2> _onMoved;
        private readonly Paint _basePaint;
        private readonly Paint _stickPaint;
        private float _centerX, _centerY, _baseRadius, _stickRadius, _stickX, _stickY;

        public VirtualJoystickView(Context ctx, Action<Vector2> onMoved) : base(ctx)
        {
            _onMoved = onMoved;
            _basePaint = new Paint(PaintFlags.AntiAlias) { Color = Color.Argb(70, 25, 30, 45), StrokeWidth = 3 };
            _basePaint.SetStyle(Paint.Style.FillAndStroke);

            _stickPaint = new Paint(PaintFlags.AntiAlias) { Color = Color.Argb(160, 56, 132, 255) };
            _stickPaint.SetStyle(Paint.Style.Fill);
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            _centerX = w * 0.5f;
            _centerY = h * 0.5f;
            _baseRadius = MathF.Min(w, h) * 0.45f;
            _stickRadius = _baseRadius * 0.38f;
            _stickX = _centerX;
            _stickY = _centerY;
        }

        protected override void OnDraw(Canvas? canvas)
        {
            if (canvas == null) return;
            canvas.DrawCircle(_centerX, _centerY, _baseRadius, _basePaint);
            canvas.DrawCircle(_stickX, _stickY, _stickRadius, _stickPaint);
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null) return false;
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                case MotionEventActions.Move:
                    float dx = e.GetX() - _centerX;
                    float dy = e.GetY() - _centerY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > _baseRadius) { dx = (dx / dist) * _baseRadius; dy = (dy / dist) * _baseRadius; }
                    _stickX = _centerX + dx;
                    _stickY = _centerY + dy;
                    _onMoved(new Vector2(dx / _baseRadius, -dy / _baseRadius));
                    Invalidate();
                    return true;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    _stickX = _centerX;
                    _stickY = _centerY;
                    _onMoved(Vector2.Zero);
                    Invalidate();
                    return true;
            }
            return base.OnTouchEvent(e);
        }
    }
}
