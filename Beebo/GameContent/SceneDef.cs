using System.Collections.Generic;
using System.Text.Json;
using Jelly;
using Jelly.GameContent;
using Jelly.Utilities;

namespace Beebo.GameContent;

public class SceneDef : ContentDef
{
    public IList<EntityDef> Entities { get; set; } = [];

    public Scene Build()
    {
        var scene = new Scene(Name.GetHashCode())
        {
            Name = Name,
        };

        foreach(var e in Entities ?? [])
        {
            e.Create(scene);
        }

        return scene;
    }

    public static explicit operator SceneDef(Scene scene) => new SceneDef
    {
        Entities = [.. GetEntityDefs(scene.Entities)],
        Name = scene.Name,
    };

    private static IList<EntityDef> GetEntityDefs(EntityList entities)
    {
        IList<EntityDef> list = [];

        var _entities = entities.ToArray();

        foreach(var entity in _entities)
        {
            list.Add((EntityDef)entity);
        }

        return list;
    }

    public override string ToString()
    {
        return Serialize(true);
    }

    public string Serialize(bool pretty = false)
    {
        var options = RegistryManager.SerializerOptions;
        options.WriteIndented = pretty;

        return JsonSerializer.Serialize(this, options);
    }

    public static SceneDef? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<SceneDef>(json, RegistryManager.SerializerOptions);
    }
}

public static class SceneExtensions
{
    public static string Serialize(this Scene scene, bool pretty = false)
    {
        var options = RegistryManager.SerializerOptions;
        options.WriteIndented = pretty;

        return JsonSerializer.Serialize((SceneDef)scene, options);
    }

    public static string Serialize(this Component component, bool pretty = false)
    {
        var options = RegistryManager.SerializerOptions;
        options.WriteIndented = pretty;

        return JsonSerializer.Serialize(component, options);
    }
}
