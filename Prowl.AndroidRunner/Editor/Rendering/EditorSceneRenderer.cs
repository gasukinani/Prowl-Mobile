using System;
using System.Collections.Generic;
using System.Numerics;
using Prowl.Runtime;
using Silk.NET.OpenGLES;

namespace Prowl.AndroidRunner.Editor.Rendering
{
    public class EditorSceneRenderer : IDisposable
    {
        private readonly GL _gl;
        private uint _standard3DProgram;
        private uint _skyProgram;
        private uint _billboardProgram;
        private uint _vaoSky, _vaoFloor, _vaoCube, _vaoTree, _vaoGizmo, _vaoBoxOutline, _vaoLightGizmo;
        private int _floorVertCount, _treeVertCount, _gizmoVertCount;

        public EditorSceneRenderer(GL gl)
        {
            _gl = gl;
            InitShaders();
            BuildMeshes();
        }

        private void InitShaders()
        {
            // 1. Sky Shader
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
                vec3 skyTop = vec3(0.08, 0.10, 0.15);
                vec3 skyMid = vec3(0.18, 0.22, 0.29);
                vec3 horizonGlow = vec3(0.58, 0.42, 0.24);
                vec3 col = mix(horizonGlow, skyMid, smoothstep(0.10, 0.45, vUV.y));
                col = mix(col, skyTop, smoothstep(0.45, 0.95, vUV.y));
                FragColor = vec4(col, 1.0);
            }";
            _skyProgram = CompileProgram(skyVS, skyFS);

            // 2. 3D World Lit Shader
            string litVS = @"#version 300 es
            layout(location = 0) in vec3 aPos;
            layout(location = 1) in vec3 aNorm;
            layout(location = 2) in vec3 aCol;
            uniform mat4 uModel, uView, uProj;
            out vec3 vWorldPos, vNorm, vCol;
            void main() {
                vec4 worldPos = uModel * vec4(aPos, 1.0);
                vWorldPos = worldPos.xyz;
                vNorm = mat3(uModel) * aNorm;
                vCol = aCol;
                gl_Position = uProj * uView * worldPos;
            }";

            string litFS = @"#version 300 es
            precision mediump float;
            in vec3 vWorldPos, vNorm, vCol;
            out vec4 FragColor;
            void main() {
                vec3 N = normalize(vNorm);
                vec3 L = normalize(vec3(0.6, 1.4, 0.7));
                float diff = max(dot(N, L), 0.0);
                vec3 ambient = vec3(0.38, 0.40, 0.46);
                vec3 sunCol = vec3(1.0, 0.96, 0.88);
                FragColor = vec4((ambient + diff * sunCol) * vCol, 1.0);
            }";
            _standard3DProgram = CompileProgram(litVS, litFS);

            // 3. Gizmo Billboard Shader
            string billVS = @"#version 300 es
            layout(location = 0) in vec3 aPos;
            layout(location = 1) in vec3 aCol;
            uniform mat4 uModel, uView, uProj;
            out vec3 vCol;
            void main() {
                vCol = aCol;
                gl_Position = uProj * uView * uModel * vec4(aPos, 1.0);
            }";

            string billFS = @"#version 300 es
            precision mediump float;
            in vec3 vCol;
            out vec4 FragColor;
            void main() {
                FragColor = vec4(vCol, 1.0);
            }";
            _billboardProgram = CompileProgram(billVS, billFS);
        }

        private unsafe void BuildMeshes()
        {
            // Sky Quad
            float[] skyVerts = { -1, -1, 1, -1, 1, 1, 1, 1, -1, 1, -1, -1 };
            _vaoSky = _gl.GenVertexArray();
            uint vboSky = _gl.GenBuffer();
            _gl.BindVertexArray(_vaoSky);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vboSky);
            fixed (float* p = skyVerts) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(skyVerts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            // Floor Grid with Red/Yellow Markers (matching Screenshot 2)
            var floorList = new List<float>();
            int gridSize = 24; float step = 1.0f, start = -gridSize * step * 0.5f;
            for (int x = 0; x < gridSize; x++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    float x0 = start + x * step, z0 = start + z * step;
                    float x1 = x0 + step, z1 = z0 + step;
                    bool isEven = ((x + z) % 2 == 0);
                    Vector3 tileCol = isEven ? new Vector3(0.85f, 0.86f, 0.89f) : new Vector3(0.35f, 0.38f, 0.44f);
                    AddTile(floorList, x0, z0, x1, z1, tileCol);

                    if ((x + z) % 3 == 0)
                    {
                        Vector3 markerCol = (x % 2 == 0) ? new Vector3(0.95f, 0.25f, 0.25f) : new Vector3(0.95f, 0.85f, 0.20f);
                        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                        AddTile(floorList, cx - 0.08f, cz - 0.018f, cx + 0.08f, cz + 0.018f, markerCol, 0.002f);
                        AddTile(floorList, cx - 0.018f, cz - 0.08f, cx + 0.018f, cz + 0.08f, markerCol, 0.002f);
                    }
                }
            }
            _floorVertCount = floorList.Count / 9;
            _vaoFloor = CreateVAO(floorList.ToArray());

