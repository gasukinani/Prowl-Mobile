using System;
using System.Collections.Generic;
using System.Numerics;

namespace Prowl.Runtime
{
    public enum MeshShape { Cube, Tree, Sphere, Plane }
    public enum LightType { Directional, Point, Spot }

    public class Scene
    {
        public string Name { get; set; } = "Untitled Scene";
        public List<ProwlNode> Nodes { get; } = new();

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

        public void Update(float dt, Vector2 joystick, bool isPlaying)
        {
            foreach (var node in Nodes)
            {
                if (node.IsActive) node.Update(dt, joystick, isPlaying);
            }
        }
    }

    public class ProwlNode
    {
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;
        public string Tag { get; set; } = "Untagged";
        public string Layer { get; set; } = "Default";
        public bool IsDynamic { get; set; } = true;
        public Scene Scene { get; }
        public Transform Transform { get; }
        public List<Component> Components { get; } = new();

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

        public Component AddComponentByType(Type type)
        {
            var comp = (Component)Activator.CreateInstance(type)!;
            comp.Node = this;
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

        public void Start()
        {
            foreach (var comp in Components) comp.Start();
        }

        public void Update(float dt, Vector2 joy, bool isPlaying)
        {
            foreach (var comp in Components)
            {
                comp.Update(dt);
                if (isPlaying && comp is ScriptComponent script)
                {
                    script.OnUpdateGame(dt, joy);
                }
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
                   Matrix4x4.CreateFromYawPitchRoll(
                       Rotation.Y * MathF.PI / 180f,
                       Rotation.X * MathF.PI / 180f,
                       Rotation.Z * MathF.PI / 180f) *
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

    public class MeshRendererComponent : Component
    {
        public MeshShape Shape { get; set; } = MeshShape.Cube;
        public Vector3 Color { get; set; } = Vector3.One;
        public string MaterialName { get; set; } = "Standard (Lit)";
    }

    public class LightComponent : Component
    {
        public LightType Type { get; set; } = LightType.Directional;
        public Vector3 Color { get; set; } = new(1.0f, 0.96f, 0.88f);
        public float Intensity { get; set; } = 1.0f;
    }

    public class ScriptComponent : Component
    {
        public string ScriptName { get; set; } = "PlayerController.cs";
        public string SourceCode { get; set; } = @"using System;
using System.Numerics;

public class PlayerController {
    public float Speed = 5.0f;
    public void Update(float dt, Vector2 input, ref Vector3 position, ref Vector3 rotation) {
        position.X += input.X * Speed * dt;
        position.Z -= input.Y * Speed * dt;
        rotation.Y += 45.0f * dt;
    }
}";
        public float Speed { get; set; } = 5.0f;

        public virtual void OnUpdateGame(float dt, Vector2 joy)
        {
            if (Node == null) return;
            var pos = Node.Transform.Position;
            var rot = Node.Transform.Rotation;

            pos.X += joy.X * Speed * dt;
            pos.Z -= joy.Y * Speed * dt;
            rot.Y += 40.0f * dt;

            Node.Transform.Position = pos;
            Node.Transform.Rotation = rot;
        }
    }
}
