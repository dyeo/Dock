using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// <b>When used with a class</b>, makes a <see cref="MonoBehaviour"/> or <see cref="ScriptableObject"/> dockable visible to the <see cref="Dock"/> so that it may be dependency injected.
/// <b>When used with a field</b>, makes the Dock automatically assign a dockable dockable based on the type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Interface)]
public class DockAttribute : Attribute { }

/// <summary>
/// The <see cref="Dock"/> is a simple <see cref="UnityEngine.Object"/> dependency manager that can be added to any scene.
/// It will automatically collect all <see cref="MonoBehaviour"/> and <see cref="ScriptableObject"/> instances with the <see cref="DockAttribute"/> attribute (so-called "dockable objects").
/// The Dock also possesses its own dockable instantiation functions which will automatically collect dockable objects.
/// </summary>
/// <remarks>
/// The Dock should be the first thing initialized in a scene. Make sure to set it to the beginning of Unity's script execution order.
/// Before the Dock has begun its initialization, it will fire an OnDockPreInitialized message on its own GameObject which can be used by your own scripts.
/// Once the Dock has finished its initialization, it will fire an OnDockInitialized message on its own GameObject which can be used by your own scripts.
/// </remarks>
[DisallowMultipleComponent]
public class Dock : MonoBehaviour
{
	#region Fields

	private static Dock instance;

	private static IEnumerable<Assembly> loadedAssemblies;
	private static IEnumerable<Type> dockableTypes;

	private static Dictionary<Type, IEnumerable<MemberInfo>> connections = new Dictionary<Type, IEnumerable<MemberInfo>>();

	private readonly Dictionary<Type, IEnumerable<UnityEngine.Object>> dockables = new Dictionary<Type, IEnumerable<UnityEngine.Object>>();
	
	private const BindingFlags dockAssignableBindingFlags = BindingFlags.Instance | BindingFlags.Static |
															BindingFlags.Public | BindingFlags.NonPublic |
															BindingFlags.SetField | BindingFlags.SetProperty;

	#endregion
	

	#region Reloading

	/// <summary>
	/// Retrieves all user-defined assemblies and all plugin assemblies in this project for the purposes of finding dockable types.
	/// </summary>
	public static void ReloadAssemblies()
	{
		loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(isValidAssembly);
	}

	/// <summary>
	/// Scans the assemblies currently recognized by the dock for a list of all dockable types.
	/// </summary>
	/// <remarks>
	/// This function does not refresh the loaded assemblies. If you need to scan new assemblies, use <see cref="ReloadAssemblies"/> first.
	/// </remarks>
	public static void ReloadTypes()
	{
		IEnumerable<Type> loadedTypes = loadedAssemblies
			.SelectMany(a => a.GetTypes());
		
		dockableTypes = loadedTypes
			.Where(isValidDockable);

		connections = loadedTypes
			.Where(isDockInteractable)
			.SelectMany(getDockAssignables)
			.GroupBy(f => f.DeclaringType)
			.ToDictionary(p => p.Key, p => p.AsEnumerable());
	}

	/// <summary>
	/// Reloads all dockables stored in the dock, discarding unloaded dockables and adding newly loaded dockables.
	/// </summary>
	/// <remarks>
	/// This function calls FindObjectsOfType once for every dockable type, which is known to be a slow operation.
	/// It is not recommended to call ReloadDockables frequently.
	/// </remarks>
	public static void ReloadDockables()
	{
		IEnumerable<UnityEngine.Object> allObjects = FindObjectsOfType<ScriptableObject>();
		Scene activeScene = SceneManager.GetActiveScene();
		GameObject[] rootObjects = activeScene.GetRootGameObjects();
		allObjects = rootObjects.Aggregate(allObjects, (current, obj) => current.Union(obj.GetComponentsInChildren<MonoBehaviour>(true)));
		updateDockables(allObjects);
		updateDockConnections(allObjects);
	}

	#endregion


	#region Dockable Management

