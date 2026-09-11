using System;
using System.IO;
using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.SceneManagement;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;
using ProwlApp = Prowl.Runtime.Application;
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
                // I-extract ang game assets sa internal storage
                AssetExtractor.EnsureAssetsExtracted(this);
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Asset extraction error: {ex.Message}");
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
            Log.Info(LogTag, "Initializing Prowl Runtime Engine...");

            try
            {
                // 1. OpenGL ES context setup
                _gl = _view?.CreateOpenGLES();
                if (_gl != null && _view != null)
                {
                    _gl.Viewport(0, 0, (uint)_view.Size.X, (uint)_view.Size.Y);
                }

                // 2. Storage at Project setup
                string storagePath = FilesDir?.AbsolutePath ?? "";
                string assetsPath = Path.Combine(storagePath, "Assets");
                if (!Directory.Exists(assetsPath))
                    Directory.CreateDirectory(assetsPath);

                // 3. I-setup ang 3D Scene Environment & Nodes
                Setup3DEnvironment();

                Log.Info(LogTag, "Prowl 3D Scene & Nodes ready!");
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
                // Gumawa ng bagong GameObject nodes
                var cameraNode = new GameObject("Main Camera");
                cameraNode.Transform.Position = new Vector3(0, 2f, -5f);

                var lightNode = new GameObject("Directional Light");
                lightNode.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(0.6f, 0.8f, 0);

                var cubeNode = new GameObject("3D Node Object");
                cubeNode.Transform.Position = Vector3.Zero;
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
                // Update ng mga aktibong GameObject components at scripts
                if (SceneManager.ActiveScene != null)
                {
                    SceneManager.ActiveScene.Update();
                }
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
                    // I-clear ang frame buffer
                    _gl.ClearColor(0.12f, 0.15f, 0.25f, 1.0f);
                    _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                }

                // Render loop ng aktibong eksena
                if (SceneManager.ActiveScene != null)
                {
                    SceneManager.ActiveScene.Render();
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
        public float Speed = 45f;

        public override void Update()
        {
            // Pag-ikot ng 3D object
            Transform.Rotate(new Vector3(0, Speed * 0.016f, 0));
        }
    }
}
