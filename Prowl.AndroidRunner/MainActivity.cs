using Android.App;
using Android.Content.PM;
using Android.OS;
using Silk.NET.Maths;
using Silk.NET.Windowing;
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
    public class MainActivity : Activity
    {
        private IView? _view;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Extract game assets to internal storage
            AssetExtractor.EnsureAssetsExtracted(this);

            // Configure Silk View for Android OpenGL ES
            var options = ViewOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
            options.FramesPerSecond = 60;
            options.UpdatesPerSecond = 60;

            // Use SilkWindow instead of Window to avoid collision with Android.App.Activity.Window
            _view = SilkWindow.GetView(options);

            _view.Load += OnLoad;
            _view.Render += OnRender;
            _view.Update += OnUpdate;

            _view.Initialize();
        }

        private void OnLoad()
        {
            // Initialization logic for Prowl Engine
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
