using System;
using System.Collections.Generic;
using System.Numerics;

namespace Prowl.Runtime
{
    public enum MeshShape { Cube, Tree, Humanoid }
    public enum LightType { Directional, Point }

    public class Scene
    {
        public List<ProwlNode> Nodes { get; } = new List<ProwlNode>();

        public ProwlNode CreateNode(string name)
        {
            var node = new ProwlNode(this, name);
            Nodes.Add(node);
            return node;
        }

        public void Start()
        {
            foreach (var node in Nodes) node.Start();
        }

        public void Update(float dt, Vector2 joy, bool isPlaying)
        {
            foreach (var node in Nodes) node.Update(dt, joy, isPlaying);
        }
    }

    public class ProwlNode
    {
        public string Name { get; set; }
        public Scene Scene { get; }
        public Transform Transform { get; }
        public List<Component> Components { get; } = new List<Component>();
        public List<MonoBehaviour> Scripts { get; } = new List<MonoBehaviour>();

        public ProwlNode(Scene scene, string name)
        {
            Scene = scene;
            Name = name;
            Transform = new Transform(this);
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var comp = new T { Node = this };
            Components.Add(comp);
            comp.Awake();
            return comp;
        }

        public T? GetComponent<T>() where T : Component
        {
            foreach (var comp in Components)
            {
                if (comp is T match) return match;
            }
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
            foreach (var comp in Components) comp.Start();
            foreach (var script in Scripts) script.Start();
        }

        public void Update(float dt, Vector2 joy, bool isPlaying)
        {
            foreach (var comp in Components) comp.Update(dt);
            foreach (var script in Scripts)
            {
                if (isPlaying) script.UpdateWithInput(dt, joy);
            }
        }
    }

    public class Transform
    {
        public ProwlNode Node { get; }
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Rotation { get; set; } = Vector3.Zero;
        public Vector3 Scale { get; set; } = Vector3.One;

        public Transform(ProwlNode node) { Node = node; }

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

    public class MeshRendererComponent : Component
    {
        public MeshShape Shape { get; set; } = MeshShape.Cube;
        public Vector3 Color { get; set; } = Vector3.One;
    }

    public class LightComponent : Component
    {
        public LightType Type { get; set; } = LightType.Directional;
        public Vector3 Color { get; set; } = Vector3.One;
    }
}
