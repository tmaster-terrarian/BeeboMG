using System.Text.Json.Serialization;

using Jelly.Serialization;

namespace Jelly;

[JsonAutoPolymorphic]
public abstract class Component
{
    internal Entity entity;

    internal bool enabled;

    [JsonIgnore]
    public Entity Entity
    {
        get => entity;

        private set
        {
            if(ReferenceEquals(value, entity) || value == entity)
            {
                return;
            }

            entity = value;
        }
    }

    public bool Enabled
    {
        get => enabled;
        set {
            if(enabled == value)
                return;

            enabled = value;

            if(enabled)
                OnEnable();
            else
                OnDisable();
        }
    }

    public bool Visible { get; set; } = true;

    [JsonIgnore] public Scene? Scene => Entity?.Scene;

    /// <summary>
    /// Ensure that the zero-argument constructor is preserved in all subtypes, to avoid netcode problems.
    /// </summary>
    public Component() : this(true, true) {}

    public Component(bool enabled, bool visible)
    {
        Enabled = enabled;
        Visible = visible;
    }

    public virtual void Added(Entity entity)
    {
        Entity = entity;
    }

    public virtual void Removed(Entity entity)
    {
        Entity = null;
    }

    public virtual void EntityAwake() {}

    public virtual void EntityAdded(Scene scene) {}

    public virtual void EntityRemoved(Scene scene) {}

    public virtual void OnEnable() {}

    public virtual void OnDisable() {}

    public virtual void Update() {}

    /// <summary>
    /// Will not be called if the running instance is headless or if the component is not visible
    /// </summary>
    public virtual void PreDraw() {}

    /// <summary>
    /// Will not be called if the running instance is headless or if the component is not visible
    /// </summary>
    public virtual void Draw() {}

    /// <summary>
    /// Will not be called if the running instance is headless or if the component is not visible
    /// </summary>
    public virtual void PostDraw() {}

    /// <summary>
    /// Will not be called if the running instance is headless or if the component is not visible
    /// </summary>
    public virtual void DrawUI() {}

    public virtual void SceneEnd(Scene scene) {}

    public void RemoveSelf()
    {
        Entity?.RemoveComponent(this);
    }
}
