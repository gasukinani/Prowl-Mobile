using Android.App;
using Android.Content.PM;
using Android.OS;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

namespace Prowl.AndroidRunner
{
    [Activity(
        Label = "Prowl Mobile",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
        ScreenOrientation = ScreenOrientation.SensorLandscape)]
    public class MainActivity : SilkActivity
    {
        private IView? _view;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // 1. I-extract ang assets mula sa APK papunta sa storage
            string localDataPath = AssetExtractor.EnsureAssetsExtracted(this);

            // 2. I-configure ang Silk.NET para sa OpenGLES 3.0 (Para sa Mobile)
            var options = ViewOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
            options.VSync = true;

            _view = Silk.NET.Windowing.Window.GetView(options);

            _view.Load += () =>
            {
                // I-initialize ang Prowl Runtime at ituro ang data path
                // Prowl.Runtime.Application.Initialize(localDataPath);
            };

            _view.Update += (delta) =>
            {
                // Prowl.Runtime.Application.Update((float)delta);
            };

            _view.Render += (delta) =>
            {
                // Prowl.Runtime.Application.Render((float)delta);
            };

            _view.Run();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _view?.Dispose();
        }
    }
}
