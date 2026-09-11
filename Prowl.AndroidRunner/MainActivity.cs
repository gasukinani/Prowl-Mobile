                
                using System;
using System.IO;
using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
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
        private const string LogTag = "ProwlMobile";
        private IView? _view;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            try
            {
                // I-extract ang game assets sa internal app storage
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
                // 1. Storage setup
                string storagePath = FilesDir?.AbsolutePath ?? "";
                string assetsPath = Path.Combine(storagePath, "Assets");
                if (!Directory.Exists(assetsPath))
                    Directory.CreateDirectory(assetsPath);

                // 2. Initialize Core Prowl Runtime
                Application.Initialize();

                // 3. I-setup ang 3D Scene Environment & Nodes
                Setup3DEnvironment();

                Log.Info(LogTag, "Prowl 3D Scene loaded and running!");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Engine Initialization Error: {ex}");
            }
        }

        private void Setup3DEnvironment()
        {
            // Gumawa ng aktibong 3D Scene
            Scene scene = new Scene();
            SceneManager.SetActiveScene(scene);

            // NODE 1: Main Camera Node
            var cameraNode = GameObject.Create("Main Camera");
            cameraNode.Transform.Position = new Vector3(0, 2f, -5f);
            cameraNode.Transform.LookAt(Vector3.Zero);
            var cam = cameraNode.AddComponent<Camera>();
            cam.ClearColor = new Color(0.1f, 0.15f, 0.25f, 1.0f);

            // NODE 2: Sun / Directional Light Node
            var lightNode = GameObject.Create("SunLight");
            lightNode.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(0.6f, 0.8f, 0);
            var light = lightNode.AddComponent<DirectionalLight>();
            light.Color = Color.white;
            light.Intensity = 1.0f;

            // NODE 3: 3D Object Node na may Script
            var cubeNode = GameObject.Create("Interactive 3D Object");
            cubeNode.Transform.Position = Vector3.Zero;
            var renderer = cubeNode.AddComponent<MeshRenderer>();
            renderer.Mesh = Mesh.CreateCube();
            renderer.Material = Material.CreateDefault();

            // Mag-attach ng script para sa animation at touch interaction
            cubeNode.AddComponent<RotatorComponent>();
        }

        private void OnResize(Vector2D<int> size)
        {
            Screen.InternalUpdate((int)size.X, (int)size.Y);
        }

        private void OnUpdate(double delta)
        {
            try
            {
                Time.Update((float)delta);
                SceneManager.ActiveScene?.Update();
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
                Graphics.StartFrame();
                SceneManager.ActiveScene?.Render();
                Graphics.EndFrame();
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Render error: {ex.Message}");
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Application.Quit();
            _view?.Dispose();
        }
    }

    // ==========================================
    // CUSTOM MONOBEHAVIOUR SCRIPT
    // ==========================================
    public class RotatorComponent : MonoBehaviour
    {
        public float RotationSpeed = 50f;

        public override void Update()
        {
            // Awtomatikong pag-ikot sa 3D Space
            Transform.Rotate(new Vector3(15f * Time.DeltaTime, RotationSpeed * Time.DeltaTime, 0));
        }
    }
}
