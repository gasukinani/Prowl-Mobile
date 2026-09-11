using System;
using System.Collections.Generic;
using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Prowl.AndroidRunner.Editor.Input;
using Prowl.AndroidRunner.Editor.Rendering;
using Prowl.AndroidRunner.Editor.UI;
using Prowl.Runtime;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;
using SilkWindow = Silk.NET.Windowing.Window;

namespace Prowl.AndroidRunner
{
    [Activity(
        Label = "Prowl Studio Mobile",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        Theme = "@android:style/Theme.NoTitleBar.Fullscreen"
    )]
    public class MainActivity : SilkActivity
    {
        private IView? _view;
        private GL? _gl;
        private EditorSceneRenderer? _renderer;

        // UI Panels
        private ToolbarHeader? _toolbar;
        private HierarchyPanel? _hierarchyPanel;
        private InspectorPanel? _inspectorPanel;
        private ProjectConsoleDock? _consoleDock;
        private LinearLayout? _rightSidebar;

        // Engine State
        private readonly Scene _scene = new();
        private ProwlNode? _selectedNode;
        private bool _isPlaying = false;
        private readonly Dictionary<ProwlNode, (Vector3 pos, Vector3 rot, Vector3 scale)> _initialTransforms = new();

        // Natural Editor Camera Settings (Matches Screenshot 2)
        private float _camYaw = 45.0f;
        private float _camPitch = 32.0f;
        private float _camDistance = 6.2f; // Tamang-tamang layo tulad ng nasa screenshot
        private Vector3 _camTarget = new(-0.5f, 0.4f, 0.0f);
        private readonly float _fov = 55.0f;

        // Joystick Input
        private Vector2 _moveVector = Vector2.Zero;
        private Vector2 _lookVector = Vector2.Zero;

        private float _fpsTimer;
        private int _frameCount;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try { AssetExtractor.EnsureAssetsExtracted(this); } catch { }

            InitDefaultScene();
            RunOnUiThread(BuildStudioInterface);
        }

        private void InitDefaultScene()
        {
            // 1. Directional Light
            var sun = _scene.CreateNode("Directional Light");
            sun.AddComponent<LightComponent>().Type = LightType.Directional;
            sun.Transform.Position = new Vector3(0, 3.2f, 0);

            // 2. Center Cube with Player Script
            var cube = _scene.CreateNode("Cube");
            cube.Transform.Position = new Vector3(-0.7919196f, 0.49999952f, -0.13459778f);
            cube.AddComponent<MeshRendererComponent>().Shape = MeshShape.Cube;
            cube.AddComponent<ScriptComponent>(); // Attached C# Script

            // 3. Environment Trees
            var tree1 = _scene.CreateNode("Tree_1");
            tree1.Transform.Position = new Vector3(2.5f, 0, 1.8f);
            tree1.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            var tree2 = _scene.CreateNode("Tree_2");
            tree2.Transform.Position = new Vector3(-2.4f, 0, 2.0f);
            tree2.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            _selectedNode = cube;
            _scene.Start();
        }

        private void BuildStudioInterface()
        {
            var root = new FrameLayout(this) { LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            int rightWidth = EditorTheme.DpToPx(this, 260);

            // 1. Top Bar
            _toolbar = new ToolbarHeader(this);
            _toolbar.OnPlayToggleRequested += TogglePlay;
            _toolbar.OnMenuActionSelected += HandleMenuAction;
            root.AddView(_toolbar);

            // 2. Viewport Tabs (❖ Scene, 🎮 Game, ⚙ Preferences)
            var vpTabs = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
                LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, EditorTheme.DpToPx(this, 28))
                {
                    TopMargin = EditorTheme.DpToPx(this, 40),
                    LeftMargin = EditorTheme.DpToPx(this, 8)
                }
            };
            vpTabs.AddView(CreateTabButton("❖ Scene ✕", true));
            vpTabs.AddView(CreateTabButton("🎮 Game", false));
            vpTabs.AddView(CreateTabButton("⚙ Preferences", false));
            root.AddView(vpTabs);

