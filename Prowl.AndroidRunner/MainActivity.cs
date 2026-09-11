using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Prowl.Runtime;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;
using SilkWindow = Silk.NET.Windowing.Window;

namespace Prowl.AndroidRunner
{
    [Activity(
        Label = "Prowl Mobile Studio",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        Theme = "@android:style/Theme.NoTitleBar.Fullscreen"
    )]
    public class MainActivity : SilkActivity
    {
        private const string LogTag = "ProwlStudio";
        private IView? _view;
        private GL? _gl;

        // --- Shaders & Buffers ---
        private uint _shaderProgram;
        private uint _vaoHumanoid, _vboHumanoid;
        private int _humanoidVertCount;
        private uint _vaoCube, _vboCube;
        private uint _vaoGrid, _vboGrid;
        private int _gridVertCount;

        // --- Engine & Editor State ---
        public enum EditorTab { Viewport, Hierarchy, Inspector, ScriptEditor, AssetBrowser }
        public enum PlayState { EditMode, PlayMode }

        private PlayState _currentState = PlayState.EditMode;
        private EditorTab _activeTab = EditorTab.Viewport;

        // --- Core Scene ---
        private readonly Scene _scene = new Scene();
        private ProwlNode? _selectedNode;

        // --- Script Editor State ---
        private string _activeScriptCode = "";
        private string _activeScriptName = "NewScript.cs";

        // --- Touch & Joystick Navigation ---
        private Vector2 _leftTouchStart, _leftTouchCurrent;
        private bool _isLeftTouching = false;
        private float _rightTouchLastX, _rightTouchLastY;
        private bool _isRightTouching = false;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try { AssetExtractor.EnsureAssetsExtracted(this); } catch { }

            InitSceneNodes();
        }

        private void InitSceneNodes()
        {
            // 1. 3D Humanoid Hero Node
            var hero = _scene.CreateNode("Hero_Character");
            hero.Transform.Position = new Vector3(0, 0, 0);
            var mesh = hero.AddComponent<MeshRendererComponent>();
            mesh.Shape = MeshShape.Humanoid;
            mesh.Color = new Vector3(0.18f, 0.55f, 0.95f);
            hero.AddComponent<RigidBodyComponent>();
            hero.AddComponent<BoxColliderComponent>();
            hero.AttachScript(new PlayerControllerScript());

            // 2. Main Camera Node
            var cam = _scene.CreateNode("Main_Camera");
            cam.AddComponent<CameraComponent>();
            var camScript = cam.AttachScript(new OrbitCameraScript()) as OrbitCameraScript;
            if (camScript != null) camScript.Target = hero;

            // 3. Directional Sun Light
            var sun = _scene.CreateNode("Directional_Sun");
            var sunLight = sun.AddComponent<LightComponent>();
            sunLight.Type = LightType.Directional;
            sunLight.Color = new Vector3(1.0f, 0.95f, 0.8f);

            // 4. Particle Emitter Node
            var particles = _scene.CreateNode("Magic_ParticleEmitter");
            particles.Transform.Position = new Vector3(2.5f, 1.0f, 0);
            particles.AddComponent<ParticleSystemComponent>();
            particles.AttachScript(new ParticlePulseScript());

            // 5. Point Light Torch
            var torch = _scene.CreateNode("PointLight_Torch");
            torch.Transform.Position = new Vector3(-3.0f, 1.5f, 1.5f);
            var torchLight = torch.AddComponent<LightComponent>();
            torchLight.Type = LightType.Point;
            torchLight.Color = new Vector3(1.0f, 0.4f, 0.1f);
            torch.AttachScript(new LightFlickerScript());

            _selectedNode = hero;
            UpdateScriptEditorView();
            _scene.Start();
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
            _gl = _view?.CreateOpenGLES();
            if (_gl == null || _view == null) return;

            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);