	/// <summary>
	/// Connects an dockable not previously connected to the dock to it.
	/// <b>If the dockable is a <see cref="MonoBehaviour"/> or a <see cref="ScriptableObject"/></b>, will link all assignable members marked with a <see cref="DockAttribute"/> to the dockable stored in the dock.
	/// <b>If the dockable is a <see cref="GameObject"/></b>, will link all child <see cref="MonoBehaviour"/>s to the dock recursively in the manner described above.
	/// </summary>
	/// <param name="dockable">The dockable dockable to connect to the dock.</param>
	public static void Connect(UnityEngine.Object dockable) 
		=> updateObjectDockConnections(dockable);

	/// <summary>
	/// Adds a dockable of a given type to the dock to be connected later.
	/// </summary>
	/// <typeparam name="T">The type of the given dockable.</typeparam>
	/// <param name="dockable">The dockable instance to add.</param>
	public static void Add<T>(T dockable) => instance.appendDockable(typeof(T), dockable as UnityEngine.Object);

	#endregion


	#region Dockable Retrieval

	/// <summary>
	/// Returns the first loaded dockable dockable of a given type.
	/// </summary>
	/// <param name="type">The type of dockable dockable to return.</param>
	/// <returns>A dockable dockable dockable, or <c>null</c> if none was found.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="type"/> is <c>null</c>.</exception>
	/// <exception cref="KeyNotFoundException"><paramref name="type"/> is not a valid dockable type.</exception>
	public static UnityEngine.Object Get(Type type) 
		=> instance.dockables[type].FirstOrDefault();

	/// <summary>
	/// Returns the first loaded dockable dockable.
	/// </summary>
	/// <typeparam name="T">The type of dockable dockable to return.</typeparam>
	/// <returns>A dockable dockable dockable, or <c>null</c> if none was found.</returns>
	/// <exception cref="KeyNotFoundException"><typeparamref name="T"/> is not a valid dockable type.</exception>
	public static T Get<T>()
		=> instance.dockables[typeof(T)].Cast<T>().FirstOrDefault();

	/// <summary>
	/// Returns the first loaded dockable dockable of a given type.
	/// </summary>
	/// <param name="type">The type of dockable dockable to return.</param>
	/// <param name="predicate">A predicate for filtering dockables of the given type.</param>
	/// <returns>A dockable dockable dockable, or <c>null</c> if none was found.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="type"/> is <c>null</c>.</exception>
	/// <exception cref="KeyNotFoundException"><paramref name="type"/> is not a valid dockable type.</exception>
	public static UnityEngine.Object Get(Type type, Func<UnityEngine.Object, bool> predicate)
		=> instance.dockables[type].Where(predicate).FirstOrDefault();

	/// <summary>
	/// Returns the first loaded dockable dockable.
	/// </summary>
	/// <param name="predicate">A predicate for filtering dockables of the given type.</param>
	/// <typeparam name="T">The type of dockable dockable to return.</typeparam>
	/// <returns>A dockable dockable dockable, or <c>null</c> if none was found.</returns>
	/// <exception cref="KeyNotFoundException"><typeparamref name="T"/> is not a valid dockable type.</exception>
	public static T Get<T>(Func<T, bool> predicate) 
		=> instance.dockables[typeof(T)].Cast<T>().Where(predicate).FirstOrDefault();

	/// <summary>
	/// Returns all dockable objects of a given type.
	/// </summary>
	/// <param name="type">The type of dockable dockable to return.</param>
	/// <returns>An array of dockable dockable instances, or an empty array if none were found.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="type"/> is <c>null</c>.</exception>
	/// <exception cref="KeyNotFoundException"><paramref name="type"/> is not a valid dockable type.</exception>
	public static UnityEngine.Object[] GetAll(Type type) 
		=> instance.dockables[type].ToArray();

	/// <summary>
	/// Returns all dockable objects of a given type.
	/// </summary>
	/// <typeparam name="T">The type of dockable dockable to return.</typeparam>
	/// <returns>An array of dockable dockable instances, or an empty array if none were found.</returns>
	/// <exception cref="KeyNotFoundException"><typeparamref name="T"/> is not a valid dockable type.</exception>
	public static T[] GetAll<T>() 
		=> instance.dockables[typeof(T)].Cast<T>().ToArray();

