using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Silk.NET.Maths;
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

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // 1. I-extract ang game assets sa internal storage bago tumakbo ang engine
            try
            {
                AssetExtractor.EnsureAssetsExtracted(this);
            }
            catch (System.Exception ex)
            {
                Log.Error(LogTag, $"Asset extraction failed: {ex.Message}");
            }
        }

        // Dito tinatawag ng SilkActivity ang game loop kapag ready na ang SDL Android Surface
        protected override void OnRun()
        {
            try
            {
                var options = ViewOptions.Default;
                options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
                options.FramesPerSecond = 60;
                options.UpdatesPerSecond = 60;

                // Kunin ang view gamit ang SilkWindow
                _view = SilkWindow.GetView(options);

                _view.Load += OnLoad;
                _view.Render += OnRender;
                _view.Update += OnUpdate;

                // Simulan ang Silk view loop
                _view.Run();
            }
            catch (System.Exception ex)
            {
                Log.Error(LogTag, $"Fatal error in OnRun: {ex}");
                throw;
            }
        }

        private void OnLoad()
        {
            Log.Info(LogTag, "Prowl Engine OnLoad initialized successfully!");
            // Initialization logic para sa Prowl Runtime
        }

        private void OnUpdate(double delta)
        {
            // Game update loop
        }

        private void OnRender(double delta)
        {
            // Game render loop
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _view?.Dispose();
        }
    }
}
