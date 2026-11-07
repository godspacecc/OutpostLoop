using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

	// {{ AOT assemblies
	public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
	{
		"Newtonsoft.Json.dll",
		"System.Core.dll",
		"UnityEngine.CoreModule.dll",
		"UnityEngine.JSONSerializeModule.dll",
		"UnityEngine.UI.dll",
		"YooAsset.dll",
		"mscorlib.dll",
	};
	// }}

	// {{ constraint implement type
	// }}

	// {{ AOT generic types
	// System.Action<UnityEngine.Bounds>
	// System.Action<UnityEngine.BoundsInt>
	// System.Action<UnityEngine.Color32>
	// System.Action<UnityEngine.Color>
	// System.Action<UnityEngine.EventSystems.RaycastResult>
	// System.Action<UnityEngine.Quaternion>
	// System.Action<UnityEngine.RangeInt>
	// System.Action<UnityEngine.Rect>
	// System.Action<UnityEngine.RectInt>
	// System.Action<UnityEngine.Vector2>
	// System.Action<UnityEngine.Vector2Int>
	// System.Action<UnityEngine.Vector3>
	// System.Action<UnityEngine.Vector3Int>
	// System.Action<UnityEngine.Vector4>
	// System.Action<byte>
	// System.Action<double>
	// System.Action<float>
	// System.Action<int,object>
	// System.Action<int>
	// System.Action<long>
	// System.Action<object,int>
	// System.Action<object,object,object>
	// System.Action<object,object>
	// System.Action<object>
	// System.Collections.Generic.ArraySortHelper<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.ArraySortHelper<float>
	// System.Collections.Generic.ArraySortHelper<int>
	// System.Collections.Generic.ArraySortHelper<object>
	// System.Collections.Generic.Comparer<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.Comparer<float>
	// System.Collections.Generic.Comparer<int>
	// System.Collections.Generic.Comparer<object>
	// System.Collections.Generic.Dictionary.Enumerator<System.ValueTuple<object,object>,object>
	// System.Collections.Generic.Dictionary.Enumerator<int,Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.Dictionary.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.Enumerator<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.Dictionary.Enumerator<object,Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.Dictionary.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<System.ValueTuple<object,object>,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.Dictionary.KeyCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.KeyCollection<System.ValueTuple<object,object>,object>
	// System.Collections.Generic.Dictionary.KeyCollection<int,Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.Dictionary.KeyCollection<int,object>
	// System.Collections.Generic.Dictionary.KeyCollection<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.Dictionary.KeyCollection<object,Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.Dictionary.KeyCollection<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<System.ValueTuple<object,object>,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.Dictionary.ValueCollection.Enumerator<object,object>
	// System.Collections.Generic.Dictionary.ValueCollection<System.ValueTuple<object,object>,object>
	// System.Collections.Generic.Dictionary.ValueCollection<int,Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.Dictionary.ValueCollection<int,object>
	// System.Collections.Generic.Dictionary.ValueCollection<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.Dictionary.ValueCollection<object,Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.Dictionary.ValueCollection<object,object>
	// System.Collections.Generic.Dictionary<System.ValueTuple<object,object>,object>
	// System.Collections.Generic.Dictionary<int,Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.Dictionary<int,object>
	// System.Collections.Generic.Dictionary<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.Dictionary<object,Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.Dictionary<object,object>
	// System.Collections.Generic.EqualityComparer<Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.EqualityComparer<Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.EqualityComparer<Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.EqualityComparer<System.ValueTuple<object,object>>
	// System.Collections.Generic.EqualityComparer<int>
	// System.Collections.Generic.EqualityComparer<object>
	// System.Collections.Generic.HashSet.Enumerator<object>
	// System.Collections.Generic.HashSet<object>
	// System.Collections.Generic.HashSetEqualityComparer<object>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,Game.TimerMgr.TimerEntry>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,Game.ResourceUpdateNoticeContent>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,Game.SafeAreaFitter.LayoutSnapshot>>
	// System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.ICollection<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.ICollection<float>
	// System.Collections.Generic.ICollection<int>
	// System.Collections.Generic.ICollection<object>
	// System.Collections.Generic.IComparer<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IComparer<float>
	// System.Collections.Generic.IComparer<int>
	// System.Collections.Generic.IComparer<object>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,Game.TimerMgr.TimerEntry>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,Game.ResourceUpdateNoticeContent>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,Game.SafeAreaFitter.LayoutSnapshot>>
	// System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerable<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IEnumerable<float>
	// System.Collections.Generic.IEnumerable<int>
	// System.Collections.Generic.IEnumerable<object>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,Game.TimerMgr.TimerEntry>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<int,object>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,Game.ResourceUpdateNoticeContent>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,Game.SafeAreaFitter.LayoutSnapshot>>
	// System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<object,object>>
	// System.Collections.Generic.IEnumerator<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IEnumerator<float>
	// System.Collections.Generic.IEnumerator<int>
	// System.Collections.Generic.IEnumerator<object>
	// System.Collections.Generic.IEqualityComparer<System.ValueTuple<object,object>>
	// System.Collections.Generic.IEqualityComparer<int>
	// System.Collections.Generic.IEqualityComparer<object>
	// System.Collections.Generic.IList<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.IList<float>
	// System.Collections.Generic.IList<int>
	// System.Collections.Generic.IList<object>
	// System.Collections.Generic.IReadOnlyCollection<float>
	// System.Collections.Generic.IReadOnlyCollection<object>
	// System.Collections.Generic.IReadOnlyDictionary<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.IReadOnlyList<float>
	// System.Collections.Generic.IReadOnlyList<object>
	// System.Collections.Generic.KeyValuePair<System.ValueTuple<object,object>,object>
	// System.Collections.Generic.KeyValuePair<int,Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.KeyValuePair<int,object>
	// System.Collections.Generic.KeyValuePair<object,Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.KeyValuePair<object,Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.KeyValuePair<object,object>
	// System.Collections.Generic.List.Enumerator<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.List.Enumerator<float>
	// System.Collections.Generic.List.Enumerator<int>
	// System.Collections.Generic.List.Enumerator<object>
	// System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.List<float>
	// System.Collections.Generic.List<int>
	// System.Collections.Generic.List<object>
	// System.Collections.Generic.ObjectComparer<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.Generic.ObjectComparer<float>
	// System.Collections.Generic.ObjectComparer<int>
	// System.Collections.Generic.ObjectComparer<object>
	// System.Collections.Generic.ObjectEqualityComparer<Game.ResourceUpdateNoticeContent>
	// System.Collections.Generic.ObjectEqualityComparer<Game.SafeAreaFitter.LayoutSnapshot>
	// System.Collections.Generic.ObjectEqualityComparer<Game.TimerMgr.TimerEntry>
	// System.Collections.Generic.ObjectEqualityComparer<System.ValueTuple<object,object>>
	// System.Collections.Generic.ObjectEqualityComparer<int>
	// System.Collections.Generic.ObjectEqualityComparer<object>
	// System.Collections.Generic.Stack.Enumerator<object>
	// System.Collections.Generic.Stack<object>
	// System.Collections.ObjectModel.ReadOnlyCollection<UnityEngine.EventSystems.RaycastResult>
	// System.Collections.ObjectModel.ReadOnlyCollection<float>
	// System.Collections.ObjectModel.ReadOnlyCollection<int>
	// System.Collections.ObjectModel.ReadOnlyCollection<object>
	// System.Comparison<UnityEngine.EventSystems.RaycastResult>
	// System.Comparison<float>
	// System.Comparison<int>
	// System.Comparison<object>
	// System.Func<UnityEngine.Bounds,UnityEngine.Bounds,byte>
	// System.Func<UnityEngine.BoundsInt,UnityEngine.BoundsInt,byte>
	// System.Func<UnityEngine.Color,UnityEngine.Color,byte>
	// System.Func<UnityEngine.Color32,UnityEngine.Color32,byte>
	// System.Func<UnityEngine.Quaternion,UnityEngine.Quaternion,byte>
	// System.Func<UnityEngine.RangeInt,UnityEngine.RangeInt,byte>
	// System.Func<UnityEngine.Rect,UnityEngine.Rect,byte>
	// System.Func<UnityEngine.RectInt,UnityEngine.RectInt,byte>
	// System.Func<UnityEngine.Vector2,UnityEngine.Vector2,byte>
	// System.Func<UnityEngine.Vector2Int,UnityEngine.Vector2Int,byte>
	// System.Func<UnityEngine.Vector3,UnityEngine.Vector3,byte>
	// System.Func<UnityEngine.Vector3Int,UnityEngine.Vector3Int,byte>
	// System.Func<UnityEngine.Vector4,UnityEngine.Vector4,byte>
	// System.Func<byte>
	// System.Func<double,double,byte>
	// System.Func<float,float,byte>
	// System.Func<int,int,byte>
	// System.Func<long,long,byte>
	// System.Func<object,byte>
	// System.Func<object,int,int,object>
	// System.Func<object,object,byte>
	// System.Func<object,object>
	// System.Func<object>
	// System.IEquatable<object>
	// System.Linq.Buffer<object>
	// System.Linq.Enumerable.<CastIterator>d__99<object>
	// System.Linq.Enumerable.Iterator<object>
	// System.Linq.Enumerable.WhereArrayIterator<object>
	// System.Linq.Enumerable.WhereEnumerableIterator<object>
	// System.Linq.Enumerable.WhereListIterator<object>
	// System.Linq.Enumerable.WhereSelectArrayIterator<object,object>
	// System.Linq.Enumerable.WhereSelectEnumerableIterator<object,object>
	// System.Linq.Enumerable.WhereSelectListIterator<object,object>
	// System.Linq.EnumerableSorter<object,object>
	// System.Linq.EnumerableSorter<object>
	// System.Linq.OrderedEnumerable.<GetEnumerator>d__1<object>
	// System.Linq.OrderedEnumerable<object,object>
	// System.Linq.OrderedEnumerable<object>
	// System.Nullable<int>
	// System.Predicate<UnityEngine.EventSystems.RaycastResult>
	// System.Predicate<float>
	// System.Predicate<int>
	// System.Predicate<object>
	// System.ValueTuple<object,object>
	// UnityEngine.EventSystems.ExecuteEvents.EventFunction<object>
	// UnityEngine.Events.InvokableCall<UnityEngine.Vector2>
	// UnityEngine.Events.InvokableCall<float>
	// UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene>
	// UnityEngine.Events.UnityAction<UnityEngine.Vector2>
	// UnityEngine.Events.UnityAction<float>
	// UnityEngine.Events.UnityEvent<UnityEngine.Vector2>
	// UnityEngine.Events.UnityEvent<float>
	// UnityEngine.Pool.CollectionPool.<>c<object,object>
	// UnityEngine.Pool.CollectionPool<object,object>
	// }}

	public void RefMethods()
	{
		// System.Nullable<int> Newtonsoft.Json.Linq.Extensions.Convert<object,System.Nullable<int>>(object)
		// byte Newtonsoft.Json.Linq.Extensions.Convert<object,byte>(object)
		// object Newtonsoft.Json.Linq.Extensions.Convert<object,object>(object)
		// byte Newtonsoft.Json.Linq.Extensions.Value<byte>(System.Collections.Generic.IEnumerable<Newtonsoft.Json.Linq.JToken>)
		// byte Newtonsoft.Json.Linq.Extensions.Value<object,byte>(System.Collections.Generic.IEnumerable<object>)
		// object Newtonsoft.Json.Linq.Extensions.Value<object,object>(System.Collections.Generic.IEnumerable<object>)
		// object Newtonsoft.Json.Linq.Extensions.Value<object>(System.Collections.Generic.IEnumerable<Newtonsoft.Json.Linq.JToken>)
		// System.Nullable<int> Newtonsoft.Json.Linq.JToken.Value<System.Nullable<int>>(object)
		// object System.Activator.CreateInstance<object>()
		// System.Collections.ObjectModel.ReadOnlyCollection<float> System.Array.AsReadOnly<float>(float[])
		// byte[] System.Array.Empty<byte>()
		// object[] System.Array.Empty<object>()
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Cast<object>(System.Collections.IEnumerable)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.CastIterator<object>(System.Collections.IEnumerable)
		// object System.Linq.Enumerable.FirstOrDefault<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
		// System.Linq.IOrderedEnumerable<object> System.Linq.Enumerable.OrderByDescending<object,object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,object>,System.Collections.Generic.IComparer<object>)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Select<object,object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,object>)
		// object[] System.Linq.Enumerable.ToArray<object>(System.Collections.Generic.IEnumerable<object>)
		// System.Collections.Generic.List<object> System.Linq.Enumerable.ToList<object>(System.Collections.Generic.IEnumerable<object>)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Where<object>(System.Collections.Generic.IEnumerable<object>,System.Func<object,bool>)
		// System.Collections.Generic.IEnumerable<object> System.Linq.Enumerable.Iterator<object>.Select<object>(System.Func<object,object>)
		// object& System.Runtime.CompilerServices.Unsafe.As<object,object>(object&)
		// System.Void* System.Runtime.CompilerServices.Unsafe.AsPointer<object>(object&)
		// object UnityEngine.Component.GetComponent<object>()
		// object UnityEngine.Component.GetComponentInChildren<object>(bool)
		// bool UnityEngine.EventSystems.ExecuteEvents.CanHandleEvent<object>(UnityEngine.GameObject)
		// bool UnityEngine.EventSystems.ExecuteEvents.Execute<object>(UnityEngine.GameObject,UnityEngine.EventSystems.BaseEventData,UnityEngine.EventSystems.ExecuteEvents.EventFunction<object>)
		// UnityEngine.GameObject UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<object>(UnityEngine.GameObject)
		// System.Void UnityEngine.EventSystems.ExecuteEvents.GetEventList<object>(UnityEngine.GameObject,System.Collections.Generic.IList<UnityEngine.EventSystems.IEventSystemHandler>)
		// bool UnityEngine.EventSystems.ExecuteEvents.ShouldSendToComponent<object>(UnityEngine.Component)
		// object UnityEngine.GameObject.AddComponent<object>()
		// object UnityEngine.GameObject.GetComponent<object>()
		// System.Void UnityEngine.GameObject.GetComponents<object>(System.Collections.Generic.List<object>)
		// object UnityEngine.JsonUtility.FromJson<object>(string)
		// object UnityEngine.Object.FindObjectOfType<object>()
		// object[] UnityEngine.Object.FindObjectsOfType<object>(bool)
		// object UnityEngine.Object.Instantiate<object>(object)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform)
		// object UnityEngine.Object.Instantiate<object>(object,UnityEngine.Transform,bool)
		// object[] UnityEngine.Resources.ConvertObjects<object>(UnityEngine.Object[])
		// YooAsset.AssetHandle YooAsset.ResourcePackage.LoadAssetAsync<object>(string,uint)
		// YooAsset.AssetHandle YooAsset.ResourcePackage.LoadAssetSync<object>(string)
		// string string.Join<float>(string,System.Collections.Generic.IEnumerable<float>)
		// string string.Join<object>(string,System.Collections.Generic.IEnumerable<object>)
		// string string.JoinCore<float>(System.Char*,int,System.Collections.Generic.IEnumerable<float>)
		// string string.JoinCore<object>(System.Char*,int,System.Collections.Generic.IEnumerable<object>)
	}
}
