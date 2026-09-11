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

        // --- 3D Shaders & Mesh Buffers ---
        private uint _shaderProgram;
        private uint _uiProgram;
        private uint _vaoHumanoid, _vboHumanoid;
        private int _humanoidVertCount;
        private uint _vaoGrid, _vboGrid;
        private int _gridVertCount;
        private uint _vaoUI, _vboUI;

        // --- Engine & Editor State ---
        public enum PlayState { EditMode, PlayMode }
        private PlayState _currentState = PlayState.EditMode;
        private bool _showHierarchy = true;
        private bool _showInspector = true;

        // --- Core Scene ---
        private readonly Scene _scene = new Scene();
        private ProwlNode? _selectedNode;
        private ProwlNode? _heroNode;

        // --- Touch & Joystick Navigation ---
        private Vector2 _leftTouchStart, _leftTouchCurrent;
        private bool _isLeftTouching = false;
        private float _rightTouchLastX, _rightTouchLastY;
        private bool _isRightTouching = false;

        // --- Camera Angles ---
        private float _camYaw = 35.0f;
        private float _camPitch = 22.0f;
        private float _camDistance = 6.0f;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try { AssetExtractor.EnsureAssetsExtracted(this); } catch { }

            InitSceneNodes();
        }

        private void InitSceneNodes()
        {
            // 1. 3D HERO CHARACTER (Gitna ng mundo)
            _heroNode = _scene.CreateNode("Hero_Character");
            _heroNode.Transform.Position = new Vector3(0, 0, 0);
            var mesh = _heroNode.AddComponent<MeshRendererComponent>();
            mesh.Shape = MeshShape.Humanoid;
            mesh.Color = new Vector3(0.18f, 0.55f, 0.95f);
            _heroNode.AddComponent<RigidBodyComponent>();
            _heroNode.AddComponent<BoxColliderComponent>();
            _heroNode.AttachScript(new PlayerControllerScript());

            // 2. Main Camera Node
            var cam = _scene.CreateNode("Main_Camera");
            cam.AddComponent<CameraComponent>();

            // 3. Directional Sun Light
            var sun = _scene.CreateNode("Directional_Sun");
            var sunLight = sun.AddComponent<LightComponent>();
            sunLight.Type = LightType.Directional;
            sunLight.Color = new Vector3(1.0f, 0.95f, 0.8f);

            // 4. Magic Particle Emitter
            var particles = _scene.CreateNode("Magic_Particles");
            particles.Transform.Position = new Vector3(2.5f, 1.0f, 0);
            particles.AddComponent<ParticleSystemComponent>();

            // 5. Point Light Torch
            var torch = _scene.CreateNode("PointLight_Torch");
            torch.Transform.Position = new Vector3(-2.5f, 1.2f, 1.5f);
            var torchLight = torch.AddComponent<LightComponent>();
            torchLight.Type = LightType.Point;
            torchLight.Color = new Vector3(1.0f, 0.45f, 0.1f);

            _selectedNode = _heroNode;
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

            Init3DShaders();
            InitUIShaders();
            Build3DHumanoidMesh();
            Build3DGrid();
            BuildUIBuffers();
        }

        private void Init3DShaders()
        {
            if (_gl == null) return;

            // Inayos ang Matrix Multiplication para sa System.Numerics (v * M)
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
                vNorm = (vec4(aNorm, 0.0) * uModel).xyz;
                vCol = aCol;
                gl_Position = vec4(aPos, 1.0) * uModel * uView * uProj;
            }";

            string fs = @"#version 300 es
            precision mediump float;
            in vec3 vNorm;
            in vec3 vCol;
            out vec4 FragColor;

            void main() {
                vec3 norm = normalize(vNorm);
                vec3 lightDir = normalize(vec3(0.5, 1.0, 0.4));
                float diff = max(dot(norm, lightDir), 0.0);
                vec3 ambient = vec3(0.38);
                vec3 lighting = (ambient + diff * 0.72) * vCol;
                FragColor = vec4(lighting, 1.0);
            }";

            _shaderProgram = CreateProgram(vs, fs);
        }

        private void InitUIShaders()
        {
            if (_gl == null) return;

            // Inayos ang Orthographic projection multiplication
            string vs = @"#version 300 es
            layout(location = 0) in vec2 aPos;
            layout(location = 1) in vec4 aCol;

            uniform mat4 uOrtho;
            out vec4 vCol;

            void main() {
                vCol = aCol;
                gl_Position = vec4(aPos, 0.0, 1.0) * uOrtho;
            }";

            string fs = @"#version 300 es
            precision mediump float;
            in vec4 vCol;
            out vec4 FragColor;

            void main() {
                FragColor = vCol;
            }";

            _uiProgram = CreateProgram(vs, fs);
        }

        private uint CreateProgram(string vs, string fs)
        {
            uint v = _gl!.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(v, vs);
            _gl.CompileShader(v);

            uint f = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(f, fs);
            _gl.CompileShader(f);

            uint prog = _gl.CreateProgram();
            _gl.AttachShader(prog, v);
            _gl.AttachShader(prog, f);
            _gl.LinkProgram(prog);

            _gl.DeleteShader(v);
            _gl.DeleteShader(f);
            return prog;
        }

        private unsafe void Build3DHumanoidMesh()
        {
            if (_gl == null) return;

            List<float> v = new List<float>();
            void AddBox(Vector3 c, Vector3 s, Vector3 col)
            {
                float x = s.X / 2f, y = s.Y / 2f, z = s.Z / 2f;
                float[] r = {
                    // Front
                    c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,
                    c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,
                    // Back
                    c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.75f,col.Y*0.75f,col.Z*0.75f,  c.X-x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.75f,col.Y*0.75f,col.Z*0.75f,  c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.75f,col.Y*0.75f,col.Z*0.75f,
                    c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.75f,col.Y*0.75f,col.Z*0.75f,  c.X+x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.75f,col.Y*0.75f,col.Z*0.75f,  c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.75f,col.Y*0.75f,col.Z*0.75f,
                    // Top
                    c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X-x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,
                    c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X+x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,  c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.1f,col.Y*1.1f,col.Z*1.1f,
                    // Bottom
                    c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X+x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,
                    c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X-x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,
                    // Left
                    c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y-y, c.Z+z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,
                    c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y+y, c.Z-z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.7f,col.Y*0.7f,col.Z*0.7f,
                    // Right
                    c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
                    c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
                };
                v.AddRange(r);
            }

            // Head, Torso, Gold Armor Arms, Legs
            AddBox(new Vector3(0, 1.95f, 0), new Vector3(0.55f, 0.55f, 0.55f), new Vector3(1.0f, 0.82f, 0.65f));
            AddBox(new Vector3(0, 1.15f, 0), new Vector3(0.75f, 0.95f, 0.5f), new Vector3(0.18f, 0.55f, 0.95f));
            AddBox(new Vector3(-0.6f, 1.15f, 0), new Vector3(0.32f, 0.85f, 0.32f), new Vector3(0.95f, 0.75f, 0.2f));
            AddBox(new Vector3(0.6f, 1.15f, 0), new Vector3(0.32f, 0.85f, 0.32f), new Vector3(0.95f, 0.75f, 0.2f));
            AddBox(new Vector3(-0.22f, 0.38f, 0), new Vector3(0.3f, 0.8f, 0.35f), new Vector3(0.2f, 0.22f, 0.35f));
            AddBox(new Vector3(0.22f, 0.38f, 0), new Vector3(0.3f, 0.8f, 0.35f), new Vector3(0.2f, 0.22f, 0.35f));

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

        private unsafe void Build3DGrid()
        {
            if (_gl == null) return;
            List<float> lines = new List<float>();
            int r = 16;
            for (int i = -r; i <= r; i++)
            {
                Vector3 col = (i == 0) ? new Vector3(0.85f, 0.35f, 0.35f) : new Vector3(0.25f, 0.30f, 0.40f);
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

        private void BuildUIBuffers()
        {
            if (_gl == null) return;
            _vaoUI = _gl.GenVertexArray();
            _vboUI = _gl.GenBuffer();
        }

        private void OnResize(Vector2D<int> size)
        {
            if (_gl != null)
                _gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        }

        private void OnUpdate(double delta)
        {
            float dt = (float)delta;

            // Touch Joystick Vector
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

            // 1. Studio Dark Slate Viewport Background
            _gl.ClearColor(0.11f, 0.13f, 0.17f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // ==========================================
            // 2. 3D WORLD PASS (Character & Grid)
            // ==========================================
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.Disable(EnableCap.Blend);
            _gl.Disable(EnableCap.CullFace);

            _gl.UseProgram(_shaderProgram);

            float aspect = (float)_view.Size.X / Math.Max(1, _view.Size.Y);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.2f, aspect, 0.1f, 100.0f);

            // Camera Orbit Position nakatutok sa Hero Character
            Vector3 targetPos = _heroNode?.Transform.Position ?? Vector3.Zero;
            float radY = _camYaw * MathF.PI / 180f;
            float radP = _camPitch * MathF.PI / 180f;

            float camX = targetPos.X + _camDistance * MathF.Cos(radP) * MathF.Sin(radY);
            float camY = targetPos.Y + _camDistance * MathF.Sin(radP) + 1.2f;
            float camZ = targetPos.Z + _camDistance * MathF.Cos(radP) * MathF.Cos(radY);

            var view = Matrix4x4.CreateLookAt(new Vector3(camX, camY, camZ), targetPos + new Vector3(0, 1.0f, 0), Vector3.UnitY);

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

            // Draw 3D Humanoid Character Nodes
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

            // ==========================================
            // 3. 2D EDITOR GUI OVERLAY PASS
            // ==========================================
            _gl.Disable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _gl.UseProgram(_uiProgram);
            var ortho = Matrix4x4.CreateOrthographicOffCenter(0, _view.Size.X, _view.Size.Y, 0, -1f, 1f);
            int locOrtho = _gl.GetUniformLocation(_uiProgram, "uOrtho");
            _gl.UniformMatrix4(locOrtho, 1, false, (float*)&ortho);

            RenderEditorUI();
        }

        private unsafe void RenderEditorUI()
        {
            if (_gl == null || _view == null) return;

            List<float> uiVerts = new List<float>();

            void DrawRect(float x, float y, float w, float h, Vector4 col)
            {
                float[] r = {
                    x, y,       col.X, col.Y, col.Z, col.W,
                    x+w, y,     col.X, col.Y, col.Z, col.W,
                    x+w, y+h,   col.X, col.Y, col.Z, col.W,
                    x+w, y+h,   col.X, col.Y, col.Z, col.W,
                    x, y+h,     col.X, col.Y, col.Z, col.W,
                    x, y,       col.X, col.Y, col.Z, col.W,
                };
                uiVerts.AddRange(r);
            }

            float screenW = _view.Size.X;
            float screenH = _view.Size.Y;

            // 1. TOP TOOLBAR
            DrawRect(0, 0, screenW, 100, new Vector4(0.07f, 0.09f, 0.13f, 0.92f));
            // Button 1: Mode Toggle
            Vector4 modeCol = (_currentState == PlayState.PlayMode) ? new Vector4(0.18f, 0.80f, 0.44f, 1f) : new Vector4(0.95f, 0.55f, 0.15f, 1f);
            DrawRect(15, 15, 200, 70, modeCol);
            // Button 2: Add Node (Cyan Blue)
            DrawRect(230, 15, 190, 70, new Vector4(0.18f, 0.55f, 0.95f, 0.9f));
            // Button 3: Attach Script (Purple)
            DrawRect(435, 15, 200, 70, new Vector4(0.65f, 0.35f, 0.95f, 0.9f));
            // Button 4: Selected Node Info Chip
            DrawRect(650, 15, 260, 70, new Vector4(0.14f, 0.18f, 0.25f, 0.95f));

            // 2. LEFT HIERARCHY DRAWER
            if (_showHierarchy)
            {
                DrawRect(15, 120, 280, 360, new Vector4(0.08f, 0.10f, 0.15f, 0.85f));
                // Header
                DrawRect(15, 120, 280, 45, new Vector4(0.14f, 0.18f, 0.26f, 0.95f));
                // Node Items Chips
                for (int i = 0; i < Math.Min(5, _scene.Nodes.Count); i++)
                {
                    Vector4 itemCol = (_scene.Nodes[i] == _selectedNode) ? new Vector4(0.18f, 0.55f, 0.95f, 0.80f) : new Vector4(0.15f, 0.19f, 0.26f, 0.65f);
                    DrawRect(25, 175 + (i * 55), 260, 45, itemCol);
                }
            }

            // 3. RIGHT INSPECTOR CARD
            if (_showInspector)
            {
                float inspX = screenW - 320;
                DrawRect(inspX, 120, 305, 420, new Vector4(0.08f, 0.10f, 0.15f, 0.85f));
                // Header
                DrawRect(inspX, 120, 305, 45, new Vector4(0.14f, 0.18f, 0.26f, 0.95f));
                // Transform Section
                DrawRect(inspX + 10, 175, 285, 100, new Vector4(0.15f, 0.19f, 0.26f, 0.65f));
                // Components Section
                DrawRect(inspX + 10, 285, 285, 115, new Vector4(0.15f, 0.19f, 0.26f, 0.65f));
                // Attached Script Section (Gold Accent)
                DrawRect(inspX + 10, 410, 285, 110, new Vector4(0.38f, 0.32f, 0.15f, 0.75f));
            }

            // 4. TOUCH JOYSTICK VISUAL
            if (_isLeftTouching)
            {
                // Outer ring
                DrawRect(_leftTouchStart.X - 60, _leftTouchStart.Y - 60, 120, 120, new Vector4(1f, 1f, 1f, 0.25f));
                // Inner knob
                DrawRect(_leftTouchCurrent.X - 25, _leftTouchCurrent.Y - 25, 50, 50, new Vector4(0.2f, 0.65f, 1.0f, 0.90f));
            }
            else
            {
                // Idle Joystick Hint (Kaliwang Ibaba)
                DrawRect(50, screenH - 170, 120, 120, new Vector4(1f, 1f, 1f, 0.12f));
                DrawRect(85, screenH - 135, 50, 50, new Vector4(0.2f, 0.65f, 1.0f, 0.35f));
            }

            // Upload and Draw 2D UI
            _gl.BindVertexArray(_vaoUI);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboUI);
            fixed (float* p = uiVerts.ToArray())
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(uiVerts.Count * sizeof(float)), p, BufferUsageARB.DynamicDraw);
            }
            uint stride = 6 * sizeof(float);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(uiVerts.Count / 6));
        }

        // ==========================================================
        // DUAL TOUCH & INTERACTIVE ACTIONS
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
                        if (y < 110)
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

                            _camYaw += dx * 0.35f;
                            _camPitch = Math.Clamp(_camPitch - dy * 0.35f, 5.0f, 85.0f);

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
            if (touchX < 220)
            {
                // Mode Toggle: EDIT / PLAY
                _currentState = (_currentState == PlayState.EditMode) ? PlayState.PlayMode : PlayState.EditMode;
                Log.Info(LogTag, $"Mode: {_currentState}");
            }
            else if (touchX < 425)
            {
                // Add New 3D Node
                var newNode = _scene.CreateNode($"Char_{_scene.Nodes.Count + 1}");
                newNode.Transform.Position = new Vector3((_scene.Nodes.Count % 4) * 2f - 3f, 0, 1.5f);
                var m = newNode.AddComponent<MeshRendererComponent>();
                m.Shape = MeshShape.Humanoid;
                newNode.AttachScript(new RotatorScript());
                _selectedNode = newNode;
                Log.Info(LogTag, $"Added: {newNode.Name}");
            }
            else if (touchX < 640)
            {
                // Attach Script
                if (_selectedNode != null)
                {
                    _selectedNode.AttachScript(new RotatorScript { RotationSpeed = 90f });
                    Log.Info(LogTag, $"Script Attached to {_selectedNode.Name}");
                }
            }
            else
            {
                // Cycle Select Node
                int idx = _scene.Nodes.IndexOf(_selectedNode!);
                idx = (idx + 1) % _scene.Nodes.Count;
                _selectedNode = _scene.Nodes[idx];
                Log.Info(LogTag, $"Selected: {_selectedNode.Name}");
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _gl?.DeleteProgram(_shaderProgram);
            _gl?.DeleteProgram(_uiProgram);
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
                if (isPlaying)
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

    public abstract class MonoBehaviour : Component
    {
        public virtual void UpdateWithInput(float dt, Vector2 joystickInput) => Update(dt);
    }

    public enum MeshShape { Humanoid, Cube, Sphere }
    public class MeshRendererComponent : Component
    {
        public MeshShape Shape { get; set; } = MeshShape.Humanoid;
        public Vector3 Color { get; set; } = Vector3.One;
    }

    public enum LightType { Directional, Point, Spot }
    public class LightComponent : Component
    {
        public LightType Type { get; set; } = LightType.Directional;
        public Vector3 Color { get; set; } = Vector3.One;
    }

    public class CameraComponent : Component { }
    public class RigidBodyComponent : Component { }
    public class BoxColliderComponent : Component { }
    public class ParticleSystemComponent : Component { }

    // =========================================================================
    // BUILT-IN SCRIPT LIBRARY
    // =========================================================================

    public class PlayerControllerScript : MonoBehaviour
    {
        public float Speed = 4.5f;
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

    public class RotatorScript : MonoBehaviour
    {
        public float RotationSpeed = 45.0f;

        public override void Update(float dt)
        {
            Transform.Rotate(new Vector3(0, RotationSpeed * dt, 0));
        }
    }
}