            // Cube Mesh
            var cubeList = new List<float>();
            AddBox(cubeList, Vector3.Zero, Vector3.One, new Vector3(0.12f, 0.14f, 0.18f));
            _vaoCube = CreateVAO(cubeList.ToArray());

            // Tree Mesh
            var treeList = new List<float>();
            AddBox(treeList, new Vector3(0, 0.5f, 0), new Vector3(0.25f, 1.0f, 0.25f), new Vector3(0.55f, 0.35f, 0.20f));
            AddBox(treeList, new Vector3(0, 1.35f, 0), new Vector3(1.1f, 1.0f, 1.1f), new Vector3(0.22f, 0.85f, 0.28f));
            AddBox(treeList, new Vector3(0, 1.95f, 0), new Vector3(0.8f, 0.7f, 0.8f), new Vector3(0.30f, 0.92f, 0.35f));
            _treeVertCount = treeList.Count / 9;
            _vaoTree = CreateVAO(treeList.ToArray());

            // XYZ Translation Gizmo
            var gizmoList = new List<float> {
                0,0,0, 0,1,0, 0.95f,0.25f,0.25f,  1.2f,0,0, 0,1,0, 0.95f,0.25f,0.25f,
                0,0,0, 0,1,0, 0.25f,0.95f,0.25f,  0,1.2f,0, 0,1,0, 0.25f,0.95f,0.25f,
                0,0,0, 0,1,0, 0.25f,0.55f,0.95f,  0,0,1.2f, 0,1,0, 0.25f,0.55f,0.95f
            };
            _gizmoVertCount = gizmoList.Count / 9;
            _vaoGizmo = CreateVAO(gizmoList.ToArray());

            // Box Wireframe Selection Outline
            var boxLines = new List<float>();
            AddBoxLines(boxLines, Vector3.Zero, new Vector3(1.02f, 1.02f, 1.02f), new Vector3(0.22f, 0.52f, 0.98f));
            _vaoBoxOutline = CreateVAO(boxLines.ToArray());