            InitShaders();
            BuildMeshes();
            BuildGrid();
        }

        private void InitShaders()
        {
            if (_gl == null) return;

            string vs = @"#version 300 es
            layout(location = 0) in vec3 aPos;
            layout(location = 1) in vec3 aNorm;
            layout(location = 2) in vec3 aCol;

            uniform mat4 uModel;
            uniform mat4 uView;
            uniform mat4 uProj;

            out vec3 vNorm;
            out vec3 vCol;

            void main() {
                vNorm = mat3(transpose(inverse(uModel))) * aNorm;
                vCol = aCol;
                gl_Position = uProj * uView * uModel * vec4(aPos, 1.0);
            }";

            string fs = @"#version 300 es
            precision mediump float;
            in vec3 vNorm;
            in vec3 vCol;
            out vec4 FragColor;

            void main() {
                vec3 norm = normalize(vNorm);
                vec3 lightDir = normalize(vec3(0.5, 1.0, 0.4));
                float diff = max(dot(norm, lightDir), 0.25);
                FragColor = vec4((diff + 0.35) * vCol, 1.0);
            }";

            uint vsObj = _gl.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(vsObj, vs);
            _gl.CompileShader(vsObj);

            uint fsObj = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(fsObj, fs);
            _gl.CompileShader(fsObj);

            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, vsObj);
            _gl.AttachShader(_shaderProgram, fsObj);
            _gl.LinkProgram(_shaderProgram);

            _gl.DeleteShader(vsObj);
            _gl.DeleteShader(fsObj);
        }

        private unsafe void BuildMeshes()
        {
            if (_gl == null) return;

            // 1. Humanoid Mesh
            List<float> v = new List<float>();
            void AddBox(Vector3 c, Vector3 s, Vector3 col)
            {
                float x = s.X / 2f, y = s.Y / 2f, z = s.Z / 2f;
                float[] r = {
                    c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,
                    c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,
                    c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.8f,col.Y*0.8f,col.Z*0.8f,
                    c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X+x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.8f,col.Y*0.8f,col.Z*0.8f,
                    c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X-x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,
                    c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X+x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,
                    c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.6f,col.Y*0.6f,col.Z*0.6f,  c.X+x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.6f,col.Y*0.6f,col.Z*0.6f,  c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.6f,col.Y*0.6f,col.Z*0.6f,
                    c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.6f,col.Y*0.6f,col.Z*0.6f,  c.X-x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.6f,col.Y*0.6f,col.Z*0.6f,  c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.6f,col.Y*0.6f,col.Z*0.6f,
                    c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y-y, c.Z+z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,
                    c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y+y, c.Z-z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,
                    c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
                    c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
                };
                v.AddRange(r);
            }

            // Head, Torso, Arms, Legs
            AddBox(new Vector3(0, 1.9f, 0), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1.0f, 0.82f, 0.65f));
            AddBox(new Vector3(0, 1.15f, 0), new Vector3(0.7f, 0.9f, 0.45f), new Vector3(0.18f, 0.55f, 0.95f));
            AddBox(new Vector3(-0.55f, 1.15f, 0), new Vector3(0.3f, 0.8f, 0.3f), new Vector3(0.9f, 0.75f, 0.2f));
            AddBox(new Vector3(0.55f, 1.15f, 0), new Vector3(0.3f, 0.8f, 0.3f), new Vector3(0.9f, 0.75f, 0.2f));
            AddBox(new Vector3(-0.2f, 0.38f, 0), new Vector3(0.28f, 0.75f, 0.35f), new Vector3(0.2f, 0.22f, 0.35f));
            AddBox(new Vector3(0.2f, 0.38f, 0), new Vector3(0.28f, 0.75f, 0.35f), new Vector3(0.2f, 0.22f, 0.35f));

            _humanoidVertCount = v.Count / 9;
            _vaoHumanoid = _gl.GenVertexArray();
            _vboHumanoid = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoHumanoid);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboHumanoid);
            fixed (float* p = v.ToArray())
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(v.Count * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
            uint stride = 9 * sizeof(float);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
        }

        private unsafe void BuildGrid()
        {
            if (_gl == null) return;
            List<float> lines = new List<float>();
            int r = 16;
            for (int i = -r; i <= r; i++)
            {
                Vector3 col = (i == 0) ? new Vector3(0.85f, 0.35f, 0.35f) : new Vector3(0.22f, 0.26f, 0.34f);
                lines.AddRange(new[] { (float)i, 0f, -r, 0f, 1f, 0f, col.X, col.Y, col.Z });
                lines.AddRange(new[] { (float)i, 0f,  r, 0f, 1f, 0f, col.X, col.Y, col.Z });
                lines.AddRange(new[] { -r, 0f, (float)i, 0f, 1f, 0f, col.X, col.Y, col.Z });
                lines.AddRange(new[] {  r, 0f, (float)i, 0f, 1f, 0f, col.X, col.Y, col.Z });
            }

            _gridVertCount = lines.Count / 9;
            _vaoGrid = _gl.GenVertexArray();
            _vboGrid = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoGrid);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboGrid);
            fixed (float* p = lines.ToArray())
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(lines.Count * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
            uint stride = 9 * sizeof(float);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
        }

        private void OnResize(Vector2D<int> size)
        {
            if (_gl != null)
                _gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        }

        private void OnUpdate(double delta)
        {
            float dt = (float)delta;

            // Touch Joystick Input
            Vector2 joy = Vector2.Zero;
            if (_isLeftTouching)
            {
                Vector2 diff = _leftTouchCurrent - _leftTouchStart;
                if (diff.Length() > 10f)
                    joy = Vector2.Normalize(diff) * Math.Clamp(diff.Length() / 80f, 0f, 1f);
            }

            _scene.Update(dt, joy, _currentState == PlayState.PlayMode);
        }

        private unsafe void OnRender(double delta)
        {
            if (_gl == null || _view == null) return;

            // Studio Dark Background
            _gl.ClearColor(0.08f, 0.10f, 0.14f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _gl.UseProgram(_shaderProgram);

            // 3D Perspective Projection & View
            float aspect = (float)_view.Size.X / Math.Max(1, _view.Size.Y);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.5f, aspect, 0.1f, 100.0f);
            var cam = _scene.FindComponent<CameraComponent>();
            var view = cam != null ? cam.GetViewMatrix() : Matrix4x4.CreateLookAt(new Vector3(0, 4, -7), Vector3.Zero, Vector3.UnitY);

            int locProj = _gl.GetUniformLocation(_shaderProgram, "uProj");
            int locView = _gl.GetUniformLocation(_shaderProgram, "uView");
            int locModel = _gl.GetUniformLocation(_shaderProgram, "uModel");

            _gl.UniformMatrix4(locProj, 1, false, (float*)&proj);
            _gl.UniformMatrix4(locView, 1, false, (float*)&view);

            // Draw 3D Grid
            var gridMat = Matrix4x4.Identity;
            _gl.UniformMatrix4(locModel, 1, false, (float*)&gridMat);
            _gl.BindVertexArray(_vaoGrid);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertCount);

            // Draw Scene Nodes (Meshes & Characters)
            foreach (var node in _scene.Nodes)
            {
                var mesh = node.GetComponent<MeshRendererComponent>();
                if (mesh != null && mesh.Shape == MeshShape.Humanoid)
                {
                    var model = node.Transform.GetWorldMatrix();
                    _gl.UniformMatrix4(locModel, 1, false, (float*)&model);
                    _gl.BindVertexArray(_vaoHumanoid);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_humanoidVertCount);
                }
            }
        }

        // ==========================================================
        // DUAL TOUCH & SCRIPT EDITOR ACTIONS
        // ==========================================================
        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null || _view == null) return base.OnTouchEvent(e);

            int halfX = _view.Size.X / 2;

            for (int i = 0; i < e.PointerCount; i++)
            {
                float x = e.GetX(i);
                float y = e.GetY(i);

                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                    case MotionEventActions.PointerDown:
                        // Top Bar Action Buttons Tap
                        if (y < 120)
                        {
                            HandleTopBarTap(x);
                            return true;
                        }

                        if (x < halfX)
                        {
                            _isLeftTouching = true;
                            _leftTouchStart = new Vector2(x, y);
                            _leftTouchCurrent = _leftTouchStart;
                        }
                        else
                        {
                            _isRightTouching = true;
                            _rightTouchLastX = x;
                            _rightTouchLastY = y;
                        }
                        break;

                    case MotionEventActions.Move:
                        if (_isLeftTouching && x < halfX)
                            _leftTouchCurrent = new Vector2(x, y);

                        if (_isRightTouching && x >= halfX)
                        {
                            float dx = x - _rightTouchLastX;
                            float dy = y - _rightTouchLastY;

                            var orbit = _scene.FindScript<OrbitCameraScript>();
                            if (orbit != null)
                            {
                                orbit.Yaw += dx * 0.35f;
                                orbit.Pitch = Math.Clamp(orbit.Pitch - dy * 0.35f, 5.0f, 85.0f);
                            }

                            _rightTouchLastX = x;
                            _rightTouchLastY = y;
                        }
                        break;

                    case MotionEventActions.Up:
                    case MotionEventActions.PointerUp:
                    case MotionEventActions.Cancel:
                        if (x < halfX) _isLeftTouching = false;
                        else _isRightTouching = false;
                        break;
                }
            }
            return true;
        }

        private void HandleTopBarTap(float touchX)
        {
            if (_view == null) return;
            float btnW = _view.Size.X / 4f;

            if (touchX < btnW)
            {
                // Toggle Play / Edit Mode
                _currentState = (_currentState == PlayState.EditMode) ? PlayState.PlayMode : PlayState.EditMode;
                Log.Info(LogTag, $"Mode Switched: {_currentState}");
            }
            else if (touchX < btnW * 2)
            {
                // Add New 3D Node
                var newNode = _scene.CreateNode($"Node_{_scene.Nodes.Count + 1}");
                newNode.Transform.Position = new Vector3((_scene.Nodes.Count % 4) * 2f - 3f, 0, 2f);
                var m = newNode.AddComponent<MeshRendererComponent>();
                m.Shape = MeshShape.Humanoid;
                newNode.AttachScript(new RotatorScript());
                _selectedNode = newNode;
                UpdateScriptEditorView();
                Log.Info(LogTag, $"Added Node: {newNode.Name}");
            }
            else if (touchX < btnW * 3)
            {
                // Attach New C# Script to Selected Node
                if (_selectedNode != null)
                {
                    var newScript = new RotatorScript { RotationSpeed = 90f };
                    _selectedNode.AttachScript(newScript);
                    UpdateScriptEditorView();
                    Log.Info(LogTag, $"Attached C# Script to {_selectedNode.Name}");
                }
            }
            else
            {
                // Cycle Selection
                int idx = _scene.Nodes.IndexOf(_selectedNode!);
                idx = (idx + 1) % _scene.Nodes.Count;
                _selectedNode = _scene.Nodes[idx];
                UpdateScriptEditorView();
                Log.Info(LogTag, $"Selected Node: {_selectedNode.Name}");
            }
        }

        private void UpdateScriptEditorView()
        {
            if (_selectedNode == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"// ==========================================");
            sb.AppendLine($"// Prowl C# Script: {_selectedNode.Name}.cs");
            sb.AppendLine($"// ==========================================");
            sb.AppendLine("using Prowl.Runtime;\n");
            sb.AppendLine($"public class {_selectedNode.Name.Replace(" ", "_")} : MonoBehaviour");
            sb.AppendLine("{");
            sb.AppendLine($"    // Position: {_selectedNode.Transform.Position}");
            sb.AppendLine($"    // Attached Components: {_selectedNode.Components.Count}");
            sb.AppendLine($"    // Attached Scripts: {_selectedNode.Scripts.Count}\n");
            sb.AppendLine("    public override void Update(float deltaTime)");
            sb.AppendLine("    {");
            sb.AppendLine("        // Custom game logic running on Android");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            _activeScriptCode = sb.ToString();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _gl?.DeleteProgram(_shaderProgram);
            _gl?.Dispose();
            _view?.Dispose();
        }
    }

    // =========================================================================
    // PROWL ENGINE ARCHITECTURE: SCENE, NODES, COMPONENTS & SCRIPTING
    // =========================================================================

    public class Scene
    {
        public List<ProwlNode> Nodes { get; } = new List<ProwlNode>();

        public ProwlNode CreateNode(string name)
        {
            var n = new ProwlNode(this, name);
            Nodes.Add(n);
            return n;
        }

        public void Start()
        {
            foreach (var n in Nodes) n.Start();
        }

        public void Update(float dt, Vector2 joy, bool isPlaying)
        {
            foreach (var n in Nodes) n.Update(dt, joy, isPlaying);
        }

        public T? FindComponent<T>() where T : Component
        {
            foreach (var n in Nodes)
            {
                var c = n.GetComponent<T>();
                if (c != null) return c;
            }
            return null;
        }

        public T? FindScript<T>() where T : MonoBehaviour
        {
            foreach (var n in Nodes)
            {
                foreach (var s in n.Scripts)
                    if (s is T match) return match;
            }
            return null;
        }
    }

    public class ProwlNode
    {
        public string Name { get; set; }
        public Scene Scene { get; }
        public Transform Transform { get; }
        public List<Component> Components { get; } = new List<Component>();
        public List<MonoBehaviour> Scripts { get; } = new List<MonoBehaviour>();

        public ProwlNode(Scene s, string name)
        {
            Scene = s;
            Name = name;
            Transform = new Transform(this);
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { Node = this };
            Components.Add(c);
            c.Awake();
            return c;
        }

        public T? GetComponent<T>() where T : Component
        {
            foreach (var c in Components) if (c is T m) return m;
            return null;
        }

        public MonoBehaviour AttachScript(MonoBehaviour script)
        {
            script.Node = this;
            Scripts.Add(script);
            script.Awake();
            script.Start();
            return script;
        }

        public void Start()
        {
            foreach (var c in Components) c.Start();
            foreach (var s in Scripts) s.Start();
        }

        public void Update(float dt, Vector2 joy, bool isPlaying)
        {
            foreach (var c in Components) c.Update(dt);
            foreach (var s in Scripts)
            {
                if (isPlaying || s is OrbitCameraScript)
                    s.UpdateWithInput(dt, joy);
            }
        }
    }

    public class Transform
    {
        public ProwlNode Node { get; }
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Rotation { get; set; } = Vector3.Zero;
        public Vector3 Scale { get; set; } = Vector3.One;

        public Transform(ProwlNode n) { Node = n; }

        public void Translate(Vector3 offset) => Position += offset;
        public void Rotate(Vector3 rot) => Rotation += rot;

        public Matrix4x4 GetWorldMatrix()
        {
            return Matrix4x4.CreateScale(Scale) *
                   Matrix4x4.CreateFromYawPitchRoll(Rotation.Y * MathF.PI / 180f, Rotation.X * MathF.PI / 180f, Rotation.Z * MathF.PI / 180f) *
                   Matrix4x4.CreateTranslation(Position);
        }
    }

    public abstract class Component
    {
        public ProwlNode? Node { get; set; }
        public Transform Transform => Node!.Transform;
        public virtual void Awake() { }
        public virtual void Start() { }
        public virtual void Update(float dt) { }
    }

    // =========================================================================
    // PROWL MONOBEHAVIOUR SCRIPTING BASE
    // =========================================================================

    public abstract class MonoBehaviour : Component
    {
        public bool Enabled { get; set; } = true;
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
        public virtual void FixedUpdate() { }
        public virtual void LateUpdate() { }
        public virtual void OnCollisionEnter(ProwlNode other) { }
        public virtual void UpdateWithInput(float dt, Vector2 joystickInput) => Update(dt);
    }

    // =========================================================================
    // BUILT-IN PROWL ENGINE COMPONENTS
    // =========================================================================

    public enum MeshShape { Humanoid, Cube, Sphere, Cylinder, Capsule, CustomObj }
    public class MeshRendererComponent : Component
    {
        public MeshShape Shape { get; set; } = MeshShape.Humanoid;
        public Vector3 Color { get; set; } = Vector3.One;
        public bool CastShadows { get; set; } = true;
    }

    public enum LightType { Directional, Point, Spot }
    public class LightComponent : Component
    {
        public LightType Type { get; set; } = LightType.Directional;
        public Vector3 Color { get; set; } = Vector3.One;
        public float Intensity { get; set; } = 1.0f;
        public float Range { get; set; } = 10.0f;
    }

    public class CameraComponent : Component
    {
        public float FieldOfView { get; set; } = 60.0f;
        public float NearClip { get; set; } = 0.1f;
        public float FarClip { get; set; } = 100.0f;

        public Matrix4x4 GetViewMatrix()
        {
            return Matrix4x4.CreateLookAt(Transform.Position, Transform.Position + new Vector3(0, 0, 1), Vector3.UnitY);
        }
    }

    public class RigidBodyComponent : Component
    {
        public float Mass { get; set; } = 1.0f;
        public bool UseGravity { get; set; } = true;
        public Vector3 Velocity { get; set; } = Vector3.Zero;
    }

    public class BoxColliderComponent : Component
    {
        public Vector3 Size { get; set; } = Vector3.One;
    }

    public class ParticleSystemComponent : Component
    {
        public int ParticleCount { get; set; } = 50;
        public float EmissionRate { get; set; } = 10f;
        public Vector3 ParticleColor { get; set; } = new Vector3(1f, 0.8f, 0.2f);
    }

    // =========================================================================
    // BUILT-IN C# SCRIPT LIBRARY
    // =========================================================================

    // 1. Player Movement Controller Script
    public class PlayerControllerScript : MonoBehaviour
    {
        public float Speed = 4.0f;
        private float _walkTimer = 0f;

        public override void UpdateWithInput(float dt, Vector2 joy)
        {
            if (joy.LengthSquared() > 0.01f)
            {
                Vector3 dir = new Vector3(joy.X, 0, joy.Y);
                Transform.Translate(dir * Speed * dt);

                float yaw = MathF.Atan2(dir.X, dir.Z) * (180f / MathF.PI);
                Transform.Rotation = new Vector3(0, yaw, 0);

                _walkTimer += dt * 8.0f;
                Transform.Position = new Vector3(Transform.Position.X, MathF.Abs(MathF.Sin(_walkTimer)) * 0.12f, Transform.Position.Z);
            }
            else
            {
                Transform.Position = new Vector3(Transform.Position.X, 0, Transform.Position.Z);
            }
        }
    }

    // 2. 3rd-Person Orbit Follow Camera Script
    public class OrbitCameraScript : MonoBehaviour
    {
        public ProwlNode? Target { get; set; }
        public float Distance = 6.5f;
        public float Yaw = 35.0f;
        public float Pitch = 25.0f;

        public override void Update(float dt)
        {
            if (Target == null) return;
            float radY = Yaw * MathF.PI / 180f;
            float radP = Pitch * MathF.PI / 180f;

            float ox = Distance * MathF.Cos(radP) * MathF.Sin(radY);
            float oy = Distance * MathF.Sin(radP);
            float oz = Distance * MathF.Cos(radP) * MathF.Cos(radY);

            Transform.Position = Target.Transform.Position + new Vector3(ox, oy + 1.2f, oz);
        }
    }

    // 3. Continuous Object Rotator Script
    public class RotatorScript : MonoBehaviour
    {
        public float RotationSpeed = 45.0f;

        public override void Update(float dt)
        {
            Transform.Rotate(new Vector3(0, RotationSpeed * dt, 0));
        }
    }

    // 4. Torch Light Dynamic Flicker Script
    public class LightFlickerScript : MonoBehaviour
    {
        private float _t = 0;
        public override void Update(float dt)
        {
            _t += dt * 14.0f;
            var light = Node?.GetComponent<LightComponent>();
            if (light != null) light.Intensity = 1.0f + MathF.Sin(_t) * 0.35f;
        }
    }

    // 5. Particle System Pulse Script
    public class ParticlePulseScript : MonoBehaviour
    {
        private float _t = 0;
        public override void Update(float dt)
        {
            _t += dt * 3.0f;
            Transform.Position = new Vector3(Transform.Position.X, 1.0f + MathF.Sin(_t) * 0.4f, Transform.Position.Z);
        }
    }
}
