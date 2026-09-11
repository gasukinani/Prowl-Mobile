using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;
using SilkWindow = Silk.NET.Windowing.Window;

namespace Prowl.AndroidRunner
{
    [Activity(
        Label = "Prowl Mobile",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        Theme = "@android:style/Theme.NoTitleBar.Fullscreen"
    )]
    public class MainActivity : SilkActivity
    {
        private const string LogTag = "ProwlAndroidRunner";
        private IView? _view;
        private GL? _gl;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            try
            {
                // 1. I-extract ang game assets sa internal storage
                AssetExtractor.EnsureAssetsExtracted(this);
            }
            catch (System.Exception ex)
            {
                Log.Error(LogTag, $"Asset extraction error: {ex.Message}");
            }
        }

        protected override void OnRun()
        {
            try
            {
                var options = ViewOptions.Default;
                options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
                options.FramesPerSecond = 60;
                options.UpdatesPerSecond = 60;

                _view = SilkWindow.GetView(options);

                _view.Load += OnLoad;
                _view.Resize += OnResize;
                _view.Render += OnRender;
                _view.Update += OnUpdate;

                _view.Run();
            }
            catch (System.Exception ex)
            {
                Log.Error(LogTag, $"Fatal error during OnRun: {ex}");
                throw;
            }
        }

        private void OnLoad()
        {
            Log.Info(LogTag, "Initializing OpenGL ES context...");

            // Kumuha ng OpenGL ES API instance mula sa Silk View
            _gl = _view?.CreateOpenGLES();

            if (_gl != null && _view != null)
            {
                _gl.Viewport(0, 0, (uint)_view.Size.X, (uint)_view.Size.Y);
                Log.Info(LogTag, $"Viewport configured: {_view.Size.X}x{_view.Size.Y}");
            }

            // DITO I-INITIALIZE ANG PROWL ENGINE (hal. Prowl.Runtime components)
        }

        private void OnResize(Vector2D<int> size)
        {
            if (_gl != null)
            {
                _gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
            }
        }

        private void OnUpdate(double delta)
        {
            // Game logic update (Prowl Engine Update)
        }

        private void OnRender(double delta)
        {
            if (_gl == null) return;

            // 1. Mag-clear ng screen gamit ang kulay (RGB: Cornflower Blue) para mapatunayang buhay ang graphics pipeline
            _gl.ClearColor(0.2f, 0.4f, 0.8f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // 2. DITO TATAWAGIN ANG PROWL ENGINE RENDER PIPELINE:
            // Halimbawa: Prowl.Runtime.Graphics.Render();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _gl?.Dispose();
            _view?.Dispose();
        }
    }
}
