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

        // --- Shaders ---
        private uint _standard3DProgram;
        private uint _skyProgram;
        private uint _uiProgram;

        // --- Buffers & VAOs ---
        private uint _vaoCube, _vboCube;
        private uint _vaoFloor, _vboFloor;
        private int _floorVertCount;
        private uint _vaoTree, _vboTree;
        private int _treeVertCount;
        private uint _vaoGizmo, _vboGizmo;
        private int _gizmoVertCount;
        private uint _vaoSky, _vboSky;
        private uint _vaoUI, _vboUI;

        // --- Engine & Editor State ---
        public enum PlayState { EditMode, PlayMode, Paused }
        private PlayState _currentState = PlayState.EditMode;
        private int _activeBottomTab = 0; // 0 = Console, 1 = Project
        private int _activeViewportTab = 0; // 0 = Scene, 1 = Game
        private bool _showPanels = true;

        // --- Scene Graph ---
        private readonly Scene _scene = new Scene();
        private ProwlNode? _selectedNode;
        private ProwlNode? _cubeNode;
        private ProwlNode? _charNode;

        // --- Touch & Navigation ---
        private Vector2 _leftTouchStart, _leftTouchCurrent;
        private bool _isLeftTouching = false;
        private float _rightTouchLastX, _rightTouchLastY;
        private bool _isRightTouching = false;

        // --- Camera Orbit ---
        private float _camYaw = 40.0f;
        private float _camPitch = 24.0f;
        private float _camDistance = 7.5f;
        private Vector3 _camTarget = new Vector3(0, 0.8f, 0);

        // --- Performance Monitor ---
        private int _fps = 60;
        private float _fpsTimer = 0f;
        private int _frames = 0;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try { AssetExtractor.EnsureAssetsExtracted(this); } catch { }

            InitSceneHierarchy();
        }

        private void InitSceneHierarchy()
        {
            // 1. Directional Sun Light
            var sun = _scene.CreateNode("Directional Light");
            var sunLight = sun.AddComponent<LightComponent>();
            sunLight.Type = LightType.Directional;
            sunLight.Color = new Vector3(1.0f, 0.96f, 0.85f);
            sun.Transform.Position = new Vector3(0, 3.5f, 0);

            // 2. Main Selected Cube (gaya ng nasa Prowl Editor reference)
            _cubeNode = _scene.CreateNode("Cube");
            _cubeNode.Transform.Position = new Vector3(0f, 0.5f, 0f);
            var cubeMesh = _cubeNode.AddComponent<MeshRendererComponent>();
            cubeMesh.Shape = MeshShape.Cube;
            cubeMesh.Color = new Vector3(0.15f, 0.15f, 0.18f); // Dark Charcoal Cube with highlights

            // 3. Low-Poly Trees
            var tree1 = _scene.CreateNode("Tree_Alpha");
            tree1.Transform.Position = new Vector3(-2.2f, 0, 1.8f);
            tree1.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            var tree2 = _scene.CreateNode("Tree_Beta");
            tree2.Transform.Position = new Vector3(2.6f, 0, 2.2f);
            tree2.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            var tree3 = _scene.CreateNode("Tree_Small");
            tree3.Transform.Position = new Vector3(1.8f, 0, -1.5f);
            tree3.Transform.Scale = new Vector3(0.7f, 0.7f, 0.7f);
            tree3.AddComponent<MeshRendererComponent>().Shape = MeshShape.Tree;

            // 4. Hero Character Node
            _charNode = _scene.CreateNode("Hero_Runner");
            _charNode.Transform.Position = new Vector3(-1.0f, 0, -0.8f);
            var charMesh = _charNode.AddComponent<MeshRendererComponent>();
            charMesh.Shape = MeshShape.Humanoid;
            charMesh.Color = new Vector3(0.2f, 0.6f, 0.95f);
            _charNode.AttachScript(new PlayerControllerScript());

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

            InitShaders();
            BuildMeshes();
            BuildUIBuffers();
        }

        private void InitShaders()
        {
            if (_gl == null) return;

            // 1. SKY GRADIENT SHADER
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
                // Prowl Engine Atmosphere Gradient (Deep slate blue to warm golden horizon)
                vec3 skyTop = vec3(0.08, 0.10, 0.14);
                vec3 skyMid = vec3(0.18, 0.22, 0.28);
                vec3 horizonGlow = vec3(0.48, 0.35, 0.22);
                
                vec3 col = mix(horizonGlow, skyMid, smoothstep(0.15, 0.55, vUV.y));
                col = mix(col, skyTop, smoothstep(0.55, 0.95, vUV.y));
                FragColor = vec4(col, 1.0);
            }";
            _skyProgram = CreateProgram(skyVS, skyFS);

            // 2. STANDARD 3D LIT SHADER
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
                vec4 worldPos = vec4(aPos, 1.0) * uModel;
                vWorldPos = worldPos.xyz;
                vNorm = (vec4(aNorm, 0.0) * uModel).xyz;
                vCol = aCol;
                gl_Position = worldPos * uView * uProj;
            }";

            string litFS = @"#version 300 es
            precision mediump float;
            in vec3 vWorldPos;
            in vec3 vNorm;
            in vec3 vCol;
            out vec4 FragColor;

            void main() {
                vec3 N = normalize(vNorm);
                vec3 L = normalize(vec3(0.6, 1.2, 0.7)); // Directional Sun
                
                float diff = max(dot(N, L), 0.0);
                vec3 ambient = vec3(0.35, 0.38, 0.45);
                vec3 sunCol = vec3(1.0, 0.95, 0.85);
                
                // Soft fake floor contact shadow for objects around ground level
                float shadow = 1.0;
                if (vWorldPos.y <= 0.05) {
                    float distToCube = length(vWorldPos.xz - vec2(0.0, 0.0));
                    if (distToCube < 0.9) shadow *= smoothstep(0.3, 0.9, distToCube);
                }

                vec3 lighting = (ambient + diff * sunCol * shadow) * vCol;
                FragColor = vec4(lighting, 1.0);
            }";
            _standard3DProgram = CreateProgram(litVS, litFS);

            // 3. 2D EDITOR UI SHADER
            string uiVS = @"#version 300 es
            layout(location = 0) in vec2 aPos;
            layout(location = 1) in vec4 aCol;

            uniform mat4 uOrtho;
            out vec4 vCol;

            void main() {
                vCol = aCol;
                gl_Position = vec4(aPos, 0.0, 1.0) * uOrtho;
            }";

            string uiFS = @"#version 300 es
            precision mediump float;
            in vec4 vCol;
            out vec4 FragColor;

            void main() {
                FragColor = vCol;
            }";
            _uiProgram = CreateProgram(uiVS, uiFS);
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

        private unsafe void BuildMeshes()
        {
            if (_gl == null) return;

            // 1. Fullscreen Quad for Sky
            float[] skyVerts = {
                -1, -1,   1, -1,   1,  1,
                 1,  1,  -1,  1,  -1, -1
            };
            _vaoSky = _gl.GenVertexArray();
            _vboSky = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoSky);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboSky);
            fixed (float* p = skyVerts) {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(skyVerts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            // 2. Checkered Floor Mesh with Prowl Style Crosshairs (+ markers)
            List<float> floorList = new List<float>();
            int gridSize = 14;
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
                    Vector3 tileCol = isEven ? new Vector3(0.82f, 0.84f, 0.88f) : new Vector3(0.32f, 0.35f, 0.40f);

                    // 2 Triangles for Quad tile
                    AddFloorVertex(floorList, x0, 0f, z0, tileCol);
                    AddFloorVertex(floorList, x1, 0f, z0, tileCol);
                    AddFloorVertex(floorList, x1, 0f, z1, tileCol);
                    AddFloorVertex(floorList, x1, 0f, z1, tileCol);
                    AddFloorVertex(floorList, x0, 0f, z1, tileCol);
                    AddFloorVertex(floorList, x0, 0f, z0, tileCol);

                    // Cross Marker at center of tile
                    if ((x + z) % 3 == 0)
                    {
                        Vector3 markerCol = (x % 2 == 0) ? new Vector3(0.95f, 0.25f, 0.25f) : new Vector3(0.95f, 0.85f, 0.20f);
                        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                        float mw = 0.08f, ml = 0.02f;
                        // Horizontal cross bar
                        AddFloorVertex(floorList, cx - mw, 0.002f, cz - ml, markerCol);
                        AddFloorVertex(floorList, cx + mw, 0.002f, cz - ml, markerCol);
                        AddFloorVertex(floorList, cx + mw, 0.002f, cz + ml, markerCol);
                        AddFloorVertex(floorList, cx + mw, 0.002f, cz + ml, markerCol);
                        AddFloorVertex(floorList, cx - mw, 0.002f, cz + ml, markerCol);
                        AddFloorVertex(floorList, cx - mw, 0.002f, cz - ml, markerCol);
                        // Vertical cross bar
                        AddFloorVertex(floorList, cx - ml, 0.002f, cz - mw, markerCol);
                        AddFloorVertex(floorList, cx + ml, 0.002f, cz - mw, markerCol);
                        AddFloorVertex(floorList, cx + ml, 0.002f, cz + mw, markerCol);
                        AddFloorVertex(floorList, cx + ml, 0.002f, cz + mw, markerCol);
                        AddFloorVertex(floorList, cx - ml, 0.002f, cz + mw, markerCol);
                        AddFloorVertex(floorList, cx - ml, 0.002f, cz - mw, markerCol);
                    }
                }
            }

            _floorVertCount = floorList.Count / 9;
            _vaoFloor = CreateVAO(floorList.ToArray());

            // 3. Cube Mesh (Detailed with beveled colors)
            List<float> cubeList = new List<float>();
            AddBoxVertices(cubeList, Vector3.Zero, new Vector3(1f, 1f, 1f), new Vector3(0.12f, 0.14f, 0.18f));
            _vaoCube = CreateVAO(cubeList.ToArray());

            // 4. Low-Poly Tree Mesh (Trunk + Foliage)
            List<float> treeList = new List<float>();
            // Brown Trunk
            AddBoxVertices(treeList, new Vector3(0, 0.5f, 0), new Vector3(0.25f, 1.0f, 0.25f), new Vector3(0.55f, 0.35f, 0.20f));
            // Green Leaves
            AddBoxVertices(treeList, new Vector3(0, 1.35f, 0), new Vector3(1.1f, 1.0f, 1.1f), new Vector3(0.22f, 0.85f, 0.28f));
            AddBoxVertices(treeList, new Vector3(0, 1.95f, 0), new Vector3(0.8f, 0.7f, 0.8f), new Vector3(0.30f, 0.92f, 0.35f));
            _treeVertCount = treeList.Count / 9;
            _vaoTree = CreateVAO(treeList.ToArray());

            // 5. 3D XYZ Transform Gizmo Lines & Bounding Wireframe
            List<float> gizmoList = new List<float>();
            // X Axis (Red)
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.95f, 0.25f, 0.25f,  1.2f, 0.5f, 0f, 0f, 1f, 0f, 0.95f, 0.25f, 0.25f });
            // Y Axis (Green)
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.25f, 0.95f, 0.25f,  0f, 1.7f, 0f, 0f, 1f, 0f, 0.25f, 0.95f, 0.25f });
            // Z Axis (Blue)
            gizmoList.AddRange(new[] { 0f, 0.5f, 0f, 0f, 1f, 0f, 0.25f, 0.55f, 0.95f,  0f, 0.5f, 1.2f, 0f, 1f, 0f, 0.25f, 0.55f, 0.95f });
            // Lightbulb Ring Icon above
            float r = 0.3f;
            for (int i = 0; i < 16; i++)
            {
                float a1 = (i / 16f) * MathF.PI * 2f;
                float a2 = ((i + 1) / 16f) * MathF.PI * 2f;
                gizmoList.AddRange(new[] { MathF.Cos(a1)*r, 3.5f + MathF.Sin(a1)*r, 0f, 0f, 1f, 0f, 1.0f, 0.95f, 0.2f,
                                           MathF.Cos(a2)*r, 3.5f + MathF.Sin(a2)*r, 0f, 0f, 1f, 0f, 1.0f, 0.95f, 0.2f });
            }
            _gizmoVertCount = gizmoList.Count / 9;
            _vaoGizmo = CreateVAO(gizmoList.ToArray());
        }

        private static void AddFloorVertex(List<float> list, float x, float y, float z, Vector3 col)
        {
            list.AddRange(new[] { x, y, z,  0f, 1f, 0f,  col.X, col.Y, col.Z });
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

        private void BuildUIBuffers()
        {
            if (_gl == null) return;
            _vaoUI = _gl.GenVertexArray();
            _vboUI = _gl.GenBuffer();
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
            }

            // Virtual Joystick Input for Scene
            Vector2 joy = Vector2.Zero;
            if (_isLeftTouching)
            {
                Vector2 diff = _leftTouchCurrent - _leftTouchStart;
                if (diff.Length() > 10f)
                    joy = Vector2.Normalize(diff) * Math.Clamp(diff.Length() / 70f, 0f, 1f);
            }

            _scene.Update(dt, joy, _currentState == PlayState.PlayMode);
        }

        private unsafe void OnRender(double delta)
        {
            if (_gl == null || _view == null) return;

            int w = _view.Size.X;
            int h = _view.Size.Y;
            _gl.Viewport(0, 0, (uint)w, (uint)h);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // ==========================================
            // 1. SKYBOX PASS
            // ==========================================
            _gl.Disable(EnableCap.DepthTest);
            _gl.UseProgram(_skyProgram);
            _gl.BindVertexArray(_vaoSky);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

            // ==========================================
            // 2. 3D WORLD VIEWPORT PASS
            // ==========================================
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.Disable(EnableCap.Blend);
            _gl.UseProgram(_standard3DProgram);

            float aspect = (float)w / Math.Max(1, h);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.4f, aspect, 0.1f, 100.0f);

            // Orbit Camera Math
            float radY = _camYaw * MathF.PI / 180f;
            float radP = _camPitch * MathF.PI / 180f;
            float camX = _camTarget.X + _camDistance * MathF.Cos(radP) * MathF.Sin(radY);
            float camY = _camTarget.Y + _camDistance * MathF.Sin(radP);
            float camZ = _camTarget.Z + _camDistance * MathF.Cos(radP) * MathF.Cos(radY);
            var view = Matrix4x4.CreateLookAt(new Vector3(camX, camY, camZ), _camTarget, Vector3.UnitY);

            int locProj = _gl.GetUniformLocation(_standard3DProgram, "uProj");
            int locView = _gl.GetUniformLocation(_standard3DProgram, "uView");
            int locModel = _gl.GetUniformLocation(_standard3DProgram, "uModel");

            _gl.UniformMatrix4(locProj, 1, false, (float*)&proj);
            _gl.UniformMatrix4(locView, 1, false, (float*)&view);

            // Render Checkered Floor
            var floorMat = Matrix4x4.Identity;
            _gl.UniformMatrix4(locModel, 1, false, (float*)&floorMat);
            _gl.BindVertexArray(_vaoFloor);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_floorVertCount);

            // Render Scene Entities
            foreach (var node in _scene.Nodes)
            {
                var mesh = node.GetComponent<MeshRendererComponent>();
                if (mesh == null) continue;

                var model = node.Transform.GetWorldMatrix();
                _gl.UniformMatrix4(locModel, 1, false, (float*)&model);

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

            // Render 3D XYZ Transform Gizmo over Selected Object
            if (_selectedNode != null && _currentState == PlayState.EditMode)
            {
                _gl.Disable(EnableCap.DepthTest);
                var gizmoMat = Matrix4x4.CreateTranslation(_selectedNode.Transform.Position);
                _gl.UniformMatrix4(locModel, 1, false, (float*)&gizmoMat);
                _gl.BindVertexArray(_vaoGizmo);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gizmoVertCount);
            }

            // ==========================================
            // 3. PROWL ENGINE GUI OVERLAY PASS
            // ==========================================
            _gl.Disable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _gl.UseProgram(_uiProgram);
            var ortho = Matrix4x4.CreateOrthographicOffCenter(0, w, h, 0, -1f, 1f);
            int locOrtho = _gl.GetUniformLocation(_uiProgram, "uOrtho");
            _gl.UniformMatrix4(locOrtho, 1, false, (float*)&ortho);

            RenderProwlStudioUI(w, h);
        }

        private unsafe void RenderProwlStudioUI(float screenW, float screenH)
        {
            if (_gl == null) return;

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

            void DrawBorder(float x, float y, float w, float h, float thickness, Vector4 col)
            {
                DrawRect(x, y, w, thickness, col);
                DrawRect(x, y + h - thickness, w, thickness, col);
                DrawRect(x, y, thickness, h, col);
                DrawRect(x + w - thickness, y, thickness, h, col);
            }

            // ----------------------------------------------------
            // 1. TOP STATUS & MENU TOOLBAR (Prowl Sleek Header)
            // ----------------------------------------------------
            DrawRect(0, 0, screenW, 42, new Vector4(0.09f, 0.10f, 0.14f, 0.98f));
            DrawRect(0, 41, screenW, 1, new Vector4(0.18f, 0.20f, 0.26f, 1f));

            // Menu Items: File, Edit, Assets, GameObject, Window
            DrawRect(10, 8, 48, 26, new Vector4(0.14f, 0.16f, 0.22f, 0.9f)); // File
            DrawRect(64, 8, 48, 26, new Vector4(0.14f, 0.16f, 0.22f, 0.9f)); // Edit
            DrawRect(118, 8, 60, 26, new Vector4(0.14f, 0.16f, 0.22f, 0.9f)); // Assets
            DrawRect(184, 8, 86, 26, new Vector4(0.14f, 0.16f, 0.22f, 0.9f)); // GameObject

            // Center Play / Pause / Step Controls
            float midX = screenW * 0.5f;
            DrawRect(midX - 55, 6, 110, 30, new Vector4(0.06f, 0.07f, 0.10f, 0.95f));
            DrawBorder(midX - 55, 6, 110, 30, 1, new Vector4(0.22f, 0.26f, 0.35f, 1f));

            // Play Toggle Button (Green if PlayMode)
            Vector4 playCol = (_currentState == PlayState.PlayMode) ? new Vector4(0.18f, 0.85f, 0.45f, 1f) : new Vector4(0.85f, 0.88f, 0.92f, 0.9f);
            DrawRect(midX - 45, 12, 18, 18, playCol);
            // Pause Button
            DrawRect(midX - 15, 12, 16, 18, new Vector4(0.6f, 0.65f, 0.75f, 0.9f));
            // Step Button
            DrawRect(midX + 15, 12, 18, 18, new Vector4(0.6f, 0.65f, 0.75f, 0.9f));

            // Right Info Chips (FPS Counter, Version, Project Tag)
            DrawRect(screenW - 240, 8, 85, 26, new Vector4(0.12f, 0.15f, 0.20f, 0.95f)); // 212 FPS
            DrawRect(screenW - 235, 16, 10, 10, new Vector4(0.18f, 0.85f, 0.45f, 1f)); // Green Dot
            DrawRect(screenW - 145, 8, 80, 26, new Vector4(0.12f, 0.15f, 0.20f, 0.95f)); // v1.0-mobile
            DrawRect(screenW - 55, 8, 45, 26, new Vector4(0.18f, 0.55f, 0.95f, 0.95f)); // Project

            // ----------------------------------------------------
            // 2. VIEWPORT TABS & TOOLSTRIP
            // ----------------------------------------------------
            // Tabs: [# Scene] [Game] [Preferences]
            DrawRect(10, 48, 85, 28, (_activeViewportTab == 0) ? new Vector4(0.18f, 0.22f, 0.30f, 0.95f) : new Vector4(0.10f, 0.12f, 0.16f, 0.8f));
            DrawRect(100, 48, 75, 28, (_activeViewportTab == 1) ? new Vector4(0.18f, 0.22f, 0.30f, 0.95f) : new Vector4(0.10f, 0.12f, 0.16f, 0.8f));

            // Left Toolstrip (Move, Rotate, Scale gizmo tools)
            DrawRect(10, 85, 36, 140, new Vector4(0.09f, 0.10f, 0.14f, 0.90f));
            DrawBorder(10, 85, 36, 140, 1, new Vector4(0.20f, 0.24f, 0.32f, 0.9f));
            DrawRect(15, 92, 26, 26, new Vector4(0.18f, 0.55f, 0.95f, 0.9f)); // Move Active Tool
            DrawRect(15, 126, 26, 26, new Vector4(0.14f, 0.16f, 0.22f, 0.7f)); // Rotate
            DrawRect(15, 160, 26, 26, new Vector4(0.14f, 0.16f, 0.22f, 0.7f)); // Scale
            DrawRect(15, 194, 26, 26, new Vector4(0.14f, 0.16f, 0.22f, 0.7f)); // Rect

            // 3D Orientation Cube Gizmo at Top Right of Viewport
            float cubeGizmoX = screenW - 325, cubeGizmoY = 55;
            DrawRect(cubeGizmoX, cubeGizmoY, 32, 32, new Vector4(0.18f, 0.55f, 0.95f, 0.85f));
            DrawRect(cubeGizmoX + 16, cubeGizmoY, 16, 16, new Vector4(0.25f, 0.85f, 0.35f, 0.9f));
            DrawRect(cubeGizmoX, cubeGizmoY + 16, 16, 16, new Vector4(0.95f, 0.25f, 0.25f, 0.9f));

            if (_showPanels)
            {
                // ----------------------------------------------------
                // 3. RIGHT DOCK: HIERARCHY & INSPECTOR
                // ----------------------------------------------------
                float rightW = 275;
                float rightX = screenW - rightW;

                // Panel Background
                DrawRect(rightX, 42, rightW, screenH - 42, new Vector4(0.08f, 0.09f, 0.13f, 0.96f));
                DrawRect(rightX, 42, 1, screenH - 42, new Vector4(0.18f, 0.20f, 0.28f, 1f));

                // === HIERARCHY HEADER ===
                DrawRect(rightX, 42, rightW, 30, new Vector4(0.11f, 0.13f, 0.18f, 1f));
                // Search Bar
                DrawRect(rightX + 10, 78, rightW - 20, 26, new Vector4(0.05f, 0.06f, 0.09f, 0.95f));
                DrawBorder(rightX + 10, 78, rightW - 20, 26, 1, new Vector4(0.18f, 0.22f, 0.30f, 1f));

                // Hierarchy Tree Items
                float treeY = 112;
                for (int i = 0; i < Math.Min(4, _scene.Nodes.Count); i++)
                {
                    var n = _scene.Nodes[i];
                    bool isSel = (n == _selectedNode);
                    Vector4 rowCol = isSel ? new Vector4(0.22f, 0.38f, 0.65f, 0.95f) : new Vector4(0.11f, 0.13f, 0.18f, 0.50f);
                    DrawRect(rightX + 6, treeY + (i * 28), rightW - 12, 25, rowCol);
                    // Eye Visibility Icon
                    DrawRect(rightX + rightW - 28, treeY + (i * 28) + 6, 14, 13, new Vector4(0.5f, 0.55f, 0.65f, 0.8f));
                }

                // === INSPECTOR SECTION ===
                float inspY = 240;
                DrawRect(rightX, inspY, rightW, 30, new Vector4(0.11f, 0.13f, 0.18f, 1f));
                DrawBorder(rightX, inspY, rightW, 30, 1, new Vector4(0.16f, 0.19f, 0.25f, 1f));

                // Selected Object Chip (Cube [Dynamic])
                DrawRect(rightX + 10, inspY + 38, rightW - 20, 32, new Vector4(0.12f, 0.14f, 0.20f, 0.95f));
                DrawRect(rightX + 18, inspY + 48, 12, 12, new Vector4(0.18f, 0.55f, 0.95f, 1f)); // Blue Checkbox

                // Transform Foldout Header
                DrawRect(rightX + 10, inspY + 78, rightW - 20, 24, new Vector4(0.14f, 0.16f, 0.22f, 0.9f));

                // Transform Inputs (Position X/Y/Z)
                float propY = inspY + 108;
                DrawRect(rightX + 10, propY, 55, 22, new Vector4(0.10f, 0.11f, 0.16f, 0.9f));
                // X (Red Accent)
                DrawRect(rightX + 70, propY, 55, 22, new Vector4(0.18f, 0.10f, 0.12f, 0.95f));
                // Y (Green Accent)
                DrawRect(rightX + 130, propY, 55, 22, new Vector4(0.10f, 0.18f, 0.12f, 0.95f));
                // Z (Blue Accent)
                DrawRect(rightX + 190, propY, 55, 22, new Vector4(0.10f, 0.14f, 0.22f, 0.95f));

                // MeshRenderer Foldout
                DrawRect(rightX + 10, propY + 32, rightW - 20, 24, new Vector4(0.14f, 0.16f, 0.22f, 0.9f));

                // + Add Component Button
                DrawRect(rightX + 25, propY + 65, rightW - 50, 30, new Vector4(0.14f, 0.17f, 0.24f, 0.95f));
                DrawBorder(rightX + 25, propY + 65, rightW - 50, 30, 1, new Vector4(0.24f, 0.30f, 0.42f, 1f));

                // ----------------------------------------------------
                // 4. BOTTOM DOCK: PROJECT & CONSOLE DRAWER
                // ----------------------------------------------------
                float botH = 145;
                float botY = screenH - botH;
                float botW = screenW - rightW;

                DrawRect(0, botY, botW, botH, new Vector4(0.07f, 0.08f, 0.12f, 0.96f));
                DrawRect(0, botY, botW, 1, new Vector4(0.18f, 0.20f, 0.28f, 1f));

                // Bottom Tabs: [Project] [Console]
                DrawRect(10, botY + 4, 85, 26, (_activeBottomTab == 0) ? new Vector4(0.16f, 0.19f, 0.26f, 1f) : new Vector4(0.09f, 0.10f, 0.14f, 0.7f));
                DrawRect(100, botY + 4, 85, 26, (_activeBottomTab == 1) ? new Vector4(0.16f, 0.19f, 0.26f, 1f) : new Vector4(0.09f, 0.10f, 0.14f, 0.7f));

                if (_activeBottomTab == 0)
                {
                    // Console Messages
                    DrawRect(15, botY + 36, botW - 30, 24, new Vector4(0.10f, 0.12f, 0.17f, 0.9f));
                    DrawRect(22, botY + 42, 10, 10, new Vector4(0.18f, 0.55f, 0.95f, 1f)); // Info icon
                    DrawRect(15, botY + 64, botW - 30, 24, new Vector4(0.10f, 0.12f, 0.17f, 0.6f));
                    DrawRect(22, botY + 70, 10, 10, new Vector4(0.95f, 0.75f, 0.20f, 1f)); // Warning icon
                }
                else
                {
                    // Project Asset Folder Cards
                    for (int f = 0; f < 5; f++)
                    {
                        DrawRect(20 + (f * 75), botY + 40, 65, 55, new Vector4(0.12f, 0.14f, 0.20f, 0.95f));
                        DrawRect(30 + (f * 75), botY + 48, 24, 18, new Vector4(0.95f, 0.75f, 0.20f, 0.9f)); // Folder icon
                    }
                }
            }

            // ----------------------------------------------------
            // 5. TOUCH JOYSTICK (Left Bottom Screen)
            // ----------------------------------------------------
            if (_isLeftTouching)
            {
                DrawRect(_leftTouchStart.X - 50, _leftTouchStart.Y - 50, 100, 100, new Vector4(1f, 1f, 1f, 0.20f));
                DrawRect(_leftTouchCurrent.X - 22, _leftTouchCurrent.Y - 22, 44, 44, new Vector4(0.2f, 0.65f, 1.0f, 0.90f));
            }

            // Upload Buffer at Render 2D GUI
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
                        // Top Header Play Controls Tap
                        if (y < 45)
                        {
                            float midX = _view.Size.X * 0.5f;
                            if (x > midX - 60 && x < midX + 60)
                            {
                                _currentState = (_currentState == PlayState.EditMode) ? PlayState.PlayMode : PlayState.EditMode;
                                Log.Info(LogTag, $"State: {_currentState}");
                                return true;
                            }
                        }

                        // Bottom Drawer Tab Switch
                        if (y > _view.Size.Y - 145 && x < _view.Size.X - 275)
                        {
                            if (x > 10 && x < 95) _activeBottomTab = 0;
                            else if (x > 100 && x < 185) _activeBottomTab = 1;
                            return true;
                        }

                        // Left Side Joystick / Camera
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

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _gl?.DeleteProgram(_standard3DProgram);
            _gl?.DeleteProgram(_skyProgram);
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

    public enum MeshShape { Cube, Tree, Humanoid }
    public class MeshRendererComponent : Component
    {
        public MeshShape Shape { get; set; } = MeshShape.Cube;
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

    public class PlayerControllerScript : MonoBehaviour
    {
        public float Speed = 4.0f;

        public override void UpdateWithInput(float dt, Vector2 joy)
        {
            if (joy.LengthSquared() > 0.01f)
            {
                Vector3 dir = new Vector3(joy.X, 0, joy.Y);
                Transform.Translate(dir * Speed * dt);
                float yaw = MathF.Atan2(dir.X, dir.Z) * (180f / MathF.PI);
                Transform.Rotation = new Vector3(0, yaw, 0);
            }
        }
    }
}