	/// <summary>
	/// Returns all dockable objects of a given type.
	/// </summary>
	/// <param name="type">The type of dockable dockable to return.</param>
	/// <param name="predicate">A predicate for filtering dockables of the given type.</param>
	/// <returns>An array of dockable dockable instances, or an empty array if none were found.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="type"/> is <c>null</c>.</exception>
	/// <exception cref="KeyNotFoundException"><paramref name="type"/> is not a valid dockable type.</exception>
	public static UnityEngine.Object[] GetAll(Type type, Func<UnityEngine.Object, bool> predicate)
		=> instance.dockables[type].Where(predicate).ToArray();

	/// <summary>
	/// Returns all dockable objects of a given type.
	/// </summary>
	/// <typeparam name="T">The type of dockable dockable to return.</typeparam>
	/// <param name="predicate">A predicate for filtering dockables of the given type.</param>
	/// <returns>An array of dockable dockable instances, or an empty array if none were found.</returns>
	/// <exception cref="KeyNotFoundException"><typeparamref name="T"/> is not a valid dockable type.</exception>
	public static T[] GetAll<T>(Func<T, bool> predicate)
		=> instance.dockables[typeof(T)].Cast<T>().Where(predicate).ToArray();

	#endregion


	#region GameObject Instantiation

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <returns>The instantiated clone.</returns>
	public new static UnityEngine.Object Instantiate(UnityEngine.Object original)
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original));

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object, Transform)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="parent">Parent that will be assigned to the new object.</param>
	/// <returns>The instantiated clone.</returns>
	public new static UnityEngine.Object Instantiate(UnityEngine.Object original, Transform parent)
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, parent));

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object, Transform, bool)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="parent">Parent that will be assigned to the new object.</param>
	/// <param name="instantiateInWorldSpace">When you assign a parent Object, pass <c>true</c> to position the new object directly in world space. Pass <c>false</c> to set the Object’s position relative to its new parent.</param>
	/// <returns>The instantiated clone.</returns>
	public new static UnityEngine.Object Instantiate(UnityEngine.Object original, Transform parent, bool instantiateInWorldSpace)
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, parent, instantiateInWorldSpace));

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object, Vector3, Quaternion)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="position">Position for the new object.</param>
	/// <param name="rotation">Orientation of the new object.</param>
	/// <returns>The instantiated clone.</returns>
	public new static UnityEngine.Object Instantiate(UnityEngine.Object original, Vector3 position, Quaternion rotation)
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, position, rotation));

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object, Vector3, Quaternion, Transform)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="position">Position for the new object.</param>
	/// <param name="rotation">Orientation of the new object.</param>
	/// <param name="parent">Parent that will be assigned to the new object.</param>
	/// <returns>The instantiated clone.</returns>
	public new static UnityEngine.Object Instantiate(UnityEngine.Object original, Vector3 position, Quaternion rotation, Transform parent)
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, position, rotation, parent));

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate{T}(T)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <returns>The instantiated clone.</returns>
	public new static T Instantiate<T>(T original) where T : UnityEngine.Object
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original)) as T;

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate{T}(T, Transform)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="parent">Parent that will be assigned to the new object.</param>
	/// <returns>The instantiated clone.</returns>
	public new static T Instantiate<T>(T original, Transform parent) where T : UnityEngine.Object
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, parent)) as T;

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate{T}(T, Transform, bool)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="parent">Parent that will be assigned to the new object.</param>
	/// <param name="instantiateInWorldSpace">When you assign a parent Object, pass <c>true</c> to position the new object directly in world space. Pass <c>false</c> to set the Object’s position relative to its new parent.</param>
	/// <returns>The instantiated clone.</returns>
	public new static T Instantiate<T>(T original, Transform parent, bool instantiateInWorldSpace) where T : UnityEngine.Object
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, parent, instantiateInWorldSpace)) as T;

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate{T}(T, Vector3, Quaternion)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="position">Position for the new object.</param>
	/// <param name="rotation">Orientation of the new object.</param>
	/// <returns>The instantiated clone.</returns>
	public new static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : UnityEngine.Object
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, position, rotation)) as T;

	/// <summary>
	/// This method behaves exactly the same as <see cref="UnityEngine.Object.Instantiate{T}(T, Vector3, Quaternion, Transform)"/>, except that it connects any fields or properties on scripts within the instantiated object that are marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="original">An existing object that you want to make a copy of.</param>
	/// <param name="position">Position for the new object.</param>
	/// <param name="rotation">Orientation of the new object.</param>
	/// <param name="parent">Parent that will be assigned to the new object.</param>
	/// <returns>The instantiated clone.</returns>
	public new static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform parent) where T : UnityEngine.Object
		=> updateObjectDockConnections(UnityEngine.Object.Instantiate(original, position, rotation, parent)) as T;

	#endregion


	#region ScriptableObject Instnatiation

	/// <summary>
	/// This method behaves exactly the same as <see cref="ScriptableObject.CreateInstance(string)"/>, except that it connects any fields or properties within the object marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <param name="className">The type of the <see cref="ScriptableObject"/> to create, as the name of the type.</param>
	/// <returns>The created <see cref="ScriptableObject"/>.</returns>
	public static ScriptableObject CreateInstance(string className)
		=> updateObjectDockConnections(ScriptableObject.CreateInstance(className)) as ScriptableObject;

	/// <summary>
	/// This method behaves exactly the same as <see cref="ScriptableObject.CreateInstance(Type)"/>, except that it connects any fields or properties within the object marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <param name="type">The type of the <see cref="ScriptableObject"/> to create, as a <see cref="Type"/> instance.</param>
	/// <returns>The created <see cref="ScriptableObject"/>.</returns>
	public static ScriptableObject CreateInstance(Type type)
		=> updateObjectDockConnections(ScriptableObject.CreateInstance(type)) as ScriptableObject;

	/// <summary>
	/// This method behaves exactly the same as <see cref="ScriptableObject.CreateInstance{T}()"/>, except that it connects any fields or properties within the object marked with the <see cref="DockAttribute"/> attribute.
	/// </summary>
	/// <typeparam name="T">The type of the <see cref="ScriptableObject"/> to create.</typeparam>
	/// <returns>The created <see cref="ScriptableObject"/>.</returns>
	public static T CreateInstance<T>() where T : ScriptableObject
		=> updateObjectDockConnections(ScriptableObject.CreateInstance<T>()) as T;

	#endregion


	#region Unity Events

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void initializeRuntime()
	{
		ReloadAssemblies();
		ReloadTypes();
	}

	private void Awake()
	{
		#if UNITY_EDITOR
		if (instance != null)
		{
			throw new InvalidOperationException("[Dock] More than one Dock exists in the current scene. Make sure you only have one Dock per scene and that it doesn't persist between scenes.");
		}
		#endif
		instance = this;
		initialize();
	}

	private void OnDisable() => finalize();

	#endregion


	#region Private Methods

	private static void initialize()
	{
		instance.gameObject.SendMessage("OnDockPreInitialized", null, SendMessageOptions.DontRequireReceiver);
		ReloadDockables();
		instance.gameObject.SendMessage("OnDockInitialized", null, SendMessageOptions.DontRequireReceiver);
	}

	private static void finalize()
	{
		instance = null;
	}

	private static bool isValidAssembly(Assembly assembly)
		=> !disallowedAssemblies.Contains(assembly.GetName().Name);

	private static bool isValidDockable(Type type)
		=> type.GetCustomAttribute<DockAttribute>() != null;

	private static bool isDockInteractable(Type type)
		=> typeof(UnityEngine.Object).IsAssignableFrom(type);

	private static IEnumerable<MemberInfo> getDockAssignables(Type type)
		=> type.GetMembers(dockAssignableBindingFlags).Where(member => member.GetCustomAttribute<DockAttribute>() != null);
	
	private static void updateDockables(IEnumerable<UnityEngine.Object> availableObjects)
	{
		foreach (Type type in dockableTypes)
		{
			IEnumerable<UnityEngine.Object> objects = availableObjects.Where(o => type.IsAssignableFrom(o.GetType()));
			instance.appendDockables(type, objects);
		}
	}

	private static void updateDockConnections(IEnumerable<UnityEngine.Object> availableObjects)
	{
		foreach (KeyValuePair<Type, IEnumerable<MemberInfo>> pair in connections)
		{
			foreach (UnityEngine.Object o in availableObjects.Where(o => pair.Key.IsAssignableFrom(o.GetType())))
			{
				foreach (MemberInfo m in pair.Value)
				{
					connectObjectMember(o, m);
				}
			}
		}
	}

	private static UnityEngine.Object updateObjectDockConnections(UnityEngine.Object obj)
	{
		IEnumerable<MemberInfo> members = connections.Where(pair => pair.Key.IsAssignableFrom(obj.GetType())).SelectMany(pair => pair.Value);

		if (obj is GameObject go)
		{
			foreach (Component o in go.GetComponentsInChildren<Component>())
			{
				updateObjectDockConnections(o);
			}
		}
		else if (members.Any())
		{
			foreach (MemberInfo member in members)
			{
				connectObjectMember(obj, member);
			}
		}
		return obj;
	}

	private static void connectObjectMember(UnityEngine.Object obj, MemberInfo member)
	{
		switch (member)
		{
			case PropertyInfo property:
				connectObjectProperty(obj, property);
				break;
			case FieldInfo field:
				connectObjectField(obj, field);
				break;
		}
	}

	private static void connectObjectProperty(UnityEngine.Object obj, PropertyInfo property)
	{
		Type type = property.PropertyType;
		#if UNITY_EDITOR
		if (!dockableTypes.Contains(type) && !dockableTypes.Contains(type.GetElementType()))
		{
			throw new InvalidOperationException($"[Dock] Cannot dock property of type {type}: type is not marked as dockable.");
		}
		#endif
		if (type.IsArray)
		{
			UnityEngine.Object[] dockables = GetAll(type.GetElementType());
			IList array = Activator.CreateInstance(type, dockables.Length) as IList;

			for (int i = 0; i < array.Count; i++)
			{
				array[i] = dockables[i];
			}

			property.SetValue(obj, array);
		}
		else if (typeof(IList).IsAssignableFrom(type))
		{
			IList list = Activator.CreateInstance(type) as IList;

			foreach (UnityEngine.Object dockable in GetAll(type.GetGenericArguments()[0]))
			{
				list.Add(dockable);
			}

			property.SetValue(obj, list);
		}
		else
		{
			property.SetValue(obj, Get(type));
		}
	}

	private static void connectObjectField(UnityEngine.Object obj, FieldInfo field)
	{
		Type type = field.FieldType;
		#if UNITY_EDITOR
		if (!dockableTypes.Contains(type) && !dockableTypes.Contains(type.GetElementType()))
		{
			throw new InvalidOperationException($"[Dock] Cannot dock property of type {type}: type is not marked as dockable.");
		}
		#endif
		if (type.IsArray)
		{
			UnityEngine.Object[] dockables = GetAll(type.GetElementType());
			IList array = Activator.CreateInstance(type, dockables.Length) as IList;

			for (int i = 0; i < array.Count; i++)
			{
				array[i] = dockables[i];
			}

			field.SetValue(obj, array);
		}
		else if (typeof(IList).IsAssignableFrom(type))
		{
			IList list = Activator.CreateInstance(type) as IList;

			foreach (UnityEngine.Object dockable in GetAll(type.GetGenericArguments()[0]))
			{
				list.Add(dockable);
			}

			field.SetValue(obj, list);
		}
		else
		{
			field.SetValue(obj, Get(type));
		}
	}

	private void appendDockables(Type type, IEnumerable<UnityEngine.Object> dockablesEnumerable)
	{
		if (dockables.TryGetValue(type, out IEnumerable<UnityEngine.Object> existingDockables))
		{
			dockables[type] = existingDockables.Union(dockablesEnumerable);
		}
		else
		{
			instance.dockables[type] = dockablesEnumerable.Any() ? dockablesEnumerable : new UnityEngine.Object[0];
		}
	}

	private void appendDockable(Type type, UnityEngine.Object dockable)
	{
		if (dockables.TryGetValue(type, out IEnumerable<UnityEngine.Object> existingDockables))
		{
			dockables[type] = existingDockables.Append(dockable);
		}
		else
		{
			dockables[type] = toEnumerable(dockable);
		}
	}

	#endregion

	/// <summary>
	/// Converts any value to a single-element IEnumerable of the same value.
	/// </summary>
	private static IEnumerable<T> toEnumerable<T>(T value)
	{
		yield return value;
	}

	/// <summary>
	/// These are all the default assemblies loaded by Unity (as of 2019.3.13f1). We don't need to check these since we know they don't contain any dockables.
	/// </summary>
	private static readonly HashSet<string> disallowedAssemblies = new HashSet<string>
	{
		"mscorlib",
		"UnityEngine",
		"UnityEngine.AIModule",
		"UnityEngine.ARModule",
		"UnityEngine.AccessibilityModule",
		"UnityEngine.AndroidJNIModule",
		"UnityEngine.AnimationModule",
		"UnityEngine.AssetBundleModule",
		"UnityEngine.AudioModule",
		"UnityEngine.ClothModule",
		"UnityEngine.ClusterInputModule",
		"UnityEngine.ClusterRendererModule",
		"UnityEngine.CoreModule",
		"UnityEngine.CrashReportingModule",
		"UnityEngine.DSPGraphModule",
		"UnityEngine.DirectorModule",
		"UnityEngine.GameCenterModule",
		"UnityEngine.GridModule",
		"UnityEngine.HotReloadModule",
		"UnityEngine.IMGUIModule",
		"UnityEngine.ImageConversionModule",
		"UnityEngine.InputModule",
		"UnityEngine.InputLegacyModule",
		"UnityEngine.JSONSerializeModule",
		"UnityEngine.LocalizationModule",
		"UnityEngine.ParticleSystemModule",
		"UnityEngine.PerformanceReportingModule",
		"UnityEngine.PhysicsModule",
		"UnityEngine.Physics2DModule",
		"UnityEngine.ProfilerModule",
		"UnityEngine.ScreenCaptureModule",
		"UnityEngine.SharedInternalsModule",
		"UnityEngine.SpriteMaskModule",
		"UnityEngine.SpriteShapeModule",
		"UnityEngine.StreamingModule",
		"UnityEngine.SubstanceModule",
		"UnityEngine.SubsystemsModule",
		"UnityEngine.TLSModule",
		"UnityEngine.TerrainModule",
		"UnityEngine.TerrainPhysicsModule",
		"UnityEngine.TextCoreModule",
		"UnityEngine.TextRenderingModule",
		"UnityEngine.TilemapModule",
		"UnityEngine.UIModule",
		"UnityEngine.UIElementsModule",
		"UnityEngine.UNETModule",
		"UnityEngine.UmbraModule",
		"UnityEngine.UnityAnalyticsModule",
		"UnityEngine.UnityConnectModule",
		"UnityEngine.UnityTestProtocolModule",
		"UnityEngine.UnityWebRequestModule",
		"UnityEngine.UnityWebRequestAssetBundleModule",
		"UnityEngine.UnityWebRequestAudioModule",
		"UnityEngine.UnityWebRequestTextureModule",
		"UnityEngine.UnityWebRequestWWWModule",
		"UnityEngine.VFXModule",
		"UnityEngine.VRModule",
		"UnityEngine.VehiclesModule",
		"UnityEngine.VideoModule",
		"UnityEngine.WindModule",
		"UnityEngine.XRModule",
		"UnityEditor",
		"System.Core",
		"System",
		"Unity.CompilationPipeline.Common",
		"UnityEditor.VR",
		"UnityEditor.Graphs",
		"UnityEditor.WindowsStandalone.Extensions",
		"UnityEditor.WebGL.Extensions",
		"UnityEditor.OSXStandalone.Extensions",
		"UnityEditor.LinuxStandalone.Extensions",
		"UnityEditor.Android.Extensions",
		"UnityEditor.UWP.Extensions",
		"UnityEditor.iOS.Extensions",
		"UnityEditor.iOS.Extensions.Xcode",
		"UnityEditor.iOS.Extensions.Common",
		"SyntaxTree.VisualStudio.Unity.Bridge",
		"Unity.Timeline.Editor",
		"Unity.VSCode.Editor",
		"Unity.TextMeshPro.Editor",
		"UnityEngine.UI",
		"Unity.Timeline",
		"Unity.CollabProxy.Editor",
		"UnityEditor.TestRunner",
		"UnityEngine.TestRunner",
		"Unity.Rider.Editor",
		"Unity.TextMeshPro",
		"UnityEditor.UI",
		"nunit.framework",
		"System.Xml",
		"System.Xml.Linq",
		"SyntaxTree.VisualStudio.Unity.Messaging",
		"Unity.Cecil",
		"Unity.SerializationLogic",
		"ExCSS.Unity",
		"Unity.Legacy.NRefactory"
	};
}