using System;
using System.IO;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Prowl.Runtime;
using Prowl.Vector;
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
        private const string LogTag = "ProwlMobile";
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
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Asset extraction warning: {ex.Message}");
            }
        }

        protected override void OnRun()
        {
            var options = ViewOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
            options.FramesPerSecond = 60;
            options.UpdatesPerSecond = 60;

            _view = SilkWindow.GetView(options);

            _view.Load += OnLoad;
            _view.Resize += OnResize;
            _view.Update += OnUpdate;
            _view.Render += OnRender;

            _view.Run();
        }

        private void OnLoad()
        {
            Log.Info(LogTag, "Initializing Prowl Engine subsystems...");

            try
            {
                // 1. OpenGL ES context setup
                _gl = _view?.CreateOpenGLES();
                if (_gl != null && _view != null)
                {
                    _gl.Viewport(0, 0, (uint)_view.Size.X, (uint)_view.Size.Y);
                }

                // 2. Storage directory para sa Assets
                string storagePath = FilesDir?.AbsolutePath ?? "";
                string assetsPath = Path.Combine(storagePath, "Assets");
                if (!Directory.Exists(assetsPath))
                    Directory.CreateDirectory(assetsPath);

                // 3. I-setup ang 3D Scene Environment & Nodes
                Setup3DEnvironment();

                Log.Info(LogTag, "Prowl 3D Engine & Nodes ready!");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Engine Initialization Error: {ex}");
            }
        }

        private void Setup3DEnvironment()
        {
            try
            {
                // NODE 1: Main Camera Node (gamit ang Prowl.Vector.Vector3)
                var cameraNode = new GameObject("Main Camera");
                cameraNode.Transform.Position = new Vector3(0, 2.0, -5.0);

                // NODE 2: Light Node (gamit ang Prowl.Vector.Quaternion)
                var lightNode = new GameObject("Directional Light");
                lightNode.Transform.Rotation = Quaternion.Euler(45.0, 30.0, 0.0);

                // NODE 3: 3D Object Node na may Script Component
                var cubeNode = new GameObject("3D Node Object");
                cubeNode.Transform.Position = Vector3.zero;
                cubeNode.AddComponent<RotatorComponent>();
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, $"Scene setup notice: {ex.Message}");
            }
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
            try
            {
                // Game Loop Update
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Update error: {ex.Message}");
            }
        }

        private void OnRender(double delta)
        {
            try
            {
                if (_gl != null)
                {
                    // I-clear ang frame buffer (Dark Blue Slate)
                    _gl.ClearColor(0.12f, 0.15f, 0.25f, 1.0f);
                    _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                }
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Render error: {ex.Message}");
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _gl?.Dispose();
            _view?.Dispose();
        }
    }

    // ==========================================
    // C# MONOBEHAVIOUR SCRIPT COMPONENT
    // ==========================================
    public class RotatorComponent : MonoBehaviour
    {
        public double Speed = 45.0;

        public override void Update()
        {
            // Pag-ikot gamit ang Prowl.Vector.Vector3
            Transform.Rotate(new Vector3(0, Speed * 0.016, 0));
        }
    }
}
