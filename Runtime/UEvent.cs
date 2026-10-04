using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Core.Runtime
{
    /// <summary>
    /// Copy-on-write delegate list: safe to modify during Invoke.
    /// </summary>
    internal sealed class DelegateList<T> where T : Delegate
    {
        private T[] items = Array.Empty<T>();

        public T[] Snapshot => items;

        public void Add(T item)
        {
            if (item == null)
                return;

            T[] next = new T[items.Length + 1];
            Array.Copy(items, next, items.Length);
            next[items.Length] = item;
            items = next;
        }

        public bool Remove(T item)
        {
            int index = Array.LastIndexOf(items, item);

            if (index < 0)
                return false;

            if (items.Length == 1)
            {
                items = Array.Empty<T>();
                return true;
            }

            T[] next = new T[items.Length - 1];
            Array.Copy(items, 0, next, 0, index);
            Array.Copy(
                items,
                index + 1,
                next,
                index,
                items.Length - index - 1);

            items = next;
            return true;
        }

        public void Clear()
        {
            items = Array.Empty<T>();
        }
    }

    // ------------------------------------------------------------
    // Events
    // ------------------------------------------------------------

    [Serializable]
    public abstract class UEventBase : ISerializationCallbackReceiver
    {
        public List<Listener> listeners = new();

        // True when serialized listeners may have changed and
        // their delegates must be rebuilt.
        [NonSerialized]
        protected bool persistentDirty = true;

        public void OnBeforeSerialize()
        {
        }

        // May run off the main thread.
        // Never touch UnityEngine.Object references here.
        public void OnAfterDeserialize()
        {
            persistentDirty = true;
        }
    }

    /// <summary>
    /// Inspector listeners are compiled into delegates lazily.
    /// Runtime listeners live separately, so persistent rebuilds
    /// never affect them.
    ///
    /// Invoke operates only on a cached delegate array.
    /// No reflection, DynamicInvoke, MethodInfo.Invoke, or
    /// object[] allocation occurs in the invocation path.
    /// </summary>
    [Serializable]
    public abstract class UEventCore<TAction> : UEventBase
        where TAction : Delegate
    {
        [NonSerialized]
        private TAction[] persistent = Array.Empty<TAction>();

        [NonSerialized]
        private TAction[] combined;

        [NonSerialized]
        private DelegateList<TAction> runtime;

        protected abstract TAction Wrap(Listener listener);

        protected TAction[] Snapshot
        {
            get
            {
                if (persistentDirty)
                    RebuildPersistent();

                return combined ??= Combine();
            }
        }

        public void AddListener(TAction action)
        {
            if (action == null)
                return;

            (runtime ??= new DelegateList<TAction>()).Add(action);
            combined = null;
        }

        public void RemoveListener(TAction action)
        {
            if (runtime != null && runtime.Remove(action))
                combined = null;
        }

        /// <summary>
        /// Removes code listeners only. Inspector listeners stay.
        /// </summary>
        public void RemoveAllListeners()
        {
            runtime?.Clear();
            combined = null;
        }

        private void RebuildPersistent()
        {
            persistentDirty = false;

            int count = listeners?.Count ?? 0;

            if (count == 0)
            {
                persistent = Array.Empty<TAction>();
                combined = null;
                return;
            }

            List<TAction> built = new(count);

            for (int i = 0; i < count; i++)
            {
                Listener listener = listeners[i];

                if (listener == null)
                    continue;

                TAction action = Wrap(listener);

                if (action != null)
                    built.Add(action);
            }

            persistent = built.Count == 0
                ? Array.Empty<TAction>()
                : built.ToArray();

            combined = null;
        }

        private TAction[] Combine()
        {
            TAction[] code =
                runtime?.Snapshot ?? Array.Empty<TAction>();

            if (persistent.Length == 0)
                return code;

            if (code.Length == 0)
                return persistent;

            TAction[] all =
                new TAction[persistent.Length + code.Length];

            Array.Copy(
                persistent,
                0,
                all,
                0,
                persistent.Length);

            Array.Copy(
                code,
                0,
                all,
                persistent.Length,
                code.Length);

            return all;
        }
    }

    [Serializable]
    public class UEvent : UEventCore<Action>
    {
        protected override Action Wrap(Listener listener)
        {
            if (listener.mode == ListenerMode.Static)
                return listener.BindStatic();

            Action fast = listener.TryBind<Action>();

            if (fast == null)
                return null;

            return () =>
            {
                if (listener.IsAlive)
                    fast();
            };
        }

        public void Invoke()
        {
            Action[] snapshot = Snapshot;

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i]();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    [Serializable]
    public class UEvent<T0> : UEventCore<Action<T0>>
    {
        protected override Action<T0> Wrap(Listener listener)
        {
            if (listener.mode == ListenerMode.Static)
            {
                Action staticCall = listener.BindStatic();

                if (staticCall == null)
                    return null;

                return _ => staticCall();
            }

            Action<T0> fast = listener.TryBind<Action<T0>>();

            if (fast == null)
                return null;

            return a0 =>
            {
                if (listener.IsAlive)
                    fast(a0);
            };
        }

        public void Invoke(T0 a0)
        {
            Action<T0>[] snapshot = Snapshot;

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i](a0);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    [Serializable]
    public class UEvent<T0, T1> : UEventCore<Action<T0, T1>>
    {
        protected override Action<T0, T1> Wrap(Listener listener)
        {
            if (listener.mode == ListenerMode.Static)
            {
                Action staticCall = listener.BindStatic();

                if (staticCall == null)
                    return null;

                return (_, __) => staticCall();
            }

            Action<T0, T1> fast =
                listener.TryBind<Action<T0, T1>>();

            if (fast == null)
                return null;

            return (a0, a1) =>
            {
                if (listener.IsAlive)
                    fast(a0, a1);
            };
        }

        public void Invoke(T0 a0, T1 a1)
        {
            Action<T0, T1>[] snapshot = Snapshot;

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i](a0, a1);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    [Serializable]
    public class UEvent<T0, T1, T2> :
        UEventCore<Action<T0, T1, T2>>
    {
        protected override Action<T0, T1, T2> Wrap(Listener listener)
        {
            if (listener.mode == ListenerMode.Static)
            {
                Action staticCall = listener.BindStatic();

                if (staticCall == null)
                    return null;

                return (_, __, ___) => staticCall();
            }

            Action<T0, T1, T2> fast =
                listener.TryBind<Action<T0, T1, T2>>();

            if (fast == null)
                return null;

            return (a0, a1, a2) =>
            {
                if (listener.IsAlive)
                    fast(a0, a1, a2);
            };
        }

        public void Invoke(T0 a0, T1 a1, T2 a2)
        {
            Action<T0, T1, T2>[] snapshot = Snapshot;

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i](a0, a1, a2);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    [Serializable]
    public class UEvent<T0, T1, T2, T3> :
        UEventCore<Action<T0, T1, T2, T3>>
    {
        protected override Action<T0, T1, T2, T3> Wrap(Listener listener)
        {
            if (listener.mode == ListenerMode.Static)
            {
                Action staticCall = listener.BindStatic();

                if (staticCall == null)
                    return null;

                return (_, __, ___, ____) => staticCall();
            }

            Action<T0, T1, T2, T3> fast =
                listener.TryBind<Action<T0, T1, T2, T3>>();

            if (fast == null)
                return null;

            return (a0, a1, a2, a3) =>
            {
                if (listener.IsAlive)
                    fast(a0, a1, a2, a3);
            };
        }

        public void Invoke(
            T0 a0,
            T1 a1,
            T2 a2,
            T3 a3)
        {
            Action<T0, T1, T2, T3>[] snapshot = Snapshot;

            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i](a0, a1, a2, a3);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }

    // ------------------------------------------------------------
    // Listener
    // ------------------------------------------------------------

    public enum ListenerMode
    {
        // Parameters are set in the inspector.
        Static = 0,

        // Parameters come from the event Invoke call.
        Dynamic = 1
    }

    [Serializable]
    public class Listener
    {
        public UnityEngine.Object target;
        public Component component;
        public string eventName;
        public string methodSignature;
        public ListenerMode mode;
        public Parameter[] parameters;

        [NonSerialized]
        private MethodInfo method;

        [NonSerialized]
        private Type resolvedComponentType;

        [NonSerialized]
        private string resolvedName;

        [NonSerialized]
        private string resolvedSignature;

        [NonSerialized]
        private int parameterCount;

        [NonSerialized]
        private Delegate dynamicDelegate;

        [NonSerialized]
        private Action staticDelegate;

        public bool IsAlive => component != null;

        /// <summary>
        /// Binds a dynamic listener to the exact typed delegate required
        /// by the event.
        ///
        /// Reflection is performed only when the listener is rebuilt.
        /// The resulting delegate is reused for every invocation.
        /// </summary>
        public TDelegate TryBind<TDelegate>()
            where TDelegate : Delegate
        {
            if (mode != ListenerMode.Dynamic ||
                component == null ||
                !TryResolve())
            {
                return null;
            }

            if (dynamicDelegate is TDelegate cached)
                return cached;

            TDelegate bound = Delegate.CreateDelegate(
                typeof(TDelegate),
                component,
                method,
                false) as TDelegate;

            if (bound == null)
                return null;

            dynamicDelegate = bound;
            return bound;
        }

        /// <summary>
        /// Compiles a static inspector listener into a normal Action.
        ///
        /// All reflection and parameter conversion happens here,
        /// never during event invocation.
        /// </summary>
        public Action BindStatic()
        {
            if (mode != ListenerMode.Static ||
                component == null ||
                !TryResolve())
            {
                return null;
            }

            if (staticDelegate != null)
                return staticDelegate;

            object[] values = GetStaticValues();

            if (values == null)
                return null;

            staticDelegate = StaticInvokerFactory.Create(
                component,
                method,
                values);

            return staticDelegate;
        }

        private bool TryResolve()
        {
            if (component == null)
                return false;

            Type componentType = component.GetType();

            if (resolvedComponentType == componentType &&
                resolvedName == eventName &&
                resolvedSignature == methodSignature)
            {
                return method != null;
            }

            resolvedComponentType = componentType;
            resolvedName = eventName;
            resolvedSignature = methodSignature;

            method = null;
            dynamicDelegate = null;
            staticDelegate = null;
            parameterCount = 0;

            if (string.IsNullOrEmpty(eventName))
                return false;

            string[] names =
                string.IsNullOrEmpty(methodSignature)
                    ? Array.Empty<string>()
                    : methodSignature.Split('|');

            Type[] types = new Type[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                types[i] = Type.GetType(names[i]);

                if (types[i] == null)
                {
                    Debug.LogWarning(
                        $"Listener could not resolve type '{names[i]}' " +
                        $"for '{eventName}'.",
                        component);

                    return false;
                }
            }

            method = componentType.GetMethod(
                eventName,
                BindingFlags.Instance |
                BindingFlags.Public,
                null,
                types,
                null);

            if (method == null)
            {
                Debug.LogWarning(
                    $"Listener could not find '{eventName}' on " +
                    $"{componentType.Name}.",
                    component);

                return false;
            }

            if (method.ReturnType != typeof(void))
            {
                Debug.LogWarning(
                    $"Listener method '{eventName}' on " +
                    $"{componentType.Name} must return void.",
                    component);

                method = null;
                return false;
            }

            parameterCount = types.Length;
            return true;
        }

        private object[] GetStaticValues()
        {
            int count = parameters?.Length ?? 0;

            if (count != parameterCount)
            {
                Debug.LogWarning(
                    $"Listener '{eventName}' has {count} parameter(s) " +
                    $"but the method expects {parameterCount}. " +
                    "Re-select the function in the inspector.",
                    component);

                return null;
            }

            object[] values = new object[count];

            for (int i = 0; i < count; i++)
            {
                Parameter parameter = parameters[i];

                if (parameter == null)
                {
                    Debug.LogWarning(
                        $"Listener '{eventName}' contains a null " +
                        $"parameter at index {i}.",
                        component);

                    return null;
                }

                values[i] = parameter.GetValue();
            }

            return values;
        }
    }

    // ------------------------------------------------------------
    // Static listener binding
    // ------------------------------------------------------------

    internal interface IStaticInvoker
    {
        void Invoke();
    }

    internal sealed class StaticInvoker1<T0> : IStaticInvoker
    {
        private readonly Action<T0> action;
        private readonly T0 value0;

        public StaticInvoker1(
            Action<T0> action,
            object value0)
        {
            this.action = action;
            this.value0 = (T0)value0;
        }

        public void Invoke()
        {
            action(value0);
        }
    }

    internal sealed class StaticInvoker2<T0, T1> : IStaticInvoker
    {
        private readonly Action<T0, T1> action;
        private readonly T0 value0;
        private readonly T1 value1;

        public StaticInvoker2(
            Action<T0, T1> action,
            object value0,
            object value1)
        {
            this.action = action;
            this.value0 = (T0)value0;
            this.value1 = (T1)value1;
        }

        public void Invoke()
        {
            action(value0, value1);
        }
    }

    internal sealed class StaticInvoker3<T0, T1, T2> : IStaticInvoker
    {
        private readonly Action<T0, T1, T2> action;
        private readonly T0 value0;
        private readonly T1 value1;
        private readonly T2 value2;

        public StaticInvoker3(
            Action<T0, T1, T2> action,
            object value0,
            object value1,
            object value2)
        {
            this.action = action;
            this.value0 = (T0)value0;
            this.value1 = (T1)value1;
            this.value2 = (T2)value2;
        }

        public void Invoke()
        {
            action(value0, value1, value2);
        }
    }

    internal sealed class StaticInvoker4<T0, T1, T2, T3> : IStaticInvoker
    {
        private readonly Action<T0, T1, T2, T3> action;
        private readonly T0 value0;
        private readonly T1 value1;
        private readonly T2 value2;
        private readonly T3 value3;

        public StaticInvoker4(
            Action<T0, T1, T2, T3> action,
            object value0,
            object value1,
            object value2,
            object value3)
        {
            this.action = action;
            this.value0 = (T0)value0;
            this.value1 = (T1)value1;
            this.value2 = (T2)value2;
            this.value3 = (T3)value3;
        }

        public void Invoke()
        {
            action(value0, value1, value2, value3);
        }
    }

    internal static class StaticInvokerFactory
    {
        public static Action Create(
            Component component,
            MethodInfo method,
            object[] values)
        {
            ParameterInfo[] parameters = method.GetParameters();
            Type[] types = new Type[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
                types[i] = parameters[i].ParameterType;

            switch (types.Length)
            {
                case 0:
                {
                    Action action = Delegate.CreateDelegate(
                        typeof(Action),
                        component,
                        method) as Action;

                    return action;
                }

                case 1:
                {
                    Type invokerType =
                        typeof(StaticInvoker1<>)
                            .MakeGenericType(types);

                    Type delegateType =
                        typeof(Action<>)
                            .MakeGenericType(types);

                    Delegate bound = Delegate.CreateDelegate(
                        delegateType,
                        component,
                        method);

                    IStaticInvoker invoker =
                        (IStaticInvoker)Activator.CreateInstance(
                            invokerType,
                            bound,
                            values[0]);

                    return invoker.Invoke;
                }

                case 2:
                {
                    Type invokerType =
                        typeof(StaticInvoker2<,>)
                            .MakeGenericType(types);

                    Type delegateType =
                        typeof(Action<,>)
                            .MakeGenericType(types);

                    Delegate bound = Delegate.CreateDelegate(
                        delegateType,
                        component,
                        method);

                    IStaticInvoker invoker =
                        (IStaticInvoker)Activator.CreateInstance(
                            invokerType,
                            bound,
                            values[0],
                            values[1]);

                    return invoker.Invoke;
                }

                case 3:
                {
                    Type invokerType =
                        typeof(StaticInvoker3<,,>)
                            .MakeGenericType(types);

                    Type delegateType =
                        typeof(Action<,,>)
                            .MakeGenericType(types);

                    Delegate bound = Delegate.CreateDelegate(
                        delegateType,
                        component,
                        method);

                    IStaticInvoker invoker =
                        (IStaticInvoker)Activator.CreateInstance(
                            invokerType,
                            bound,
                            values[0],
                            values[1],
                            values[2]);

                    return invoker.Invoke;
                }

                case 4:
                {
                    Type invokerType =
                        typeof(StaticInvoker4<,,,>)
                            .MakeGenericType(types);

                    Type delegateType =
                        typeof(Action<,,,>)
                            .MakeGenericType(types);

                    Delegate bound = Delegate.CreateDelegate(
                        delegateType,
                        component,
                        method);

                    IStaticInvoker invoker =
                        (IStaticInvoker)Activator.CreateInstance(
                            invokerType,
                            bound,
                            values[0],
                            values[1],
                            values[2],
                            values[3]);

                    return invoker.Invoke;
                }

                default:
                    Debug.LogWarning(
                        $"Static listener '{method.Name}' has " +
                        $"{types.Length} parameters. " +
                        "Maximum supported is 4.",
                        component);

                    return null;
            }
        }
    }

    // ------------------------------------------------------------
    // Parameters
    // ------------------------------------------------------------

    [Serializable]
    public class Parameter
    {
        public string name;
        public string type;
        public string value;
        public UnityEngine.Object objectValue;

        [SerializeReference]
        public object managedValue;

        public object GetValue()
        {
            Type t =
                string.IsNullOrEmpty(type)
                    ? null
                    : Type.GetType(type);

            if (t == null)
                return null;

            if (typeof(UnityEngine.Object).IsAssignableFrom(t))
                return objectValue;

            if (ParameterValueUtility.IsSupported(t))
                return ParameterValueUtility.Parse(t, value);

            return managedValue;
        }
    }

    /// <summary>
    /// Shared by the drawer (writes) and Parameter (reads).
    /// Invariant culture ensures values survive locale changes.
    /// </summary>
    public static class ParameterValueUtility
    {
        public static bool IsSupported(Type type)
        {
            return type == typeof(int)
                || type == typeof(long)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(bool)
                || type == typeof(string)
                || type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Color)
                || type.IsEnum;
        }

        public static string Serialize(Type type, object value)
        {
            CultureInfo c = CultureInfo.InvariantCulture;

            if (type == typeof(int))
                return ((int)value).ToString(c);

            if (type == typeof(long))
                return ((long)value).ToString(c);

            if (type == typeof(float))
                return ((float)value).ToString("R", c);

            if (type == typeof(double))
                return ((double)value).ToString("R", c);

            if (type == typeof(bool))
                return (bool)value ? "1" : "0";

            if (type == typeof(string))
                return (string)value ?? string.Empty;

            if (type == typeof(Vector2))
            {
                Vector2 v = (Vector2)value;
                return string.Join(",", F(v.x), F(v.y));
            }

            if (type == typeof(Vector3))
            {
                Vector3 v = (Vector3)value;
                return string.Join(
                    ",",
                    F(v.x),
                    F(v.y),
                    F(v.z));
            }

            if (type == typeof(Vector4))
            {
                Vector4 v = (Vector4)value;
                return string.Join(
                    ",",
                    F(v.x),
                    F(v.y),
                    F(v.z),
                    F(v.w));
            }

            if (type == typeof(Color))
            {
                Color col = (Color)value;
                return string.Join(
                    ",",
                    F(col.r),
                    F(col.g),
                    F(col.b),
                    F(col.a));
            }

            if (type.IsEnum)
                return Convert.ToInt64(value).ToString(c);

            return string.Empty;

            static string F(float f) =>
                f.ToString(
                    "R",
                    CultureInfo.InvariantCulture);
        }

        public static object Parse(Type type, string s)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            s ??= string.Empty;

            if (type == typeof(int))
                return int.TryParse(
                    s,
                    NumberStyles.Integer,
                    c,
                    out int i)
                    ? i
                    : 0;

            if (type == typeof(long))
                return long.TryParse(
                    s,
                    NumberStyles.Integer,
                    c,
                    out long l)
                    ? l
                    : 0L;

            if (type == typeof(float))
                return float.TryParse(
                    s,
                    NumberStyles.Float,
                    c,
                    out float f)
                    ? f
                    : 0f;

            if (type == typeof(double))
                return double.TryParse(
                    s,
                    NumberStyles.Float,
                    c,
                    out double d)
                    ? d
                    : 0d;

            if (type == typeof(bool))
            {
                return s == "1" ||
                       s.Equals(
                           "true",
                           StringComparison.OrdinalIgnoreCase);
            }

            if (type == typeof(string))
                return s;

            if (type == typeof(Vector2))
            {
                float[] v = ParseFloats(s, 2, 0f);
                return new Vector2(v[0], v[1]);
            }

            if (type == typeof(Vector3))
            {
                float[] v = ParseFloats(s, 3, 0f);
                return new Vector3(v[0], v[1], v[2]);
            }

            if (type == typeof(Vector4))
            {
                float[] v = ParseFloats(s, 4, 0f);
                return new Vector4(
                    v[0],
                    v[1],
                    v[2],
                    v[3]);
            }

            if (type == typeof(Color))
            {
                float[] v = ParseFloats(s, 4, 1f);
                return new Color(
                    v[0],
                    v[1],
                    v[2],
                    v[3]);
            }

            if (type.IsEnum)
            {
                long.TryParse(
                    s,
                    NumberStyles.Integer,
                    c,
                    out long e);

                return Enum.ToObject(type, e);
            }

            return null;
        }

        private static float[] ParseFloats(
            string s,
            int count,
            float fallback)
        {
            float[] result = new float[count];

            for (int i = 0; i < count; i++)
                result[i] = fallback;

            if (string.IsNullOrEmpty(s))
                return result;

            string[] parts = s.Split(',');

            for (int i = 0;
                 i < count && i < parts.Length;
                 i++)
            {
                if (float.TryParse(
                    parts[i],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float f))
                {
                    result[i] = f;
                }
            }

            return result;
        }
    }
}
