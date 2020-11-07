# Dock

## Simple Runtime Dependancy Injection for Unity Engine

The **Dock** is a single MonoBehaviour that loads before any other script in the scene. It connects instances of MonoBehaviour- and ScriptableObject-derived types marked with the `[Dock]` attribute to fields and properties in other loaded MonoBehaviour and ScriptableObject instances that are marked with the same attribute.

Unity Engine has a built-in dependancy injection scheme in the form of **editor references**. Anyone who has worked on a large Unity project can tell you that editor references frequently break, and refactoring them is a pain point for many teams. While project asset references cannot be so easily eliminated, runtime assets such as MonoBehaviour and ScriptableObject references much more easily can, and with a greater degree of type safety than Unity's DI scheme allows.

## How to Use

1. Add `Dock.cs` in your project's code.
2. Set the script execution order so that the `Dock` MonoBehaviour runs before any other script in your game.
3. Add the `Dock` to a GameObject in the scenes you wish to be managed.
    - If you have a GameManager script, attach the dock on the same GameObject. The Dock fires a `OnDockPreInitialized` message and a `OnDockInitialized` message on its own GameObject which your GameManager can listen for.
4. On types whose instances you would like to be managed by the dock, add the `[Dock]` attribute.
5. On scripts which reference dockable type instances, mark the fields with the `[Dock]` attribute.
    - The Dock matches all derived classes *and* interfaces. So you can mark an interface with the `[Dock]` attribute and retrieve its implementations.

## Example

Attach the `[Dock]` attribute to a type with instances that exist in the scene.
```csharp
[Dock] // dockable type
public class MyScript : MonoBehaviour
{
    // your code here
}
```

Attach the `[Dock]` attribute to fields or properties in other scripts.
```csharp
public class Player : MonoBehaviour // works with ScriptableObject too
{
    // a prefab containing scripts with dock references
    public GameObject somePrefab;
    
    [Dock] // dock reference
    public MyScript myInstance; // first instance of MySript that exists on scene load will be assigned
    [Dock]
    public MyScript[] myInstances; // all instances of MyScript that exist on scene load will be collected and assigned here
    
    public void Start()
    {
	// will hook up any [Dock]-marked fields or properties in prefab
	// supports all the same signatures as unity
        Dock.Instantiate(somePrefab);
        // also supported
        Dock.CreateInstance<SomeScriptableObject>();
        // reloads all dockable type instances and re-establishes connections
        // this function is very costly; do avoid if possible
        Dock.Reload();
        // you can also manually get dockable type instances
        Dock.Get<MyScript>(); // returns first loaded MyScript instance
        Dock.GetAll<MyScript>(); // returns all loaded MyScript instances
    }
}
```
