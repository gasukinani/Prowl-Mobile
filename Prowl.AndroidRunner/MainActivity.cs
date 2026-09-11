using System;
using System.IO;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Prowl.Editor;
using Prowl.Editor.GUI.Panels;
using Prowl.Runtime;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;
using SilkWindow = Silk.NET.Windowing.Window;

namespace Prowl.AndroidRunner
{
    [Activity(
        Label = "Prowl Editor Mobile",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        Theme = "@android:style/Theme.NoTitleBar.Fullscreen"
    )]
    public class MainActivity : SilkActivity
    {
        private const string LogTag = "ProwlEditorAndroid";
        private IView? _view;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            try
            {
                // 1. I-extract ang default assets at shaders sa internal directory
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
            Log.Info(LogTag, "Initializing Prowl Editor & Runtime Subsystems...");

            try
            {
                // 1. Setup Project Path sa Android Internal Storage
                string rootStorage = FilesDir?.AbsolutePath ?? ApplicationContext.FilesDir?.AbsolutePath ?? "/sdcard/Android/data/com.gasukinani.prowlmobile/files";
                string projectPath = Path.Combine(rootStorage, "DefaultProject");

                if (!Directory.Exists(projectPath))
                    Directory.CreateDirectory(projectPath);

                // 2. Initialize Runtime & Editor Application
                Application.Initialize();
                EditorApplication.Initialize();

                // 3. Buksan o gumawa ng Project
                if (Project.HasProject)
                {
                    Project.Open(new DirectoryInfo(projectPath));
                }

                // 4. I-setup ang Editor Panels Layout
                SetupEditorLayout();

                Log.Info(LogTag, "Prowl Editor GUI ready!");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Failed to start Editor: {ex}");
            }
        }

        private void SetupEditorLayout()
        {
            try
            {
                // Buksan ang mga pangunahing Editor Panels
                EditorGui.ClearPanels();
                
                EditorGui.AddPanel(new SceneViewPanel());      // 3D Viewport kung saan makikita ang mundo
                EditorGui.AddPanel(new HierarchyPanel());      // Tree view ng Nodes / GameObjects
                EditorGui.AddPanel(new InspectorPanel());      // Property editor ng selected Object/Component
                EditorGui.AddPanel(new ProjectPanel());        // Asset Manager / File Browser
                EditorGui.AddPanel(new ConsolePanel());        // Debug Logs & Error Output
            }
            catch (Exception ex)
            {
                Log.Warn(LogTag, $"Layout setup notice: {ex.Message}");
            }
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
                EditorApplication.Update();
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Editor Update error: {ex.Message}");
            }
        }

        private void OnRender(double delta)
        {
            try
            {
                Graphics.StartFrame();
                
                // I-render ang 3D Scene Viewport + Editor ImGui / Paper UI Panels
                EditorApplication.Render();
                
                Graphics.EndFrame();
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Editor Render error: {ex.Message}");
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            EditorApplication.Quit();
            Application.Quit();
            _view?.Dispose();
        }
    }
}