            // 3. Viewport Tools (Top-Left)
            var toolOverlay = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(EditorTheme.DpToPx(this, 30), ViewGroup.LayoutParams.WrapContent)
                {
                    TopMargin = EditorTheme.DpToPx(this, 75),
                    LeftMargin = EditorTheme.DpToPx(this, 10)
                }
            };
            toolOverlay.SetBackgroundColor(Color.ParseColor("#a0181b25"));
            toolOverlay.AddView(CreateToolIcon("✥", "Translate Tool"));
            toolOverlay.AddView(CreateToolIcon("↻", "Rotate Tool"));
            toolOverlay.AddView(CreateToolIcon("⤢", "Scale Tool"));
            root.AddView(toolOverlay);

            // 4. Right Sidebar (Hierarchy & Inspector)
            _rightSidebar = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(rightWidth, ViewGroup.LayoutParams.MatchParent)
                {
                    Gravity = GravityFlags.Right,
                    TopMargin = EditorTheme.DpToPx(this, 38),
                    BottomMargin = EditorTheme.DpToPx(this, 22)
                }
            };
            _rightSidebar.SetBackgroundColor(EditorTheme.BgPanel);

            _hierarchyPanel = new HierarchyPanel(this);
            _hierarchyPanel.OnNodeSelected += node => { _selectedNode = node; RefreshUI(); };
            _rightSidebar.AddView(_hierarchyPanel);
            _rightSidebar.AddView(EditorTheme.CreateDivider(this));

            _inspectorPanel = new InspectorPanel(this);
            _inspectorPanel.OnFocusRequested += target => _camTarget = target;
            _inspectorPanel.OnComponentUpdated += () => RefreshUI();
            _rightSidebar.AddView(_inspectorPanel);

            root.AddView(_rightSidebar);

            // 5. Bottom Dock (Interactive Project Browser + Console)
            _consoleDock = new ProjectConsoleDock(this, rightWidth);
            root.AddView(_consoleDock);

            // 6. Viewport Joysticks
            var joystickOverlay = new FrameLayout(this) { LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };

            var leftJoy = new VirtualJoystickView(this, v => _moveVector = v);
            leftJoy.LayoutParameters = new FrameLayout.LayoutParams(EditorTheme.DpToPx(this, 110), EditorTheme.DpToPx(this, 110))
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Left,
                LeftMargin = EditorTheme.DpToPx(this, 10),
                BottomMargin = EditorTheme.DpToPx(this, 155)
            };
            joystickOverlay.AddView(leftJoy);

            var rightJoy = new VirtualJoystickView(this, v => _lookVector = v);
            rightJoy.LayoutParameters = new FrameLayout.LayoutParams(EditorTheme.DpToPx(this, 110), EditorTheme.DpToPx(this, 110))
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Right,
                RightMargin = rightWidth + EditorTheme.DpToPx(this, 10),
                BottomMargin = EditorTheme.DpToPx(this, 155)
            };
            joystickOverlay.AddView(rightJoy);

            root.AddView(joystickOverlay);

            AddContentView(root, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            RefreshUI();
        }

        private TextView CreateTabButton(string text, bool isActive)
        {
            var tv = new TextView(this) { Text = text, TextSize = 10 };
            tv.SetTextColor(isActive ? EditorTheme.AccentBlue : EditorTheme.TextMuted);
            tv.SetBackgroundColor(isActive ? EditorTheme.BgHeader : Color.Transparent);
            tv.SetPadding(EditorTheme.DpToPx(this, 8), EditorTheme.DpToPx(this, 4), EditorTheme.DpToPx(this, 8), EditorTheme.DpToPx(this, 4));
            return tv;
        }

        private TextView CreateToolIcon(string icon, string tooltip)
        {
            var tv = new TextView(this) { Text = icon, TextSize = 12, Gravity = GravityFlags.Center };
            tv.SetTextColor(Color.White);
            tv.SetPadding(EditorTheme.DpToPx(this, 4), EditorTheme.DpToPx(this, 6), EditorTheme.DpToPx(this, 4), EditorTheme.DpToPx(this, 6));
            tv.Click += (s, e) => Toast.MakeText(this, tooltip, ToastLength.Short)?.Show();
            return tv;
        }

        private void RefreshUI()
        {
            _hierarchyPanel?.Rebuild(_scene.Nodes, _selectedNode);
            _inspectorPanel?.Rebuild(_selectedNode);
        }

        private void TogglePlay()
        {
            _isPlaying = !_isPlaying;
            _toolbar?.SetPlayState(_isPlaying);

            if (_isPlaying)
            {
                _initialTransforms.Clear();
                foreach (var n in _scene.Nodes) _initialTransforms[n] = (n.Transform.Position, n.Transform.Rotation, n.Transform.Scale);
                _consoleDock?.AddLog("▶ Engine entered Play Mode (Scripts Active)");
            }
            else
            {
                foreach (var kvp in _initialTransforms)
                {
                    kvp.Key.Transform.Position = kvp.Value.pos;
                    kvp.Key.Transform.Rotation = kvp.Value.rot;
                    kvp.Key.Transform.Scale = kvp.Value.scale;
                }
                _consoleDock?.AddLog("⏹ State restored to Edit Mode");
                RefreshUI();
            }
        }

        private void HandleMenuAction(string action)
        {
            if (action.Contains("Cube"))
            {
                var c = _scene.CreateNode("Cube_" + (_scene.Nodes.Count + 1));
                c.Transform.Position = _camTarget + new Vector3(0, 0.5f, 0);
                c.AddComponent<MeshRendererComponent>().Shape = MeshShape.Cube;
                _selectedNode = c;
                RefreshUI();
            }
            else if (action.Contains("Tree"))
            {
                var t = _scene.CreateNode("Tree_" + (_scene.Nodes.Count + 1));
                t.Transform.Position = _camTarget;
                t.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;
                _selectedNode = t;
                RefreshUI();
            }
            else if (action.Contains("Delete") && _selectedNode != null)
            {
                _scene.Nodes.Remove(_selectedNode);
                _selectedNode = _scene.Nodes.Count > 0 ? _scene.Nodes[0] : null;
                RefreshUI();
            }
            else if (action.Contains("Toggle Sidebar"))
            {
                if (_rightSidebar != null)
                    _rightSidebar.Visibility = _rightSidebar.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible;
            }
            else if (action.Contains("Reset Editor Camera"))
            {
                _camYaw = 45.0f;
                _camPitch = 32.0f;
                _camDistance = 6.2f;
                _camTarget = new Vector3(-0.5f, 0.4f, 0.0f);
            }
            else if (action.Contains("Clear Console"))
            {
                _consoleDock?.Clear();
            }
        }

        protected override void OnRun()
        {
            var options = ViewOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 0));
            options.FramesPerSecond = 60;

            _view = SilkWindow.GetView(options);
            _view.Load += () => { _gl = _view.CreateOpenGLES(); _renderer = new EditorSceneRenderer(_gl!); };
            _view.Update += OnUpdate;
            _view.Render += OnRender;
            _view.Run();
        }

        private void OnUpdate(double delta)
        {
            float dt = (float)delta;
            _fpsTimer += dt;
            _frameCount++;
            if (_fpsTimer >= 1.0f)
            {
                _toolbar?.UpdateFps(_frameCount, (1.0f / _frameCount) * 1000f);
                _frameCount = 0;
                _fpsTimer = 0f;
            }

            // Smooth Orbit Navigation
            float lookSpeed = 65.0f, moveSpeed = 5.0f;
            if (_lookVector != Vector2.Zero)
            {
                _camYaw += _lookVector.X * lookSpeed * dt;
                _camPitch = Math.Clamp(_camPitch - _lookVector.Y * lookSpeed * dt, -80.0f, 85.0f);
            }

            if (_moveVector != Vector2.Zero)
            {
                float radY = _camYaw * MathF.PI / 180f;
                Vector3 forward = new(MathF.Sin(radY), 0, MathF.Cos(radY));
                Vector3 right = new(MathF.Cos(radY), 0, -MathF.Sin(radY));
                _camTarget += ((forward * _moveVector.Y) + (right * _moveVector.X)) * moveSpeed * dt;
            }

            _scene.Update(dt, _moveVector, _isPlaying);
        }

        private void OnRender(double delta)
        {
            if (_view == null || _renderer == null) return;
            _renderer.Render(_view.Size.X, _view.Size.Y, _fov, _camDistance, _camYaw, _camPitch, _camTarget, _scene, _selectedNode, !_isPlaying);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _renderer?.Dispose();
            _gl?.Dispose();
            _view?.Dispose();
        }
    }
}
