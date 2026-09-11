using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Prowl.Runtime;
using Silk.NET.Maths;
using Silk.NET.OpenGLES;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl.Android;
using Color = Android.Graphics.Color;
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
        private const string LogTag = "ProwlStudio";
        private IView? _view;
        private GL? _gl;

        // --- 3D Shaders & Buffers ---
        private uint _standard3DProgram;
        private uint _skyProgram;
        private uint _vaoCube, _vboCube;
        private uint _vaoFloor, _vboFloor;
        private int _floorVertCount;
        private uint _vaoTree, _vboTree;
        private int _treeVertCount;
        private uint _vaoGizmo, _vboGizmo;
        private int _gizmoVertCount;
        private uint _vaoSky, _vboSky;

        // --- Scene & Engine State ---
        public enum PlayState { EditMode, PlayMode }
        private PlayState _currentState = PlayState.EditMode;
        private readonly Scene _scene = new Scene();
        private ProwlNode? _selectedNode;
        private readonly Dictionary<ProwlNode, (Vector3 pos, Vector3 rot, Vector3 scale)> _initialTransforms = new();

        // --- Camera Navigation via Virtual Joysticks ---
        private float _camYaw = 45.0f;
        private float _camPitch = 25.0f;
        private float _camDistance = 7.5f;
        private Vector3 _camTarget = new Vector3(0, 0.5f, 0);

        // Joystick Input States
        private Vector2 _moveJoyVector = Vector2.Zero;  // Left Stick (Move/Walk)
        private Vector2 _lookJoyVector = Vector2.Zero;  // Right Stick (Lingon/Look)
        private float _flyElevation = 0f;               // Up/Down Height

        // --- Native UI Elements ---
        private Button? _btnPlay;
        private TextView? _txtFps;
        private LinearLayout? _layoutHierarchyTree;
        private LinearLayout? _layoutInspectorBody;
        private LinearLayout? _layoutConsoleLog;
        private LinearLayout? _layoutProjectGrid;
        private FrameLayout? _joystickOverlayContainer;
        private LinearLayout? _rightPanel;
        private LinearLayout? _botPanel;
        private bool _isSidebarVisible = true;
        private bool _isBottomDockVisible = true;

        private int _fps = 212;
        private float _fpsTimer = 0f;
        private int _frames = 0;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try { AssetExtractor.EnsureAssetsExtracted(this); } catch { }

            InitProwlScene();
            RunOnUiThread(BuildNativeProwlStudioLayout);
        }

        private void InitProwlScene()
        {
            // 1. Directional Light
            var sun = _scene.CreateNode("Directional Light");
            var sunLight = sun.AddComponent<LightComponent>();
            sunLight.Type = LightType.Directional;
            sun.Transform.Position = new Vector3(0, 4f, 0);

            // 2. Default Active Cube
            var cube = _scene.CreateNode("Cube");
            cube.Transform.Position = new Vector3(-0.79f, 0.5f, -0.13f);
            cube.AddComponent<MeshRendererComponent>().Shape = MeshShape.Cube;

            // 3. Low-Poly Trees
            var tree1 = _scene.CreateNode("Tree_1");
            tree1.Transform.Position = new Vector3(2.5f, 0, 1.8f);
            tree1.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            var tree2 = _scene.CreateNode("Tree_2");
            tree2.Transform.Position = new Vector3(-2.4f, 0, 2.0f);
            tree2.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            var tree3 = _scene.CreateNode("Tree_Small");
            tree3.Transform.Position = new Vector3(1.6f, 0, -1.8f);
            tree3.Transform.Scale = new Vector3(0.7f, 0.7f, 0.7f);
            tree3.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            _selectedNode = cube;
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
            Build3DWorldMeshes();
        }

        private void Init3DShaders()
        {
            if (_gl == null) return;

            string skyVS = @"#version 300 es
            layout(location = 0) in vec2 aPos;
            out vec2 vUV;
            void main() {
                vUV = aPos * 0.5 + 0.5;
                gl_Position = vec4(aPos, 0.9999, 1.0);
            }";

            string skyFS = @"#version 300 es
            precision mediump float;
            in vec2 vUV;
            out vec4 FragColor;
            void main() {
                vec3 skyTop = vec3(0.08, 0.10, 0.14);
                vec3 skyMid = vec3(0.18, 0.22, 0.28);
                vec3 horizonGlow = vec3(0.52, 0.38, 0.22);
                vec3 col = mix(horizonGlow, skyMid, smoothstep(0.12, 0.50, vUV.y));
                col = mix(col, skyTop, smoothstep(0.50, 0.95, vUV.y));
                FragColor = vec4(col, 1.0);
            }";
            _skyProgram = CreateProgram(skyVS, skyFS);

            string litVS = @"#version 300 es
            layout(location = 0) in vec3 aPos;
            layout(location = 1) in vec3 aNorm;
            layout(location = 2) in vec3 aCol;

            uniform mat4 uModel;
            uniform mat4 uView;
            uniform mat4 uProj;

            out vec3 vWorldPos;
            out vec3 vNorm;
            out vec3 vCol;

            void main() {
                vec4 worldPos = uModel * vec4(aPos, 1.0);
                vWorldPos = worldPos.xyz;
                vNorm = mat3(uModel) * aNorm;
                vCol = aCol;
                gl_Position = uProj * uView * worldPos;
            }";

            string litFS = @"#version 300 es
            precision mediump float;
            in vec3 vWorldPos;
            in vec3 vNorm;
            in vec3 vCol;
            out vec4 FragColor;

            void main() {
                vec3 N = normalize(vNorm);
                vec3 L = normalize(vec3(0.6, 1.4, 0.7));
                float diff = max(dot(N, L), 0.0);
                vec3 ambient = vec3(0.35, 0.38, 0.44);
                vec3 sunCol = vec3(1.0, 0.96, 0.88);

                float shadow = 1.0;
                if (vWorldPos.y <= 0.02) {
                    float dist = length(vWorldPos.xz - vec2(-0.79, -0.13));
                    if (dist < 1.0) shadow *= smoothstep(0.3, 1.0, dist);
                }

                vec3 lighting = (ambient + diff * sunCol * shadow) * vCol;
                FragColor = vec4(lighting, 1.0);
            }";
            _standard3DProgram = CreateProgram(litVS, litFS);
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

        private unsafe void Build3DWorldMeshes()
        {
            if (_gl == null) return;

            // Sky
            float[] skyVerts = { -1, -1, 1, -1, 1, 1, 1, 1, -1, 1, -1, -1 };
            _vaoSky = _gl.GenVertexArray();
            _vboSky = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoSky);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboSky);
            fixed (float* p = skyVerts) {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(skyVerts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            // Floor
            List<float> floorList = new List<float>();
            int gridSize = 18;
            float step = 1.0f;
            float start = -gridSize * step * 0.5f;

            for (int x = 0; x < gridSize; x++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    float x0 = start + x * step;
                    float z0 = start + z * step;
                    float x1 = x0 + step;
                    float z1 = z0 + step;

                    bool isEven = ((x + z) % 2 == 0);
                    Vector3 tileCol = isEven ? new Vector3(0.85f, 0.86f, 0.89f) : new Vector3(0.35f, 0.38f, 0.44f);
                    AddFloorTile(floorList, x0, z0, x1, z1, tileCol);

                    if ((x + z) % 3 == 0)
                    {
                        Vector3 markerCol = (x % 2 == 0) ? new Vector3(0.95f, 0.25f, 0.25f) : new Vector3(0.95f, 0.85f, 0.20f);
                        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                        float mw = 0.09f, ml = 0.02f;
                        AddFloorTile(floorList, cx - mw, cz - ml, cx + mw, cz + ml, markerCol, 0.002f);
                        AddFloorTile(floorList, cx - ml, cz - mw, cx + ml, cz + mw, markerCol, 0.002f);
                    }
                }
            }
            _floorVertCount = floorList.Count / 9;
            _vaoFloor = CreateVAO(floorList.ToArray());

            // Cube
            List<float> cubeList = new List<float>();
            AddBoxVertices(cubeList, Vector3.Zero, new Vector3(1f, 1f, 1f), new Vector3(0.12f, 0.14f, 0.18f));
            _vaoCube = CreateVAO(cubeList.ToArray());

            // Low-Poly Trees
            List<float> treeList = new List<float>();
            AddBoxVertices(treeList, new Vector3(0, 0.5f, 0), new Vector3(0.25f, 1.0f, 0.25f), new Vector3(0.55f, 0.35f, 0.20f));
            AddBoxVertices(treeList, new Vector3(0, 1.35f, 0), new Vector3(1.1f, 1.0f, 1.1f), new Vector3(0.22f, 0.85f, 0.28f));
            AddBoxVertices(treeList, new Vector3(0, 1.95f, 0), new Vector3(0.8f, 0.7f, 0.8f), new Vector3(0.30f, 0.92f, 0.35f));
            _treeVertCount = treeList.Count / 9;
            _vaoTree = CreateVAO(treeList.ToArray());

            // 3D XYZ Transform Gizmo
            List<float> gizmoList = new List<float>();
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.95f, 0.25f, 0.25f,  1.3f, 0.5f, 0f, 0f, 1f, 0f, 0.95f, 0.25f, 0.25f });
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.25f, 0.95f, 0.25f,  0f, 1.8f, 0f, 0f, 1f, 0f, 0.25f, 0.95f, 0.25f });
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.25f, 0.55f, 0.95f,  0f, 0.5f, 1.3f, 0f, 1f, 0f, 0.25f, 0.55f, 0.95f });
            _gizmoVertCount = gizmoList.Count / 9;
            _vaoGizmo = CreateVAO(gizmoList.ToArray());
        }

        private static void AddFloorTile(List<float> list, float x0, float z0, float x1, float z1, Vector3 col, float y = 0f)
        {
            float[] verts = {
                x0, y, z0,  0, 1, 0,  col.X, col.Y, col.Z,
                x1, y, z0,  0, 1, 0,  col.X, col.Y, col.Z,
                x1, y, z1,  0, 1, 0,  col.X, col.Y, col.Z,
                x1, y, z1,  0, 1, 0,  col.X, col.Y, col.Z,
                x0, y, z1,  0, 1, 0,  col.X, col.Y, col.Z,
                x0, y, z0,  0, 1, 0,  col.X, col.Y, col.Z,
            };
            list.AddRange(verts);
        }

        private static void AddBoxVertices(List<float> v, Vector3 c, Vector3 s, Vector3 col)
        {
            float x = s.X * 0.5f, y = s.Y * 0.5f, z = s.Z * 0.5f;
            float[] r = {
                c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,
                c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,

                c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,
                c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X+x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,

                c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X-x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,
                c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X+x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,

                c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X+x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,
                c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X-x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,

                c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y-y, c.Z+z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,
                c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y+y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,

                c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
                c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
            };
            v.AddRange(r);
        }

        private unsafe uint CreateVAO(float[] data)
        {
            uint vao = _gl!.GenVertexArray();
            uint vbo = _gl.GenBuffer();
            _gl.BindVertexArray(vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            fixed (float* p = data) {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
            uint stride = 9 * sizeof(float);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            return vao;
        }

        private void OnResize(Vector2D<int> size)
        {
            _gl?.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        }

        private void OnUpdate(double delta)
        {
            float dt = (float)delta;
            _fpsTimer += dt;
            _frames++;
            if (_fpsTimer >= 1.0f)
            {
                _fps = _frames;
                _frames = 0;
                _fpsTimer = 0f;
                RunOnUiThread(() => {
                    if (_txtFps != null) _txtFps.Text = $"🟢 {_fps} FPS 4.7ms";
                });
            }

            // =========================================================
            // VIRTUAL JOYSTICK CAMERA NAVIGATION (MOVE & LINGON)
            // =========================================================
            float moveSpeed = 6.0f;
            float lookSpeed = 65.0f;

            // 1. Right Joystick: Lingon / Look (Yaw & Pitch)
            if (_lookJoyVector != Vector2.Zero)
            {
                _camYaw += _lookJoyVector.X * lookSpeed * dt;
                _camPitch = Math.Clamp(_camPitch - _lookJoyVector.Y * lookSpeed * dt, -80.0f, 85.0f);
            }

            // 2. Left Joystick: Lakad / Move (Forward, Backward, Strafe)
            if (_moveJoyVector != Vector2.Zero)
            {
                float radY = _camYaw * MathF.PI / 180f;
                Vector3 forward = new Vector3(MathF.Sin(radY), 0, MathF.Cos(radY));
                Vector3 right = new Vector3(MathF.Cos(radY), 0, -MathF.Sin(radY));

                Vector3 moveDir = (forward * _moveJoyVector.Y) + (right * _moveJoyVector.X);
                _camTarget += moveDir * moveSpeed * dt;
            }

            // 3. Elevation Height (Fly Up / Down)
            if (_flyElevation != 0f)
            {
                _camTarget.Y += _flyElevation * moveSpeed * dt;
            }

            // Animate Selected Node in PlayMode
            if (_currentState == PlayState.PlayMode && _selectedNode != null)
            {
                var cur = _selectedNode.Transform.Rotation;
                _selectedNode.Transform.Rotation = new Vector3(cur.X, cur.Y + dt * 50.0f, cur.Z);
            }

            _scene.Update(dt, _moveJoyVector, _currentState == PlayState.PlayMode);
        }

        private unsafe void OnRender(double delta)
        {
            if (_gl == null || _view == null) return;

            int w = _view.Size.X;
            int h = _view.Size.Y;
            _gl.Viewport(0, 0, (uint)w, (uint)h);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Sky
            _gl.Disable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            _gl.UseProgram(_skyProgram);
            _gl.BindVertexArray(_vaoSky);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

            // 3D Scene
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.Disable(EnableCap.Blend);
            _gl.UseProgram(_standard3DProgram);

            float aspect = (float)w / Math.Max(1, h);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.4f, aspect, 0.1f, 100.0f);

            // Dynamic Camera Eye Position based on Target, Distance, Yaw & Pitch
            float radY = _camYaw * MathF.PI / 180f;
            float radP = _camPitch * MathF.PI / 180f;
            float camX = _camTarget.X + _camDistance * MathF.Cos(radP) * MathF.Sin(radY);
            float camY = _camTarget.Y + _camDistance * MathF.Sin(radP);
            float camZ = _camTarget.Z + _camDistance * MathF.Cos(radP) * MathF.Cos(radY);
            var view = Matrix4x4.CreateLookAt(new Vector3(camX, camY, camZ), _camTarget, Vector3.UnitY);

            var projT = Matrix4x4.Transpose(proj);
            var viewT = Matrix4x4.Transpose(view);

            int locProj = _gl.GetUniformLocation(_standard3DProgram, "uProj");
            int locView = _gl.GetUniformLocation(_standard3DProgram, "uView");
            int locModel = _gl.GetUniformLocation(_standard3DProgram, "uModel");

            _gl.UniformMatrix4(locProj, 1, false, (float*)&projT);
            _gl.UniformMatrix4(locView, 1, false, (float*)&viewT);

            // Floor
            var floorMat = Matrix4x4.Transpose(Matrix4x4.Identity);
            _gl.UniformMatrix4(locModel, 1, false, (float*)&floorMat);
            _gl.BindVertexArray(_vaoFloor);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_floorVertCount);

            // Objects
            foreach (var node in _scene.Nodes)
            {
                var mesh = node.GetComponent<MeshRendererComponent>();
                if (mesh == null) continue;

                var modelT = Matrix4x4.Transpose(node.Transform.GetWorldMatrix());
                _gl.UniformMatrix4(locModel, 1, false, (float*)&modelT);

                if (mesh.Shape == MeshShape.Cube)
                {
                    _gl.BindVertexArray(_vaoCube);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
                }
                else if (mesh.Shape == MeshShape.Tree)
                {
                    _gl.BindVertexArray(_vaoTree);
                    _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_treeVertCount);
                }
            }

            // Gizmo
            if (_selectedNode != null && _currentState == PlayState.EditMode)
            {
                _gl.Disable(EnableCap.DepthTest);
                var gizmoMat = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(_selectedNode.Transform.Position));
                _gl.UniformMatrix4(locModel, 1, false, (float*)&gizmoMat);
                _gl.BindVertexArray(_vaoGizmo);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gizmoVertCount);
            }
        }

        // =========================================================================
        // REAL PROWL STUDIO DARK DOCKED UI (MATCHING SCREENSHOT 2 EXACTLY)
        // =========================================================================
        private void BuildNativeProwlStudioLayout()
        {
            var root = new FrameLayout(this) { LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };

            // 1. TOP HEADER TOOLBAR
            var topBar = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
                LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(38)) { Gravity = GravityFlags.Top }
            };
            topBar.SetBackgroundColor(Color.ParseColor("#14171e"));

            // Menus
            AddHeaderMenu(topBar, "File", new[] { "New Scene", "Open Scene...", "Save Scene", "Save Scene As...", "Build Settings..." });
            AddHeaderMenu(topBar, "Edit", new[] { "Undo", "Redo", "Cut", "Copy", "Paste", "Project Settings" });
            AddHeaderMenu(topBar, "Assets", new[] { "Create C# Script", "Create Material", "Create Shader", "Import Asset..." });
            AddHeaderMenu(topBar, "GameObject", new[] { "3D Object -> Cube", "3D Object -> Low-Poly Tree", "Light -> Directional Light", "Create Empty", "Delete Selected" });
            AddHeaderMenu(topBar, "Window", new[] { "Toggle Sidebar Panel", "Toggle Bottom Dock", "Reset Editor Camera", "Clear Console Logs" });

            // Space
            topBar.AddView(new View(this) { LayoutParameters = new LinearLayout.LayoutParams(0, 1, 1f) });

            // Play / Pause / Step Controls (Center)
            _btnPlay = new Button(this) { Text = "▶", TextSize = 13 };
            _btnPlay.SetTextColor(Color.White);
            _btnPlay.SetBackgroundColor(Color.ParseColor("#202530"));
            _btnPlay.LayoutParameters = new LinearLayout.LayoutParams(DpToPx(44), DpToPx(30)) { Gravity = GravityFlags.CenterVertical };
            _btnPlay.Click += (s, e) => TogglePlayMode();
            topBar.AddView(_btnPlay);

            var btnPause = new Button(this) { Text = "⏸", TextSize = 12 };
            btnPause.SetTextColor(Color.ParseColor("#858b98"));
            btnPause.SetBackgroundColor(Color.ParseColor("#1a1e28"));
            var lpPause = new LinearLayout.LayoutParams(DpToPx(38), DpToPx(30)) { Gravity = GravityFlags.CenterVertical, LeftMargin = DpToPx(3) };
            btnPause.LayoutParameters = lpPause;
            btnPause.Click += (s, e) => Toast.MakeText(this, "Game Paused", ToastLength.Short)?.Show();
            topBar.AddView(btnPause);

            topBar.AddView(new View(this) { LayoutParameters = new LinearLayout.LayoutParams(0, 1, 1f) });

            // FPS & Engine Badges
            _txtFps = new TextView(this) { Text = "🟢 212 FPS 4.7ms", TextSize = 11 };
            _txtFps.SetTextColor(Color.ParseColor("#2ecc71"));
            _txtFps.SetPadding(DpToPx(6), DpToPx(8), DpToPx(6), DpToPx(8));
            topBar.AddView(_txtFps);

            var txtVer = new TextView(this) { Text = "v1.0-preview | MyGame5 ⚙", TextSize = 11 };
            txtVer.SetTextColor(Color.ParseColor("#858b98"));
            txtVer.SetPadding(DpToPx(6), DpToPx(8), DpToPx(12), DpToPx(8));
            topBar.AddView(txtVer);

            root.AddView(topBar);

            // 2. VIEWPORT TABS (Scene, Game, Preferences)
            var vpTabs = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
                LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, DpToPx(28))
                {
                    TopMargin = DpToPx(42),
                    LeftMargin = DpToPx(10)
                }
            };
            vpTabs.AddView(CreateTabButton("❖ Scene", true));
            vpTabs.AddView(CreateTabButton("🎮 Game", false));
            vpTabs.AddView(CreateTabButton("⚙ Preferences", false));
            root.AddView(vpTabs);

            // 3. VIEWPORT LEFT FLOATING TOOLBAR (Move, Rotate, Scale)
            var leftToolbar = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(DpToPx(32), ViewGroup.LayoutParams.WrapContent)
                {
                    TopMargin = DpToPx(80),
                    LeftMargin = DpToPx(10)
                }
            };
            leftToolbar.SetBackgroundColor(Color.ParseColor("#c0181b25"));
            leftToolbar.AddView(CreateToolIcon("✥", "Move Tool"));
            leftToolbar.AddView(CreateToolIcon("↻", "Rotate Tool"));
            leftToolbar.AddView(CreateToolIcon("⤢", "Scale Tool"));
            leftToolbar.AddView(CreateToolIcon("⊡", "Bounds Tool"));
            root.AddView(leftToolbar);

            // 4. RIGHT SIDEBAR: HIERARCHY & INSPECTOR DOCKS
            int rightW = DpToPx(260);
            _rightPanel = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(rightW, ViewGroup.LayoutParams.MatchParent)
                {
                    Gravity = GravityFlags.Right,
                    TopMargin = DpToPx(38),
                    BottomMargin = DpToPx(22) // Leave room for status bar
                }
            };
            _rightPanel.SetBackgroundColor(Color.ParseColor("#161922"));

            // --- Hierarchy Dock ---
            var hHeader = CreateHeaderBar("Hierarchy ✕");
            _rightPanel.AddView(hHeader);

            var hSearch = new EditText(this) { Hint = "🔍 Search...", TextSize = 10 };
            hSearch.SetTextColor(Color.White);
            hSearch.SetHintTextColor(Color.ParseColor("#636b7c"));
            hSearch.SetBackgroundColor(Color.ParseColor("#1b1f2b"));
            hSearch.SetPadding(DpToPx(8), DpToPx(4), DpToPx(8), DpToPx(4));
            _rightPanel.AddView(hSearch);

            var scrollHierarchy = new ScrollView(this) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(140)) };
            _layoutHierarchyTree = new LinearLayout(this) { Orientation = Orientation.Vertical };
            scrollHierarchy.AddView(_layoutHierarchyTree);
            _rightPanel.AddView(scrollHierarchy);

            var div = new View(this) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(1)) };
            div.SetBackgroundColor(Color.ParseColor("#222734"));
            _rightPanel.AddView(div);

            // --- Inspector Dock ---
            var iHeader = CreateHeaderBar("Inspector ✕");
            _rightPanel.AddView(iHeader);

            var scrollInsp = new ScrollView(this) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _layoutInspectorBody = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _layoutInspectorBody.SetPadding(DpToPx(10), DpToPx(4), DpToPx(10), DpToPx(6));
            scrollInsp.AddView(_layoutInspectorBody);
            _rightPanel.AddView(scrollInsp);

            root.AddView(_rightPanel);

            // 5. BOTTOM DOCK: PROJECT & CONSOLE PANELS (SPLIT LAYOUT)
            int botH = DpToPx(140);
            _botPanel = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
                LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, botH)
                {
                    Gravity = GravityFlags.Bottom,
                    RightMargin = rightW,
                    BottomMargin = DpToPx(22) // Above status bar
                }
            };
            _botPanel.SetBackgroundColor(Color.ParseColor("#14161f"));

            // Project Dock (Left Split)
            var projectPanel = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
            };
            var projHeader = CreateHeaderBar("📁 Project ✕  |  Assets >");
            projectPanel.AddView(projHeader);

            var projScroll = new HorizontalScrollView(this) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _layoutProjectGrid = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            _layoutProjectGrid.SetPadding(DpToPx(8), DpToPx(6), DpToPx(8), DpToPx(6));

            string[] assets = { "📁 textures", "📁 lightmaps", "📁 Scripts", "📄 Player.cs", "🎨 Material", "🗿 banana_man" };
            foreach (var a in assets)
            {
                var card = new TextView(this) { Text = a, TextSize = 10 };
                card.SetTextColor(Color.White);
                card.SetBackgroundColor(Color.ParseColor("#1c202c"));
                card.SetPadding(DpToPx(8), DpToPx(12), DpToPx(8), DpToPx(12));
                var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = DpToPx(6) };
                card.LayoutParameters = lp;
                card.Click += (s, e) => Toast.MakeText(this, $"Opened Asset: {a}", ToastLength.Short)?.Show();
                _layoutProjectGrid.AddView(card);
            }
            projScroll.AddView(_layoutProjectGrid);
            projectPanel.AddView(projScroll);
            _botPanel.AddView(projectPanel);

            // Vertical Splitter
            var splitDiv = new View(this) { LayoutParameters = new LinearLayout.LayoutParams(DpToPx(1), ViewGroup.LayoutParams.MatchParent) };
            splitDiv.SetBackgroundColor(Color.ParseColor("#252a38"));
            _botPanel.AddView(splitDiv);

            // Console Dock (Right Split)
            var consolePanel = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1.2f)
            };
            var conHeader = CreateHeaderBar("📟 Console ✕  (ℹ 69  ⚠ 2  🔴 1)");
            consolePanel.AddView(conHeader);

            var conScroll = new ScrollView(this) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
            _layoutConsoleLog = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _layoutConsoleLog.SetPadding(DpToPx(8), DpToPx(4), DpToPx(8), DpToPx(4));

            AddConsoleLog("ℹ info: VAO: [ID 0] Mesh uploaded successfully to VRAM (GPU)");
            AddConsoleLog("ℹ info: Compiling shader pass Standard with Keywords: LIGHT_ON");
            AddConsoleLog("ℹ info: Compiling shader pass Gizmos with [CommandBuffer]");
            AddConsoleLog("⚠ warning: Texture 'Cobble_Normal' 2048x2048 using default compression");

            conScroll.AddView(_layoutConsoleLog);
            consolePanel.AddView(conScroll);
            _botPanel.AddView(consolePanel);

            root.AddView(_botPanel);

            // 6. BOTTOM ENGINE STATUS BAR
            var statusBar = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
                LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(22)) { Gravity = GravityFlags.Bottom }
            };
            statusBar.SetBackgroundColor(Color.ParseColor("#0e1017"));

            var txtStatus = new TextView(this) { Text = " ℹ VAO: [ID 0] Mesh uploaded to GPU | Untitled Scene | 💾 38 MB | OpenGL ES 3.0", TextSize = 10 };
            txtStatus.SetTextColor(Color.ParseColor("#858b98"));
            txtStatus.SetPadding(DpToPx(6), DpToPx(2), DpToPx(6), DpToPx(2));
            statusBar.AddView(txtStatus);
            root.AddView(statusBar);

            // =====================================================================
            // 7. DUAL VIRTUAL JOYSTICK OVERLAY (MOVE / LAKAD & LOOK / LINGON)
            // =====================================================================
            _joystickOverlayContainer = new FrameLayout(this)
            {
                LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
            };

            // LEFT VIRTUAL JOYSTICK (Walk / Move)
            var leftStick = new VirtualJoystickView(this, (vec) => _moveJoyVector = vec);
            var lpLeft = new FrameLayout.LayoutParams(DpToPx(130), DpToPx(130))
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Left,
                LeftMargin = DpToPx(14),
                BottomMargin = DpToPx(148) // Above bottom dock
            };
            leftStick.LayoutParameters = lpLeft;
            _joystickOverlayContainer.AddView(leftStick);

            var lblMove = new TextView(this) { Text = "MOVE / LAKAD", TextSize = 9 };
            lblMove.SetTextColor(Color.ParseColor("#80ffffff"));
            var lpLblMove = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Left,
                LeftMargin = DpToPx(42),
                BottomMargin = DpToPx(140)
            };
            lblMove.LayoutParameters = lpLblMove;
            _joystickOverlayContainer.AddView(lblMove);

            // RIGHT VIRTUAL JOYSTICK (Look / Lingon)
            var rightStick = new VirtualJoystickView(this, (vec) => _lookJoyVector = vec);
            var lpRight = new FrameLayout.LayoutParams(DpToPx(130), DpToPx(130))
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Right,
                RightMargin = rightW + DpToPx(14),
                BottomMargin = DpToPx(148)
            };
            rightStick.LayoutParameters = lpRight;
            _joystickOverlayContainer.AddView(rightStick);

            var lblLook = new TextView(this) { Text = "LOOK / LINGON", TextSize = 9 };
            lblLook.SetTextColor(Color.ParseColor("#80ffffff"));
            var lpLblLook = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Bottom | GravityFlags.Right,
                RightMargin = rightW + DpToPx(42),
                BottomMargin = DpToPx(140)
            };
            lblLook.LayoutParameters = lpLblLook;
            _joystickOverlayContainer.AddView(lblLook);

            // FLY UP / DOWN ELEVATION BUTTONS
            var elevPanel = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(DpToPx(38), DpToPx(85))
                {
                    Gravity = GravityFlags.Bottom | GravityFlags.Left,
                    LeftMargin = DpToPx(150),
                    BottomMargin = DpToPx(160)
                }
            };

            var btnUp = new Button(this) { Text = "▲", TextSize = 11 };
            btnUp.SetTextColor(Color.White);
            btnUp.SetBackgroundColor(Color.ParseColor("#80252b3a"));
            btnUp.Touch += (s, e) => {
                if (e.Event?.Action == MotionEventActions.Down) _flyElevation = 1.0f;
                else if (e.Event?.Action == MotionEventActions.Up || e.Event?.Action == MotionEventActions.Cancel) _flyElevation = 0f;
            };
            elevPanel.AddView(btnUp);

            var btnDown = new Button(this) { Text = "▼", TextSize = 11 };
            btnDown.SetTextColor(Color.White);
            btnDown.SetBackgroundColor(Color.ParseColor("#80252b3a"));
            btnDown.Touch += (s, e) => {
                if (e.Event?.Action == MotionEventActions.Down) _flyElevation = -1.0f;
                else if (e.Event?.Action == MotionEventActions.Up || e.Event?.Action == MotionEventActions.Cancel) _flyElevation = 0f;
            };
            elevPanel.AddView(btnDown);

            _joystickOverlayContainer.AddView(elevPanel);

            root.AddView(_joystickOverlayContainer);

            // Attach Direct to Android Content
            AddContentView(root, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            RefreshHierarchyUI();
            RefreshInspectorUI();
        }

        // =========================================================================
        // UI HELPERS & DYNAMIC HANDLERS
        // =========================================================================
        private void AddHeaderMenu(LinearLayout container, string menuTitle, string[] items)
        {
            var btn = new TextView(this) { Text = menuTitle, TextSize = 12 };
            btn.SetTextColor(Color.ParseColor("#b5bac8"));
            btn.SetPadding(DpToPx(10), DpToPx(8), DpToPx(10), DpToPx(8));
            btn.Click += (s, e) => {
                var popup = new PopupMenu(this, btn);
                for (int i = 0; i < items.Length; i++) popup.Menu.Add(0, i, i, items[i]);
                popup.MenuItemClick += (sender, args) => HandleMenuAction(args.Item?.TitleFormatted?.ToString() ?? "");
                popup.Show();
            };
            container.AddView(btn);
        }

        private void HandleMenuAction(string action)
        {
            if (action.Contains("Cube"))
            {
                var c = _scene.CreateNode("Cube_" + (_scene.Nodes.Count + 1));
                c.Transform.Position = _camTarget + new Vector3(0, 0.5f, 0);
                c.AddComponent<MeshRendererComponent>().Shape = MeshShape.Cube;
                SelectNode(c);
                AddConsoleLog($"ℹ Created GameObject '{c.Name}'");
            }
            else if (action.Contains("Tree"))
            {
                var t = _scene.CreateNode("Tree_" + (_scene.Nodes.Count + 1));
                t.Transform.Position = _camTarget;
                t.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;
                SelectNode(t);
                AddConsoleLog($"ℹ Created GameObject '{t.Name}'");
            }
            else if (action.Contains("Delete"))
            {
                if (_selectedNode != null)
                {
                    AddConsoleLog($"⚠ Deleted GameObject '{_selectedNode.Name}'");
                    _scene.Nodes.Remove(_selectedNode);
                    _selectedNode = _scene.Nodes.Count > 0 ? _scene.Nodes[0] : null;
                    RefreshHierarchyUI();
                    RefreshInspectorUI();
                }
            }
            else if (action.Contains("Toggle Sidebar"))
            {
                _isSidebarVisible = !_isSidebarVisible;
                if (_rightPanel != null) _rightPanel.Visibility = _isSidebarVisible ? ViewStates.Visible : ViewStates.Gone;
            }
            else if (action.Contains("Toggle Bottom"))
            {
                _isBottomDockVisible = !_isBottomDockVisible;
                if (_botPanel != null) _botPanel.Visibility = _isBottomDockVisible ? ViewStates.Visible : ViewStates.Gone;
            }
            else if (action.Contains("Reset Editor Camera"))
            {
                _camYaw = 45.0f;
                _camPitch = 25.0f;
                _camDistance = 7.5f;
                _camTarget = new Vector3(0, 0.5f, 0);
                AddConsoleLog("ℹ Camera reset to default coordinates.");
            }
            else if (action.Contains("Clear Console"))
            {
                _layoutConsoleLog?.RemoveAllViews();
            }
            else
            {
                Toast.MakeText(this, $"Action: {action}", ToastLength.Short)?.Show();
            }
        }

        private void SelectNode(ProwlNode? node)
        {
            _selectedNode = node;
            RefreshHierarchyUI();
            RefreshInspectorUI();
        }

        public void RefreshHierarchyUI()
        {
            if (_layoutHierarchyTree == null) return;
            _layoutHierarchyTree.RemoveAllViews();

            var sceneRoot = CreateTreeItem("📁 Untitled Scene", false);
            _layoutHierarchyTree.AddView(sceneRoot);

            foreach (var node in _scene.Nodes)
            {
                bool isSel = (_selectedNode == node);
                string icon = node.GetComponent<LightComponent>() != null ? "💡 " : (node.GetComponent<MeshRendererComponent>()?.Shape == MeshShape.Tree ? "🌲 " : "📦 ");
                var item = CreateTreeItem($"  {icon}{node.Name}", isSel);
                item.Click += (s, e) => SelectNode(node);
                _layoutHierarchyTree.AddView(item);
            }
        }

        public void RefreshInspectorUI()
        {
            if (_layoutInspectorBody == null) return;
            _layoutInspectorBody.RemoveAllViews();

            if (_selectedNode == null)
            {
                var empty = new TextView(this) { Text = "No GameObject Selected", TextSize = 11 };
                empty.SetTextColor(Color.ParseColor("#858b98"));
                _layoutInspectorBody.AddView(empty);
                return;
            }

            var nodeTitle = new TextView(this) { Text = $"☑ {_selectedNode.Name}   [Dynamic ▼]", TextSize = 12 };
            nodeTitle.SetTextColor(Color.White);
            _layoutInspectorBody.AddView(nodeTitle);

            var tagLayer = new TextView(this) { Text = "Tag: Untagged       Layer: Default", TextSize = 10 };
            tagLayer.SetTextColor(Color.ParseColor("#858b98"));
            tagLayer.SetPadding(0, DpToPx(2), 0, DpToPx(6));
            _layoutInspectorBody.AddView(tagLayer);

            // Transform Section
            var tfHeader = new TextView(this) { Text = "▼ Transform", TextSize = 11 };
            tfHeader.SetTextColor(Color.ParseColor("#3884ff"));
            tfHeader.SetPadding(0, DpToPx(4), 0, DpToPx(2));
            _layoutInspectorBody.AddView(tfHeader);

            var pos = _selectedNode.Transform.Position;
            var rot = _selectedNode.Transform.Rotation;
            var scl = _selectedNode.Transform.Scale;

            _layoutInspectorBody.AddView(CreateEditableVector3Row("Position", pos, v => { _selectedNode.Transform.Position = v; }));
            _layoutInspectorBody.AddView(CreateEditableVector3Row("Rotation", rot, v => { _selectedNode.Transform.Rotation = v; }));
            _layoutInspectorBody.AddView(CreateEditableVector3Row("Scale", scl, v => { _selectedNode.Transform.Scale = v; }));

            // MeshRenderer Section
            var mesh = _selectedNode.GetComponent<MeshRendererComponent>();
            if (mesh != null)
            {
                var mrHeader = new TextView(this) { Text = "▼ MeshRenderer", TextSize = 11 };
                mrHeader.SetTextColor(Color.ParseColor("#3884ff"));
                mrHeader.SetPadding(0, DpToPx(6), 0, DpToPx(2));
                _layoutInspectorBody.AddView(mrHeader);

                var txtMesh = new TextView(this) { Text = $"Mesh: {mesh.Shape} (Mesh)\nMaterials: 1 elements", TextSize = 10 };
                txtMesh.SetTextColor(Color.ParseColor("#9da4b4"));
                _layoutInspectorBody.AddView(txtMesh);
            }

            // Buttons
            var btnFocus = new Button(this) { Text = "🎯 Focus Camera Target", TextSize = 11 };
            btnFocus.SetTextColor(Color.White);
            btnFocus.SetBackgroundColor(Color.ParseColor("#252b3a"));
            btnFocus.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(32)) { TopMargin = DpToPx(6) };
            btnFocus.Click += (s, e) => {
                _camTarget = _selectedNode.Transform.Position;
                Toast.MakeText(this, $"Camera focused on {_selectedNode.Name}", ToastLength.Short)?.Show();
            };
            _layoutInspectorBody.AddView(btnFocus);

            var btnAddComp = new Button(this) { Text = "+ Add Component", TextSize = 11 };
            btnAddComp.SetTextColor(Color.White);
            btnAddComp.SetBackgroundColor(Color.ParseColor("#1e2330"));
            btnAddComp.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(32)) { TopMargin = DpToPx(4) };
            btnAddComp.Click += (s, e) => Toast.MakeText(this, "Component Menu Opened", ToastLength.Short)?.Show();
            _layoutInspectorBody.AddView(btnAddComp);
        }

        private void TogglePlayMode()
        {
            if (_currentState == PlayState.EditMode)
            {
                _currentState = PlayState.PlayMode;
                _btnPlay!.Text = "⏹";
                _btnPlay.SetTextColor(Color.ParseColor("#2ecc71"));

                _initialTransforms.Clear();
                foreach (var n in _scene.Nodes) _initialTransforms[n] = (n.Transform.Position, n.Transform.Rotation, n.Transform.Scale);
                AddConsoleLog("▶ Engine entered Play Mode");
            }
            else
            {
                _currentState = PlayState.EditMode;
                _btnPlay!.Text = "▶";
                _btnPlay.SetTextColor(Color.White);

                foreach (var kvp in _initialTransforms)
                {
                    kvp.Key.Transform.Position = kvp.Value.pos;
                    kvp.Key.Transform.Rotation = kvp.Value.rot;
                    kvp.Key.Transform.Scale = kvp.Value.scale;
                }
                AddConsoleLog("⏹ Engine stopped (State restored to Edit Mode)");
                RefreshInspectorUI();
            }
        }

        private void AddConsoleLog(string msg)
        {
            RunOnUiThread(() => {
                if (_layoutConsoleLog == null) return;
                var txt = new TextView(this) { Text = msg, TextSize = 10 };
                txt.SetTextColor(msg.Contains("warning") || msg.Contains("⚠") ? Color.ParseColor("#f1c40f") : Color.ParseColor("#8e96a8"));
                _layoutConsoleLog.AddView(txt, 0);
            });
        }

        private LinearLayout CreateEditableVector3Row(string label, Vector3 val, Action<Vector3> onUpdate)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetPadding(0, DpToPx(2), 0, DpToPx(2));

            var lbl = new TextView(this) { Text = label, TextSize = 10, LayoutParameters = new LinearLayout.LayoutParams(DpToPx(48), ViewGroup.LayoutParams.WrapContent) };
            lbl.SetTextColor(Color.ParseColor("#858b98"));
            row.AddView(lbl);

            row.AddView(CreateNumChip("X", val.X.ToString("F2"), "#d63031", (v) => { val.X = v; onUpdate(val); }));
            row.AddView(CreateNumChip("Y", val.Y.ToString("F2"), "#00b894", (v) => { val.Y = v; onUpdate(val); }));
            row.AddView(CreateNumChip("Z", val.Z.ToString("F2"), "#0984e3", (v) => { val.Z = v; onUpdate(val); }));
            return row;
        }

        private TextView CreateNumChip(string axis, string val, string colorHex, Action<float> onValChanged)
        {
            var tv = new TextView(this) { Text = $"{axis} {val}", TextSize = 10 };
            tv.SetTextColor(Color.White);
            tv.SetBackgroundColor(Color.ParseColor(colorHex));
            tv.SetPadding(DpToPx(4), DpToPx(2), DpToPx(4), DpToPx(2));
            var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = DpToPx(4) };
            tv.LayoutParameters = lp;

            tv.Click += (s, e) => {
                var input = new EditText(this) { Text = val };
                input.SetRawInputType(Android.Text.InputTypes.NumberFlagDecimal | Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagSigned);

                new AlertDialog.Builder(this)
                    .SetTitle($"Edit {axis} Coordinate")
                    .SetView(input)
                    .SetPositiveButton("Apply", (dlg, ev) => {
                        if (float.TryParse(input.Text, out float parsed))
                        {
                            onValChanged(parsed);
                            RefreshInspectorUI();
                        }
                    })
                    .SetNegativeButton("Cancel", (dlg, ev) => { })
                    .Show();
            };
            return tv;
        }

        private TextView CreateToolIcon(string icon, string tooltip)
        {
            var tv = new TextView(this) { Text = icon, TextSize = 14 };
            tv.SetTextColor(Color.White);
            tv.SetPadding(DpToPx(8), DpToPx(6), DpToPx(8), DpToPx(6));
            tv.Click += (s, e) => Toast.MakeText(this, tooltip, ToastLength.Short)?.Show();
            return tv;
        }

        private TextView CreateTabButton(string text, bool isActive)
        {
            var tv = new TextView(this) { Text = text, TextSize = 11 };
            tv.SetTextColor(isActive ? Color.ParseColor("#3884ff") : Color.ParseColor("#858b98"));
            tv.SetBackgroundColor(isActive ? Color.ParseColor("#1b1f2b") : Color.Transparent);
            tv.SetPadding(DpToPx(10), DpToPx(5), DpToPx(10), DpToPx(5));
            return tv;
        }

        private TextView CreateHeaderBar(string title)
        {
            var tv = new TextView(this) { Text = title, TextSize = 11 };
            tv.SetTextColor(Color.ParseColor("#9da4b4"));
            tv.SetBackgroundColor(Color.ParseColor("#1b1e28"));
            tv.SetPadding(DpToPx(8), DpToPx(4), DpToPx(8), DpToPx(4));
            return tv;
        }

        private TextView CreateTreeItem(string title, bool isSelected)
        {
            var tv = new TextView(this) { Text = title, TextSize = 11 };
            tv.SetTextColor(Color.White);
            tv.SetBackgroundColor(isSelected ? Color.ParseColor("#2b5bb8") : Color.Transparent);
            tv.SetPadding(DpToPx(8), DpToPx(4), DpToPx(8), DpToPx(4));
            return tv;
        }

        private int DpToPx(int dp)
        {
            return (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, dp, Resources!.DisplayMetrics);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _gl?.DeleteProgram(_standard3DProgram);
            _gl?.DeleteProgram(_skyProgram);
            _gl?.Dispose();
            _view?.Dispose();
        }
    }

    // =========================================================================
    // VIRTUAL JOYSTICK CUSTOM VIEW (ON-SCREEN DUAL THUMBSTICK)
    // =========================================================================
    public class VirtualJoystickView : View
    {
        private readonly Action<Vector2> _onJoyMoved;
        private readonly Paint _basePaint;
        private readonly Paint _stickPaint;

        private float _centerX, _centerY;
        private float _baseRadius;
        private float _stickRadius;
        private float _stickX, _stickY;
        private bool _isPressed = false;

        public VirtualJoystickView(Android.Content.Context ctx, Action<Vector2> onJoyMoved) : base(ctx)
        {
            _onJoyMoved = onJoyMoved;

            _basePaint = new Paint(PaintFlags.AntiAlias)
            {
                Color = Color.Argb(70, 25, 30, 45),
                StrokeWidth = 3
            };
            _basePaint.SetStyle(Paint.Style.FillAndStroke);

            _stickPaint = new Paint(PaintFlags.AntiAlias)
            {
                Color = Color.Argb(160, 56, 132, 255)
            };
            _stickPaint.SetStyle(Paint.Style.Fill);
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            _centerX = w * 0.5f;
            _centerY = h * 0.5f;
            _baseRadius = MathF.Min(w, h) * 0.45f;
            _stickRadius = _baseRadius * 0.38f;
            _stickX = _centerX;
            _stickY = _centerY;
        }

        protected override void OnDraw(Canvas? canvas)
        {
            if (canvas == null) return;
            // Draw Outer Ring
            canvas.DrawCircle(_centerX, _centerY, _baseRadius, _basePaint);
            // Draw Inner Thumbstick
            canvas.DrawCircle(_stickX, _stickY, _stickRadius, _stickPaint);
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null) return false;

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                case MotionEventActions.Move:
                    _isPressed = true;
                    float dx = e.GetX() - _centerX;
                    float dy = e.GetY() - _centerY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (dist > _baseRadius)
                    {
                        dx = (dx / dist) * _baseRadius;
                        dy = (dy / dist) * _baseRadius;
                    }

                    _stickX = _centerX + dx;
                    _stickY = _centerY + dy;

                    // Output Normalized Vector (-1 to 1)
                    _onJoyMoved(new Vector2(dx / _baseRadius, -dy / _baseRadius));
                    Invalidate();
                    return true;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    _isPressed = false;
                    _stickX = _centerX;
                    _stickY = _centerY;
                    _onJoyMoved(Vector2.Zero);
                    Invalidate();
                    return true;
            }
            return base.OnTouchEvent(e);
        }
    }

    // =========================================================================
    // PROWL ENGINE SCENE & COMPONENT SYSTEM
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

        public void Start() { foreach (var n in Nodes) n.Start(); }
        public void Update(float dt, Vector2 joy, bool isPlaying) { foreach (var n in Nodes) n.Update(dt, joy, isPlaying); }
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

        public void Start()
        {
            foreach (var c in Components) c.Start();
            foreach (var s in Scripts) s.Start();
        }

        public void Update(float dt, Vector2 joy, bool isPlaying)
        {
            foreach (var c in Components) c.Update(dt);
            foreach (var s in Scripts) if (isPlaying) s.UpdateWithInput(dt, joy);
        }
    }

    public class Transform
    {
        public ProwlNode Node { get; }
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Rotation { get; set; } = Vector3.Zero;
        public Vector3 Scale { get; set; } = Vector3.One;

        public Transform(ProwlNode n) { Node = n; }

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

    public enum MeshShape { Cube, Tree, Humanoid }
    public class MeshRendererComponent : Component
    {
        public MeshShape Shape { get; set; } = MeshShape.Cube;
        public Vector3 Color { get; set; } = Vector3.One;
    }

    public enum LightType { Directional, Point }
    public class LightComponent : Component
    {
        public LightType Type { get; set; } = LightType.Directional;
        public Vector3 Color { get; set; } = Vector3.One;
    }
}
