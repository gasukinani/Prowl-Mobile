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

        // Camera State
        private float _camYaw = 40.0f;
        private float _camPitch = 24.0f;
        private float _camDistance = 11.5f;
        private Vector3 _camTarget = new(0, 0.8f, 0);
        private readonly float _fov = 60.0f;

        // Navigation
        private Vector2 _moveVector = Vector2.Zero;
        private Vector2 _lookVector = Vector2.Zero;
        private float _flyElevation = 0f;

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
            var sun = _scene.CreateNode("Directional Light");
            sun.AddComponent<LightComponent>().Type = LightType.Directional;
            sun.Transform.Position = new Vector3(0, 4f, 0);

            var cube = _scene.CreateNode("Cube");
            cube.Transform.Position = new Vector3(-0.79f, 0.5f, -0.13f);
            cube.AddComponent<MeshRendererComponent>().Shape = MeshShape.Cube;

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

            // 2. Right Sidebar (Hierarchy & Inspector)
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

            // 3. Bottom Dock
            _consoleDock = new ProjectConsoleDock(this, rightWidth);
            root.AddView(_consoleDock);

            // 4. Viewport Joysticks Overlay
            var joystickOverlay = new FrameLayout(this) { LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };

            var leftJoy = new VirtualJoystickView(this, v => _moveVector = v);
            leftJoy.LayoutParameters = new FrameLayout.LayoutParams(EditorTheme.DpToPx(this, 120), EditorTheme.DpToPx(this, 120))
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Left,
                LeftMargin = EditorTheme.DpToPx(this, 12),
                BottomMargin = EditorTheme.DpToPx(this, 145)
            };
            joystickOverlay.AddView(leftJoy);

            var rightJoy = new VirtualJoystickView(this, v => _lookVector = v);
            rightJoy.LayoutParameters = new FrameLayout.LayoutParams(EditorTheme.DpToPx(this, 120), EditorTheme.DpToPx(this, 120))
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Right,
                RightMargin = rightWidth + EditorTheme.DpToPx(this, 12),
                BottomMargin = EditorTheme.DpToPx(this, 145)
            };
            joystickOverlay.AddView(rightJoy);

            root.AddView(joystickOverlay);

            AddContentView(root, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            RefreshUI();
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
                _consoleDock?.AddLog("▶ Engine entered Play Mode");
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

            // Navigation
            float lookSpeed = 65.0f, moveSpeed = 6.5f;
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

            _camTarget.Y += _flyElevation * moveSpeed * dt;

            if (_isPlaying && _selectedNode != null)
            {
                var cur = _selectedNode.Transform.Rotation;
                _selectedNode.Transform.Rotation = new Vector3(cur.X, cur.Y + dt * 50.0f, cur.Z);
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
