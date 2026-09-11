using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Android.App;
using Android.Content.PM;
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
        private ProwlNode? _cubeNode;

        // --- Camera Navigation ---
        private float _camYaw = 40.0f;
        private float _camPitch = 22.0f;
        private float _camDistance = 7.5f;
        private Vector3 _camTarget = new Vector3(0, 0.6f, 0);

        private float _touchLastX, _touchLastY;
        private bool _isOrbiting = false;

        // --- Native UI Views ---
        private Button? _btnPlay;
        private TextView? _txtFps;
        private LinearLayout? _layoutConsoleLog;
        private LinearLayout? _layoutProjectGrid;
        private TextView? _tabProject;
        private TextView? _tabConsole;

        private int _fps = 212;
        private float _fpsTimer = 0f;
        private int _frames = 0;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try { AssetExtractor.EnsureAssetsExtracted(this); } catch { }

            InitProwlScene();

            // I-load ang Native Android Layout Overlay sa ibabaw ng Silk Viewport
            RunOnUiThread(BuildNativeProwlStudioLayout);
        }

        private void InitProwlScene()
        {
            // 1. Directional Light
            var sun = _scene.CreateNode("Directional Light");
            var sunLight = sun.AddComponent<LightComponent>();
            sunLight.Type = LightType.Directional;
            sun.Transform.Position = new Vector3(0, 4f, 0);

            // 2. Active Cube (Prowl Default Object)
            _cubeNode = _scene.CreateNode("Cube");
            _cubeNode.Transform.Position = new Vector3(-0.79f, 0.5f, -0.13f);
            var cubeMesh = _cubeNode.AddComponent<MeshRendererComponent>();
            cubeMesh.Shape = MeshShape.Cube;

            // 3. Low-Poly Trees
            var tree1 = _scene.CreateNode("Tree_1");
            tree1.Transform.Position = new Vector3(2.2f, 0, 1.8f);
            tree1.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            var tree2 = _scene.CreateNode("Tree_2");
            tree2.Transform.Position = new Vector3(-2.4f, 0, 2.0f);
            tree2.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            var tree3 = _scene.CreateNode("Tree_Small");
            tree3.Transform.Position = new Vector3(1.6f, 0, -1.8f);
            tree3.Transform.Scale = new Vector3(0.7f, 0.7f, 0.7f);
            tree3.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            _selectedNode = _cubeNode;
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

            // 1. SKYBOX GRADIENT SHADER
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

            // 2. LIT 3D WORLD SHADER (Standard Column-Major OpenGL ES)
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

                // Fake ground contact shadow
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

            // 1. Sky Quad
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

            // 2. Prowl Checkered Floor na may Red at Yellow Crosshair Markers (+)
            List<float> floorList = new List<float>();
            int gridSize = 16;
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

                    // Crosshair Marker (+)
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

            // 3. Cube Mesh
            List<float> cubeList = new List<float>();
            AddBoxVertices(cubeList, Vector3.Zero, new Vector3(1f, 1f, 1f), new Vector3(0.12f, 0.14f, 0.18f));
            _vaoCube = CreateVAO(cubeList.ToArray());

            // 4. Low-Poly Trees
            List<float> treeList = new List<float>();
            AddBoxVertices(treeList, new Vector3(0, 0.5f, 0), new Vector3(0.25f, 1.0f, 0.25f), new Vector3(0.55f, 0.35f, 0.20f));
            AddBoxVertices(treeList, new Vector3(0, 1.35f, 0), new Vector3(1.1f, 1.0f, 1.1f), new Vector3(0.22f, 0.85f, 0.28f));
            AddBoxVertices(treeList, new Vector3(0, 1.95f, 0), new Vector3(0.8f, 0.7f, 0.8f), new Vector3(0.30f, 0.92f, 0.35f));
            _treeVertCount = treeList.Count / 9;
            _vaoTree = CreateVAO(treeList.ToArray());

            // 5. 3D XYZ Transform Gizmo (Red X, Green Y, Blue Z) + Lightbulb Icon
            List<float> gizmoList = new List<float>();
            // X Axis
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.95f, 0.25f, 0.25f,  1.3f, 0.5f, 0f, 0f, 1f, 0f, 0.95f, 0.25f, 0.25f });
            // Y Axis
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.25f, 0.95f, 0.25f,  0f, 1.8f, 0f, 0f, 1f, 0f, 0.25f, 0.95f, 0.25f });
            // Z Axis
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.25f, 0.55f, 0.95f,  0f, 0.5f, 1.3f, 0f, 1f, 0f, 0.25f, 0.55f, 0.95f });
            // Lightbulb Ring Icon
            float r = 0.35f;
            for (int i = 0; i < 16; i++)
            {
                float a1 = (i / 16f) * MathF.PI * 2f;
                float a2 = ((i + 1) / 16f) * MathF.PI * 2f;
                gizmoList.AddRange(new[] { MathF.Cos(a1)*r, 3.8f + MathF.Sin(a1)*r, 0f, 0f, 1f, 0f, 1.0f, 0.95f, 0.2f,
                                           MathF.Cos(a2)*r, 3.8f + MathF.Sin(a2)*r, 0f, 0f, 1f, 0f, 1.0f, 0.95f, 0.2f });
            }
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
                // Front
                c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,
                c.X+x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y+y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,  c.X-x, c.Y-y, c.Z+z,  0,0,1,  col.X,col.Y,col.Z,
                // Back
                c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,
                c.X+x, c.Y+y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X+x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,  c.X-x, c.Y-y, c.Z-z,  0,0,-1, col.X*0.7f,col.Y*0.7f,col.Z*0.7f,
                // Top
                c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X-x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,
                c.X+x, c.Y+y, c.Z+z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X+x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,  c.X-x, c.Y+y, c.Z-z,  0,1,0,  col.X*1.2f,col.Y*1.2f,col.Z*1.2f,
                // Bottom
                c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X+x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,
                c.X+x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X-x, c.Y-y, c.Z+z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,  c.X-x, c.Y-y, c.Z-z,  0,-1,0, col.X*0.5f,col.Y*0.5f,col.Z*0.5f,
                // Left
                c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y-y, c.Z+z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,
                c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y+y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,
                // Right
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
                    if (_txtFps != null) _txtFps.Text = $"🟢 {_fps} FPS";
                });
            }

            _scene.Update(dt, Vector2.Zero, _currentState == PlayState.PlayMode);
        }

        private unsafe void OnRender(double delta)
        {
            if (_gl == null || _view == null) return;

            int w = _view.Size.X;
            int h = _view.Size.Y;
            _gl.Viewport(0, 0, (uint)w, (uint)h);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // 1. SKYBOX PASS (DepthMask = false para hindi masira ang depth buffer!)
            _gl.Disable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            _gl.UseProgram(_skyProgram);
            _gl.BindVertexArray(_vaoSky);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

            // 2. 3D WORLD PASS
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.Disable(EnableCap.Blend);
            _gl.UseProgram(_standard3DProgram);

            float aspect = (float)w / Math.Max(1, h);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.4f, aspect, 0.1f, 100.0f);

            // Camera Orbit
            float radY = _camYaw * MathF.PI / 180f;
            float radP = _camPitch * MathF.PI / 180f;
            float camX = _camTarget.X + _camDistance * MathF.Cos(radP) * MathF.Sin(radY);
            float camY = _camTarget.Y + _camDistance * MathF.Sin(radP);
            float camZ = _camTarget.Z + _camDistance * MathF.Cos(radP) * MathF.Cos(radY);
            var view = Matrix4x4.CreateLookAt(new Vector3(camX, camY, camZ), _camTarget, Vector3.UnitY);

            // Transpose bago i-upload sa OpenGL ES (Row-Major to Column-Major)
            var projT = Matrix4x4.Transpose(proj);
            var viewT = Matrix4x4.Transpose(view);

            int locProj = _gl.GetUniformLocation(_standard3DProgram, "uProj");
            int locView = _gl.GetUniformLocation(_standard3DProgram, "uView");
            int locModel = _gl.GetUniformLocation(_standard3DProgram, "uModel");

            _gl.UniformMatrix4(locProj, 1, false, (float*)&projT);
            _gl.UniformMatrix4(locView, 1, false, (float*)&viewT);

            // Draw Checkered Floor
            var floorMat = Matrix4x4.Transpose(Matrix4x4.Identity);
            _gl.UniformMatrix4(locModel, 1, false, (float*)&floorMat);
            _gl.BindVertexArray(_vaoFloor);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_floorVertCount);

            // Draw Entities
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

            // Draw 3D XYZ Transform Gizmo
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
        // NATIVE PROWL ENGINE STUDIO DOCKED UI LAYOUT (EXACT SCREENSHOT 2 MATCH)
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

            // Menu Items
            string[] menus = { "File", "Edit", "Assets", "GameObject", "Window" };
            foreach (var m in menus)
            {
                var tv = new TextView(this) { Text = m, TextSize = 12 };
                tv.SetTextColor(Color.ParseColor("#b5bac8"));
                tv.SetPadding(DpToPx(10), DpToPx(8), DpToPx(10), DpToPx(8));
                topBar.AddView(tv);
            }

            // Space
            var spacer1 = new View(this) { LayoutParameters = new LinearLayout.LayoutParams(0, 1, 1f) };
            topBar.AddView(spacer1);

            // Play / Pause Controls
            _btnPlay = new Button(this) { Text = "▶ Play", TextSize = 11 };
            _btnPlay.SetTextColor(Color.White);
            _btnPlay.SetBackgroundColor(Color.ParseColor("#202530"));
            _btnPlay.LayoutParameters = new LinearLayout.LayoutParams(DpToPx(75), DpToPx(28)) { Gravity = GravityFlags.CenterVertical };
            _btnPlay.Click += (s, e) => {
                _currentState = (_currentState == PlayState.EditMode) ? PlayState.PlayMode : PlayState.EditMode;
                _btnPlay.Text = (_currentState == PlayState.PlayMode) ? "⏹ Stop" : "▶ Play";
                _btnPlay.SetTextColor((_currentState == PlayState.PlayMode) ? Color.ParseColor("#2ecc71") : Color.White);
            };
            topBar.AddView(_btnPlay);

            var spacer2 = new View(this) { LayoutParameters = new LinearLayout.LayoutParams(0, 1, 1f) };
            topBar.AddView(spacer2);

            _txtFps = new TextView(this) { Text = "🟢 212 FPS", TextSize = 11 };
            _txtFps.SetTextColor(Color.ParseColor("#2ecc71"));
            _txtFps.SetPadding(DpToPx(6), DpToPx(8), DpToPx(6), DpToPx(8));
            topBar.AddView(_txtFps);

            var txtVer = new TextView(this) { Text = "v1.0-preview | MyGame5", TextSize = 11 };
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
            var tabScene = CreateTabButton("❖ Scene", true);
            var tabGame = CreateTabButton("🎮 Game", false);
            var tabPrefs = CreateTabButton("⚙ Preferences", false);
            vpTabs.AddView(tabScene);
            vpTabs.AddView(tabGame);
            vpTabs.AddView(tabPrefs);
            root.AddView(vpTabs);

            // 3. RIGHT SIDEBAR: HIERARCHY & INSPECTOR
            int rightW = DpToPx(260);
            var rightPanel = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(rightW, ViewGroup.LayoutParams.MatchParent)
                {
                    Gravity = GravityFlags.Right,
                    TopMargin = DpToPx(38)
                }
            };
            rightPanel.SetBackgroundColor(Color.ParseColor("#161922"));

            // --- Hierarchy ---
            var hHeader = CreateHeaderBar("Hierarchy");
            rightPanel.AddView(hHeader);

            var hTree = new LinearLayout(this) { Orientation = Orientation.Vertical };
            hTree.AddView(CreateTreeItem("📁 Untitled Scene", false));
            hTree.AddView(CreateTreeItem("  📷 Main Camera", false));
            hTree.AddView(CreateTreeItem("  💡 Directional Light", false));
            hTree.AddView(CreateTreeItem("  ▦ Floor", false));
            hTree.AddView(CreateTreeItem("  📦 Cube", true)); // Selected Active
            rightPanel.AddView(hTree);

            // Divider
            var div = new View(this) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(1)) };
            div.SetBackgroundColor(Color.ParseColor("#222734"));
            rightPanel.AddView(div);

            // --- Inspector ---
            var iHeader = CreateHeaderBar("Inspector");
            rightPanel.AddView(iHeader);

            var inspBody = new LinearLayout(this) { Orientation = Orientation.Vertical };
            inspBody.SetPadding(DpToPx(10), DpToPx(4), DpToPx(10), DpToPx(4));

            var nodeTitle = new TextView(this) { Text = "☑ Cube               [Dynamic ▼]", TextSize = 12 };
            nodeTitle.SetTextColor(Color.White);
            inspBody.AddView(nodeTitle);

            // Transform Section
            var tfHeader = new TextView(this) { Text = "▼ Transform", TextSize = 11 };
            tfHeader.SetTextColor(Color.ParseColor("#3884ff"));
            tfHeader.SetPadding(0, DpToPx(6), 0, DpToPx(2));
            inspBody.AddView(tfHeader);

            inspBody.AddView(CreateVector3Row("Position", "-0.79", "0.50", "-0.13"));
            inspBody.AddView(CreateVector3Row("Rotation", "0.0", "0.0", "0.0"));
            inspBody.AddView(CreateVector3Row("Scale", "1.0", "1.0", "1.0"));

            // MeshRenderer Section
            var mrHeader = new TextView(this) { Text = "▼ MeshRenderer", TextSize = 11 };
            mrHeader.SetTextColor(Color.ParseColor("#3884ff"));
            mrHeader.SetPadding(0, DpToPx(6), 0, DpToPx(2));
            inspBody.AddView(mrHeader);

            var txtMesh = new TextView(this) { Text = "Mesh: 📦 Cube (Mesh)\nMaterials: 1 elements", TextSize = 11 };
            txtMesh.SetTextColor(Color.ParseColor("#9da4b4"));
            inspBody.AddView(txtMesh);

            // Add Component Button
            var btnAddComp = new Button(this) { Text = "+ Add Component", TextSize = 11 };
            btnAddComp.SetTextColor(Color.White);
            btnAddComp.SetBackgroundColor(Color.ParseColor("#252b3a"));
            btnAddComp.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, DpToPx(32)) { TopMargin = DpToPx(8) };
            inspBody.AddView(btnAddComp);

            rightPanel.AddView(inspBody);
            root.AddView(rightPanel);

            // 4. BOTTOM DOCK: PROJECT & CONSOLE TABS
            int botH = DpToPx(130);
            var botPanel = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, botH)
                {
                    Gravity = GravityFlags.Bottom,
                    RightMargin = rightW
                }
            };
            botPanel.SetBackgroundColor(Color.ParseColor("#14161f"));

            // Tabs Bar
            var botTabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            botTabs.SetBackgroundColor(Color.ParseColor("#181b25"));

            _tabProject = CreateTabButton("📁 Project", false);
            _tabConsole = CreateTabButton("📟 Console", true);
            botTabs.AddView(_tabProject);
            botTabs.AddView(_tabConsole);
            botPanel.AddView(botTabs);

            // Console Logs Container
            _layoutConsoleLog = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _layoutConsoleLog.SetPadding(DpToPx(10), DpToPx(4), DpToPx(10), DpToPx(4));

            string[] logs = {
                "ℹ info: VAO: [ID 0] Mesh uploaded successfully to VRAM [DefaultRenderPipeline]",
                "ℹ info: Compiling shader pass Standard with Keywords: LIGHT_ON",
                "ℹ info: Compiling shader pass Gizmos with [CommandBuffer] 12:13:47",
                "⚠ warning: Texture 'Floor_Normal' compressed at 2048x2048"
            };
            foreach (var log in logs)
            {
                var txt = new TextView(this) { Text = log, TextSize = 10 };
                txt.SetTextColor(log.StartsWith("⚠") ? Color.ParseColor("#f1c40f") : Color.ParseColor("#8e96a8"));
                _layoutConsoleLog.AddView(txt);
            }
            botPanel.AddView(_layoutConsoleLog);

            // Project Grid Container
            _layoutProjectGrid = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            _layoutProjectGrid.Visibility = ViewStates.Gone;
            _layoutProjectGrid.SetPadding(DpToPx(10), DpToPx(8), DpToPx(10), DpToPx(8));
            string[] assets = { "📁 Textures", "📁 Scripts", "📁 Shaders", "📄 Player.cs", "🎨 Material" };
            foreach (var a in assets)
            {
                var card = new TextView(this) { Text = a, TextSize = 11 };
                card.SetTextColor(Color.White);
                card.SetBackgroundColor(Color.ParseColor("#1c202c"));
                card.SetPadding(DpToPx(8), DpToPx(14), DpToPx(8), DpToPx(14));
                var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = DpToPx(8) };
                card.LayoutParameters = lp;
                _layoutProjectGrid.AddView(card);
            }
            botPanel.AddView(_layoutProjectGrid);

            // Tab Click handlers
            _tabProject.Click += (s, e) => {
                _tabProject.SetTextColor(Color.ParseColor("#3884ff"));
                _tabConsole.SetTextColor(Color.ParseColor("#858b98"));
                _layoutProjectGrid.Visibility = ViewStates.Visible;
                _layoutConsoleLog.Visibility = ViewStates.Gone;
            };
            _tabConsole.Click += (s, e) => {
                _tabConsole.SetTextColor(Color.ParseColor("#3884ff"));
                _tabProject.SetTextColor(Color.ParseColor("#858b98"));
                _layoutConsoleLog.Visibility = ViewStates.Visible;
                _layoutProjectGrid.Visibility = ViewStates.Gone;
            };

            root.AddView(botPanel);

            // Attach Direct to Android Content
            AddContentView(root, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
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
            tv.SetPadding(DpToPx(10), DpToPx(4), DpToPx(10), DpToPx(4));
            return tv;
        }

        private TextView CreateTreeItem(string title, bool isSelected)
        {
            var tv = new TextView(this) { Text = title, TextSize = 11 };
            tv.SetTextColor(Color.White);
            tv.SetBackgroundColor(isSelected ? Color.ParseColor("#2b5bb8") : Color.Transparent);
            tv.SetPadding(DpToPx(10), DpToPx(3), DpToPx(10), DpToPx(3));
            return tv;
        }

        private LinearLayout CreateVector3Row(string label, string x, string y, string z)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.SetPadding(0, DpToPx(2), 0, DpToPx(2));

            var lbl = new TextView(this) { Text = label, TextSize = 10, LayoutParameters = new LinearLayout.LayoutParams(DpToPx(50), ViewGroup.LayoutParams.WrapContent) };
            lbl.SetTextColor(Color.ParseColor("#858b98"));
            row.AddView(lbl);

            row.AddView(CreateNumChip("X", x, "#d63031"));
            row.AddView(CreateNumChip("Y", y, "#00b894"));
            row.AddView(CreateNumChip("Z", z, "#0984e3"));
            return row;
        }

        private TextView CreateNumChip(string axis, string val, string colorHex)
        {
            var tv = new TextView(this) { Text = $"{axis} {val}", TextSize = 10 };
            tv.SetTextColor(Color.White);
            tv.SetBackgroundColor(Color.ParseColor(colorHex));
            tv.SetPadding(DpToPx(4), DpToPx(2), DpToPx(4), DpToPx(2));
            var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { RightMargin = DpToPx(4) };
            tv.LayoutParameters = lp;
            return tv;
        }

        private int DpToPx(int dp)
        {
            return (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, dp, Resources!.DisplayMetrics);
        }

        // ==========================================================
        // TOUCH CAMERA ORBIT FOR VIEWPORT
        // ==========================================================
        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null || _view == null) return base.OnTouchEvent(e);

            float x = e.GetX();
            float y = e.GetY();
            int rightBoundary = _view.Size.X - DpToPx(260);
            int botBoundary = _view.Size.Y - DpToPx(130);

            // Orbit lang kapag nasa Viewport area nag-touch
            if (x < rightBoundary && (y > DpToPx(70) && y < botBoundary))
            {
                switch (e.ActionMasked)
                {
                    case MotionEventActions.Down:
                        _isOrbiting = true;
                        _touchLastX = x;
                        _touchLastY = y;
                        break;
                    case MotionEventActions.Move:
                        if (_isOrbiting)
                        {
                            float dx = x - _touchLastX;
                            float dy = y - _touchLastY;
                            _camYaw += dx * 0.35f;
                            _camPitch = Math.Clamp(_camPitch - dy * 0.35f, 5.0f, 85.0f);
                            _touchLastX = x;
                            _touchLastY = y;
                        }
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        _isOrbiting = false;
                        break;
                }
            }
            return base.OnTouchEvent(e);
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
    // PROWL ENGINE SCENE & COMPONENTS
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

        public void AttachScript(MonoBehaviour script)
        {
            script.Node = this;
            Scripts.Add(script);
            script.Awake();
            script.Start();
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
