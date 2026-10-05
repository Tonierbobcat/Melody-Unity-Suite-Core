using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MelodySuite.Core.Runtime;
using UnityEditor;
using UnityEngine;

namespace MelodySuite.Core.Editor
{
    /// <summary>
    /// Draws a single Listener. When used through UEventDrawer it knows the
    /// event's generic argument types and offers Dynamic and Static entries.
    /// When a Listener field is drawn on its own, only Static entries are offered.
    /// </summary>
    [CustomPropertyDrawer(typeof(Listener))]
    public class ListenerDrawer : PropertyDrawer
    {
        private const float LineHeight = 18f;
        private const float Spacing = 2f;
        private const float ParamIndent = 15f;

        private static readonly Dictionary<Type, string> TypeAliases = new()
        {
            { typeof(int), "int" },
            { typeof(long), "long" },
            { typeof(float), "float" },
            { typeof(double), "double" },
            { typeof(bool), "bool" },
            { typeof(string), "string" },
            { typeof(object), "object" },
        };

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            DrawListener(position, property, Type.EmptyTypes);
        }

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            return GetListenerHeight(property);
        }
        
        public static void DrawListener(
            Rect position,
            SerializedProperty property,
            Type[] eventArgTypes)
        {
            EditorGUI.BeginProperty(position, GUIContent.none, property);

            SerializedProperty target =
                property.FindPropertyRelative("target");
            SerializedProperty component =
                property.FindPropertyRelative("component");
            SerializedProperty eventName =
                property.FindPropertyRelative("eventName");
            SerializedProperty methodSignature =
                property.FindPropertyRelative("methodSignature");
            SerializedProperty mode =
                property.FindPropertyRelative("mode");
            SerializedProperty parameters =
                property.FindPropertyRelative("parameters");

            bool isDynamic = mode.intValue == (int)ListenerMode.Dynamic;

            float y = position.y;
            
            const float gap = 4f;
            float targetWidth = position.width * 0.4f;

            Rect targetRect =
                new Rect(position.x, y, targetWidth, LineHeight);

            Rect functionRect =
                new Rect(
                    position.x + targetWidth + gap,
                    y,
                    position.width - targetWidth - gap,
                    LineHeight);

            EditorGUI.PropertyField(targetRect, target, GUIContent.none);

            string functionLabel =
                string.IsNullOrEmpty(eventName.stringValue)
                    ? "No Function"
                    : GetFunctionLabel(
                        eventName.stringValue,
                        methodSignature.stringValue,
                        isDynamic);

            if (EditorGUI.DropdownButton(
                    functionRect,
                    new GUIContent(functionLabel),
                    FocusType.Passive))
            {
                ShowFunctionMenu(
                    functionRect,
                    target.objectReferenceValue,
                    eventArgTypes,
                    component,
                    eventName,
                    methodSignature,
                    mode,
                    parameters);
            }

            y += LineHeight + Spacing;

            // Parameters (static only - dynamic values come from Invoke)
            if (!isDynamic && parameters != null && parameters.isArray)
            {
                for (int i = 0; i < parameters.arraySize; i++)
                {
                    SerializedProperty element =
                        parameters.GetArrayElementAtIndex(i);

                    float height = GetParameterHeight(element);

                    Rect rect = new Rect(
                        position.x + ParamIndent,
                        y,
                        position.width - ParamIndent,
                        height);

                    DrawParameter(rect, element);
                    y += height + Spacing;
                }
            }

            EditorGUI.EndProperty();
        }

        public static float GetListenerHeight(SerializedProperty property)
        {
            float height = LineHeight + Spacing;   // was (LineHeight + Spacing) * 3f

            SerializedProperty mode =
                property.FindPropertyRelative("mode");

            if (mode.intValue == (int)ListenerMode.Dynamic)
                return height;

            SerializedProperty parameters =
                property.FindPropertyRelative("parameters");

            if (parameters != null && parameters.isArray)
            {
                for (int i = 0; i < parameters.arraySize; i++)
                {
                    height += GetParameterHeight(
                        parameters.GetArrayElementAtIndex(i)) + Spacing;
                }
            }

            return height;
        }

        public static void ResetListener(SerializedProperty listener)
        {
            listener.FindPropertyRelative("target").objectReferenceValue = null;
            listener.FindPropertyRelative("component").objectReferenceValue = null;
            listener.FindPropertyRelative("eventName").stringValue = string.Empty;
            listener.FindPropertyRelative("methodSignature").stringValue = string.Empty;
            listener.FindPropertyRelative("mode").intValue = (int)ListenerMode.Static;
            listener.FindPropertyRelative("parameters").arraySize = 0;
        }

        public static string GetTypeLabel(Type type)
        {
            return TypeAliases.TryGetValue(type, out string alias)
                ? alias
                : type.Name;
        }

        // ------------------------------------------------------------
        // Parameter drawing
        // ------------------------------------------------------------

        private static void DrawParameter(
            Rect rect,
            SerializedProperty parameter)
        {
            SerializedProperty nameProp =
                parameter.FindPropertyRelative("name");
            SerializedProperty valueProp =
                parameter.FindPropertyRelative("value");
            SerializedProperty objectProp =
                parameter.FindPropertyRelative("objectValue");
            SerializedProperty managedProp =
                parameter.FindPropertyRelative("managedValue");

            string label =
                ObjectNames.NicifyVariableName(nameProp.stringValue);

            Type type = GetParameterType(parameter);

            if (type == null)
            {
                EditorGUI.LabelField(rect, label, "Missing type");
                return;
            }

            // Unity object references
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                objectProp.objectReferenceValue =
                    EditorGUI.ObjectField(
                        rect,
                        label,
                        objectProp.objectReferenceValue,
                        type,
                        true);
                return;
            }

            // Primitives / vectors / colors / enums
            if (ParameterValueUtility.IsSupported(type))
            {
                object current =
                    ParameterValueUtility.Parse(type, valueProp.stringValue);

                EditorGUI.BeginChangeCheck();
                object result = DrawValueField(rect, label, type, current);

                if (EditorGUI.EndChangeCheck())
                {
                    valueProp.stringValue =
                        ParameterValueUtility.Serialize(type, result);
                }

                return;
            }

            // [Serializable] classes (e.g. ItemInstance)
            if (IsManagedClass(type))
            {
                if (managedProp.managedReferenceValue == null)
                {
                    managedProp.managedReferenceValue =
                        CreateManagedInstance(type);
                }

                EditorGUI.PropertyField(
                    rect,
                    managedProp,
                    new GUIContent(label),
                    true);
                return;
            }

            EditorGUI.LabelField(rect, label, $"Unsupported ({type.Name})");
        }

        private static Type GetParameterType(SerializedProperty parameter)
        {
            string typeName =
                parameter.FindPropertyRelative("type").stringValue;

            return string.IsNullOrEmpty(typeName)
                ? null
                : Type.GetType(typeName);
        }

        private static float GetParameterHeight(SerializedProperty parameter)
        {
            Type type = GetParameterType(parameter);

            if (type != null &&
                !typeof(UnityEngine.Object).IsAssignableFrom(type) &&
                !ParameterValueUtility.IsSupported(type) &&
                IsManagedClass(type))
            {
                return EditorGUI.GetPropertyHeight(
                    parameter.FindPropertyRelative("managedValue"),
                    true);
            }

            return LineHeight;
        }

        private static bool IsManagedClass(Type type)
        {
            return type.IsClass
                && !type.IsAbstract
                && !type.IsArray
                && !type.IsGenericTypeDefinition
                && type.IsSerializable
                && !typeof(IList).IsAssignableFrom(type)
                && !typeof(Delegate).IsAssignableFrom(type);
        }

        private static object CreateManagedInstance(Type type)
        {
            // Classes without a parameterless constructor (like ItemInstance)
            // get an uninitialized instance. Unity fills the fields afterwards.
            return type.GetConstructor(Type.EmptyTypes) != null
                ? Activator.CreateInstance(type)
                : RuntimeHelpers.GetUninitializedObject(type);
        }

        private static object DrawValueField(
            Rect rect,
            string label,
            Type type,
            object value)
        {
            if (type == typeof(int))
                return EditorGUI.IntField(rect, label, (int)value);
            if (type == typeof(long))
                return EditorGUI.LongField(rect, label, (long)value);
            if (type == typeof(float))
                return EditorGUI.FloatField(rect, label, (float)value);
            if (type == typeof(double))
                return EditorGUI.DoubleField(rect, label, (double)value);
            if (type == typeof(bool))
                return EditorGUI.Toggle(rect, label, (bool)value);
            if (type == typeof(string))
                return EditorGUI.TextField(rect, label, (string)value);
            if (type == typeof(Vector2))
                return EditorGUI.Vector2Field(rect, label, (Vector2)value);
            if (type == typeof(Vector3))
                return EditorGUI.Vector3Field(rect, label, (Vector3)value);
            if (type == typeof(Vector4))
                return EditorGUI.Vector4Field(rect, label, (Vector4)value);
            if (type == typeof(Color))
                return EditorGUI.ColorField(rect, label, (Color)value);

            if (type.IsEnum)
            {
                Enum enumValue = (Enum)value;

                return type.IsDefined(typeof(FlagsAttribute), false)
                    ? EditorGUI.EnumFlagsField(rect, label, enumValue)
                    : EditorGUI.EnumPopup(rect, label, enumValue);
            }

            return value;
        }

        // ------------------------------------------------------------
        // Function menu
        // ------------------------------------------------------------

        private static void ShowFunctionMenu(
            Rect buttonRect,
            UnityEngine.Object target,
            Type[] eventArgTypes,
            SerializedProperty componentProperty,
            SerializedProperty eventName,
            SerializedProperty methodSignature,
            SerializedProperty modeProperty,
            SerializedProperty parametersProperty)
        {
            GenericMenu menu = new();

            if (target == null)
            {
                menu.AddDisabledItem(
                    new GUIContent("Select a Target first"));
                menu.DropDown(buttonRect);
                return;
            }

            GameObject gameObject = target as GameObject;

            if (gameObject == null &&
                target is Component targetComponent)
            {
                gameObject = targetComponent.gameObject;
            }

            if (gameObject == null)
            {
                menu.AddDisabledItem(
                    new GUIContent(
                        "Target must be a GameObject or Component"));
                menu.DropDown(buttonRect);
                return;
            }

            // Copy so the property stays valid inside the menu callbacks.
            SerializedProperty parametersCopy = parametersProperty.Copy();

            // No Function
            menu.AddItem(
                new GUIContent("No Function"),
                string.IsNullOrEmpty(eventName.stringValue),
                () =>
                {
                    eventName.stringValue = string.Empty;
                    methodSignature.stringValue = string.Empty;
                    modeProperty.intValue = (int)ListenerMode.Static;
                    parametersCopy.arraySize = 0;

                    componentProperty.serializedObject
                        .ApplyModifiedProperties();
                });

            menu.AddSeparator(string.Empty);

            void AddEntry(
                string path,
                Component entryComponent,
                MethodInfo method,
                ListenerMode entryMode)
            {
                string signature = GetMethodSignature(method);

                bool isSelected =
                    componentProperty.objectReferenceValue ==
                        entryComponent &&
                    eventName.stringValue == method.Name &&
                    methodSignature.stringValue == signature &&
                    modeProperty.intValue == (int)entryMode;

                menu.AddItem(
                    new GUIContent(path),
                    isSelected,
                    () =>
                    {
                        bool changed =
                            componentProperty.objectReferenceValue !=
                                entryComponent ||
                            eventName.stringValue != method.Name ||
                            methodSignature.stringValue != signature ||
                            modeProperty.intValue != (int)entryMode;

                        componentProperty.objectReferenceValue =
                            entryComponent;
                        eventName.stringValue = method.Name;
                        methodSignature.stringValue = signature;
                        modeProperty.intValue = (int)entryMode;

                        // Keep existing values if re-selecting the same entry.
                        if (changed)
                        {
                            if (entryMode == ListenerMode.Dynamic)
                                parametersCopy.arraySize = 0;
                            else
                                RebuildParameters(parametersCopy, method);
                        }

                        componentProperty.serializedObject
                            .ApplyModifiedProperties();
                    });
            }

            bool hasDynamicArgs = eventArgTypes.Length > 0;
            string dynamicHeader =
                "Dynamic " +
                string.Join(", ", eventArgTypes.Select(GetTypeLabel));

            bool foundMethod = false;

            foreach (Component component in
                     gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                Type componentType = component.GetType();
                string root = componentType.Name;

                MethodInfo[] methods = GetValidMethods(componentType);

                if (methods.Length == 0)
                    continue;

                foundMethod = true;

                // Dynamic section: signature matches the event's arguments.
                if (hasDynamicArgs)
                {
                    MethodInfo[] dynamicMethods =
                        methods
                            .Where(m => MatchesEventArgs(m, eventArgTypes))
                            .ToArray();

                    if (dynamicMethods.Length > 0)
                    {
                        menu.AddDisabledItem(
                            new GUIContent($"{root}/{dynamicHeader}"));

                        foreach (MethodInfo method in dynamicMethods)
                        {
                            AddEntry(
                                $"{root}/{GetMethodDisplayName(method, false)}",
                                component,
                                method,
                                ListenerMode.Dynamic);
                        }

                        menu.AddSeparator($"{root}/");
                        menu.AddDisabledItem(
                            new GUIContent($"{root}/Static Parameters"));
                    }
                }

                // Static section: any valid method, parameters set in inspector.
                foreach (MethodInfo method in methods)
                {
                    AddEntry(
                        $"{root}/{GetMethodDisplayName(method, true)}",
                        component,
                        method,
                        ListenerMode.Static);
                }
            }

            if (!foundMethod)
            {
                menu.AddDisabledItem(
                    new GUIContent("No valid functions"));
            }

            menu.DropDown(buttonRect);
        }

        private static void RebuildParameters(
            SerializedProperty parametersProperty,
            MethodInfo method)
        {
            ParameterInfo[] infos = method.GetParameters();

            // Reset first so new elements don't inherit a shared reference
            // from the previous last element.
            parametersProperty.arraySize = 0;
            parametersProperty.arraySize = infos.Length;

            for (int i = 0; i < infos.Length; i++)
            {
                SerializedProperty element =
                    parametersProperty.GetArrayElementAtIndex(i);

                Type paramType = infos[i].ParameterType;

                element.FindPropertyRelative("name").stringValue =
                    infos[i].Name;
                element.FindPropertyRelative("type").stringValue =
                    paramType.AssemblyQualifiedName;
                element.FindPropertyRelative("value").stringValue =
                    string.Empty;
                element.FindPropertyRelative("objectValue")
                    .objectReferenceValue = null;

                bool isManaged =
                    !typeof(UnityEngine.Object).IsAssignableFrom(paramType) &&
                    !ParameterValueUtility.IsSupported(paramType) &&
                    IsManagedClass(paramType);

                element.FindPropertyRelative("managedValue")
                    .managedReferenceValue =
                        isManaged ? CreateManagedInstance(paramType) : null;
            }
        }

        // ------------------------------------------------------------
        // Method helpers
        // ------------------------------------------------------------

        private static MethodInfo[] GetValidMethods(Type componentType)
        {
            return componentType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => IsValidMethod(method, componentType))
                .OrderBy(method => method.Name)
                .ThenBy(GetMethodSignature)
                .ToArray();
        }

        private static readonly HashSet<Type> ExcludedDeclaringTypes = new()
        {
            typeof(MonoBehaviour),
            typeof(Behaviour),
            typeof(Component),
            typeof(UnityEngine.Object),
            typeof(object),
        };
        
        private static bool IsValidMethod(
            MethodInfo method,
            Type componentType)
        {
            if (method.IsSpecialName)
                return false;

            if (method.IsGenericMethod)
                return false;

            if (method.ReturnType != typeof(void))
                return false;

            // ref / out parameters can't be supplied by an event.
            if (method.GetParameters().Any(p => p.ParameterType.IsByRef))
                return false;

            if (method.DeclaringType != null &&
                ExcludedDeclaringTypes.Contains(method.DeclaringType))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// A method is a valid dynamic target when it takes exactly the event's
        /// arguments (each event argument must be assignable to the parameter).
        /// </summary>
        private static bool MatchesEventArgs(
            MethodInfo method,
            Type[] eventArgTypes)
        {
            ParameterInfo[] parameters = method.GetParameters();

            if (parameters.Length != eventArgTypes.Length)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (!parameters[i].ParameterType
                        .IsAssignableFrom(eventArgTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetMethodSignature(MethodInfo method)
        {
            return string.Join(
                "|",
                method.GetParameters().Select(p =>
                    p.ParameterType.AssemblyQualifiedName));
        }

        private static string GetMethodDisplayName(
            MethodInfo method,
            bool includeParameterNames)
        {
            string parametersText =
                string.Join(
                    ", ",
                    method.GetParameters().Select(p =>
                        includeParameterNames
                            ? $"{GetTypeLabel(p.ParameterType)} {p.Name}"
                            : GetTypeLabel(p.ParameterType)));

            return $"{method.Name}({parametersText})";
        }

        private static string GetFunctionLabel(
            string eventName,
            string methodSignature,
            bool isDynamic)
        {
            string text =
                $"{eventName}({GetSignatureDisplayName(methodSignature)})";

            return isDynamic ? $"Dynamic: {text}" : text;
        }

        private static string GetSignatureDisplayName(string signature)
        {
            if (string.IsNullOrEmpty(signature))
                return string.Empty;

            return string.Join(
                ", ",
                signature.Split('|').Select(GetShortTypeName));
        }

        private static string GetShortTypeName(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
                return "Unknown";

            Type type = Type.GetType(assemblyQualifiedName);

            if (type != null)
                return GetTypeLabel(type);

            int commaIndex = assemblyQualifiedName.IndexOf(',');

            string fullName =
                commaIndex >= 0
                    ? assemblyQualifiedName.Substring(0, commaIndex)
                    : assemblyQualifiedName;

            int lastDot = fullName.LastIndexOf('.');

            return lastDot >= 0
                ? fullName.Substring(lastDot + 1)
                : fullName;
        }
    }
}