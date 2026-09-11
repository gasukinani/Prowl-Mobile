using System.Reflection;
using System.Runtime.InteropServices;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Java.Lang;
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
        private const string LogTag = "ProwlAndroidRunner";
        private IView? _view;

        // Static constructor: Tumatakbo agad bago gamitin ang anumang Silk/Prowl code
        static MainActivity()
        {
            SetupNativeLibraries();
        }

        private static void SetupNativeLibraries()
        {
            // 1. Pagkakasunod-sunod ng native libraries na kailangang i-load sa memory
            string[] libraries = 
            {
                "c++_shared",
                "monosgen-2.0",
                "monodroid",
                "SDL2",
                "openal"
            };

            foreach (var lib in libraries)
            {
                try
                {
                    JavaSystem.LoadLibrary(lib);
                    Log.Info(LogTag, $"[NativeLoader] Loaded lib{lib}.so");
                }
                catch (Java.Lang.UnsatisfiedLinkError ex)
                {
                    Log.Warn(LogTag, $"[NativeLoader] Optional lib{lib}.so not found or already in memory: {ex.Message}");
                }
                catch (System.Exception ex)
                {
                    Log.Error(LogTag, $"[NativeLoader] Error loading lib{lib}.so: {ex.Message}");
                }
            }

            // 2. I-set ang Custom DllImport Resolver para sa Silk.NET at Prowl P/Invokes
            NativeLibrary.SetDllImportResolver(typeof(MainActivity).Assembly, ResolveNativeLibrary);
            NativeLibrary.SetDllImportResolver(typeof(SilkWindow).Assembly, ResolveNativeLibrary);
        }

        private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // I-map ang common library names sa Android .so equivalents
            string mappedName = libraryName switch
            {
                "SDL2" or "SDL2.dll" or "libSDL2" => "libSDL2.so",
                "openal" or "openal32.dll" or "soft_oal.dll" => "libopenal.so",
                "monosgen-2.0" or "mono-2.0" => "libmonosgen-2.0.so",
                _ => libraryName
            };

            if (NativeLibrary.TryLoad(mappedName, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }

            // Fallback kung hindi gumana ang buong pangalan
            if (!mappedName.EndsWith(".so"))
            {
                if (NativeLibrary.TryLoad($"lib{mappedName}.so", assembly, searchPath, out handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            try
            {
                // 1. I-extract ang internal game assets
                AssetExtractor.EnsureAssetsExtracted(this);

                // 2. Setup Silk View para sa Android OpenGL ES 3.0
                var options = ViewOptions.Default;
                options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
                options.FramesPerSecond = 60;
                options.UpdatesPerSecond = 60;

                // 3. Gumawa ng Silk View instance
                _view = SilkWindow.GetView(options);

                _view.Load += OnLoad;
                _view.Render += OnRender;
                _view.Update += OnUpdate;

                _view.Initialize();
            }
            catch (System.Exception ex)
            {
                Log.Error(LogTag, $"Fatal Crash during OnCreate: {ex}");
                throw;
            }
        }

        private void OnLoad()
        {
            Log.Info(LogTag, "Prowl Engine initialized successfully.");
        }

        private void OnUpdate(double delta)
        {
            // Game update logic
        }

        private void OnRender(double delta)
        {
            // Game render logic
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _view?.Dispose();
        }
    }
}