            // Light Bulb Ring Gizmo
            var lightLines = new List<float>();
            int segments = 24; float r = 0.35f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * MathF.PI * 2f;
                lightLines.AddRange(new[] {
                    MathF.Cos(a0)*r, MathF.Sin(a0)*r, 0, 0,1,0, 0.95f,0.85f,0.20f,
                    MathF.Cos(a1)*r, MathF.Sin(a1)*r, 0, 0,1,0, 0.95f,0.85f,0.20f
                });
            }
            lightLines.AddRange(new[] {
                0f, -0.35f, 0f, 0,1,0, 0.95f,0.85f,0.20f, 0f, -0.6f, 0f, 0,1,0, 0.95f,0.85f,0.20f
            });
            _vaoLightGizmo = CreateVAO(lightLines.ToArray());
        }

        public unsafe void Render(int width, int height, float fov, float camDistance, float camYaw, float camPitch, Vector3 camTarget, Scene scene, ProwlNode? selectedNode, bool isEditMode)
        {
            _gl.Viewport(0, 0, (uint)width, (uint)height);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // 1. Sky Pass
            _gl.Disable(EnableCap.DepthTest);
            _gl.DepthMask(false);
            _gl.UseProgram(_skyProgram);
            _gl.BindVertexArray(_vaoSky);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

            // 2. 3D World Pass
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(true);
            _gl.UseProgram(_standard3DProgram);

            float aspect = (float)width / Math.Max(1, height);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov * (MathF.PI / 180f), aspect, 0.05f, 300f);

            // Natural Orbit Camera Position
            float radY = camYaw * MathF.PI / 180f;
            float radP = camPitch * MathF.PI / 180f;
            Vector3 camPos = camTarget + new Vector3(
                camDistance * MathF.Cos(radP) * MathF.Sin(radY),
                camDistance * MathF.Sin(radP),
                camDistance * MathF.Cos(radP) * MathF.Cos(radY)
            );
            var view = Matrix4x4.CreateLookAt(camPos, camTarget, Vector3.UnitY);

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

            // Meshes
            foreach (var node in scene.Nodes)
            {
                if (!node.IsActive) continue;
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

            // 3. Selection Box Outline & Gizmos
            if (isEditMode && selectedNode != null)
            {
                _gl.Disable(EnableCap.DepthTest);
                var selMat = Matrix4x4.Transpose(selectedNode.Transform.GetWorldMatrix());
                _gl.UniformMatrix4(locModel, 1, false, (float*)&selMat);
                _gl.BindVertexArray(_vaoBoxOutline);
                _gl.DrawArrays(PrimitiveType.Lines, 0, 24);

                // XYZ Arrows
                var gizmoMat = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(selectedNode.Transform.Position));
                _gl.UniformMatrix4(locModel, 1, false, (float*)&gizmoMat);
                _gl.BindVertexArray(_vaoGizmo);
                _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gizmoVertCount);

                // Light Bulb Icon (if light)
                if (selectedNode.GetComponent<LightComponent>() != null)
                {
                    _gl.BindVertexArray(_vaoLightGizmo);
                    _gl.DrawArrays(PrimitiveType.Lines, 0, 50);
                }
            }
        }

        private unsafe uint CreateVAO(float[] data)
        {
            uint vao = _gl.GenVertexArray();
            uint vbo = _gl.GenBuffer();
            _gl.BindVertexArray(vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            fixed (float* p = data) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            uint stride = 9 * sizeof(float);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            return vao;
        }

        private static void AddTile(List<float> list, float x0, float z0, float x1, float z1, Vector3 col, float y = 0f)
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

        private static void AddBox(List<float> v, Vector3 c, Vector3 s, Vector3 col)
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
                c.X-x, c.Y+y, c.Z+z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y+y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,  c.X-x, c.Y-y, c.Z-z, -1,0,0,  col.X*0.8f,col.Y*0.8f,col.Z*0.8f,
                c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
                c.X+x, c.Y+y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z+z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,  c.X+x, c.Y-y, c.Z-z,  1,0,0,  col.X*0.9f,col.Y*0.9f,col.Z*0.9f,
            };
            v.AddRange(r);
        }

        private static void AddBoxLines(List<float> list, Vector3 c, Vector3 s, Vector3 col)
        {
            float x = s.X * 0.5f, y = s.Y * 0.5f, z = s.Z * 0.5f;
            Vector3[] p = {
                new(c.X-x, c.Y-y, c.Z-z), new(c.X+x, c.Y-y, c.Z-z),
                new(c.X+x, c.Y-y, c.Z+z), new(c.X-x, c.Y-y, c.Z+z),
                new(c.X-x, c.Y+y, c.Z-z), new(c.X+x, c.Y+y, c.Z-z),
                new(c.X+x, c.Y+y, c.Z+z), new(c.X-x, c.Y+y, c.Z+z),
            };
            int[] idx = { 0,1, 1,2, 2,3, 3,0,  4,5, 5,6, 6,7, 7,4,  0,4, 1,5, 2,6, 3,7 };
            foreach (int i in idx)
            {
                list.AddRange(new[] { p[i].X, p[i].Y, p[i].Z,  0,1,0,  col.X, col.Y, col.Z });
            }
        }

        private uint CompileProgram(string vs, string fs)
        {
            uint v = _gl.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(v, vs);
            _gl.CompileShader(v);

            uint f = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(f, fs);
            _gl.CompileShader(f);

            uint p = _gl.CreateProgram();
            _gl.AttachShader(p, v);
            _gl.AttachShader(p, f);
            _gl.LinkProgram(p);

            _gl.DeleteShader(v);
            _gl.DeleteShader(f);
            return p;
        }

        public void Dispose()
        {
            _gl.DeleteProgram(_standard3DProgram);
            _gl.DeleteProgram(_skyProgram);
            _gl.DeleteProgram(_billboardProgram);
        }
    }
}
