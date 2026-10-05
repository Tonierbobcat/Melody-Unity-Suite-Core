using System;
using System.Collections.Generic;
using System.Linq;
using MelodySuite.Core.Runtime;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MelodySuite.Core.Editor
{
    /// <summary>
    /// Draws UEvent / UEvent&lt;T...&gt; as a UnityEvent-style list.
    /// It reads the generic arguments from the field type so each listener
    /// can offer Dynamic methods that match them.
    /// </summary>
    [CustomPropertyDrawer(typeof(UEventBase), true)]
    public class UEventDrawer : PropertyDrawer
    {
        private const float ElementPadding = 4f;

        private readonly Dictionary<string, ReorderableList> lists = new();
        private Type[] argTypes;

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            return GetList(property, label).GetHeight();
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            ReorderableList list = GetList(property, label);

            EditorGUI.BeginProperty(position, label, property);
            list.DoList(EditorGUI.IndentedRect(position));
            EditorGUI.EndProperty();
        }

        private ReorderableList GetList(
            SerializedProperty property,
            GUIContent label)
        {
            SerializedProperty listeners =
                property.FindPropertyRelative("listeners");

            string key = property.propertyPath;

            if (lists.TryGetValue(key, out ReorderableList existing))
            {
                // Rebind in case the SerializedObject was recreated.
                existing.serializedProperty = listeners;
                return existing;
            }

            argTypes ??= ResolveArgTypes();

            string header =
                argTypes.Length == 0
                    ? $"{label.text} ()"
                    : $"{label.text} " +
                      $"({string.Join(", ", argTypes.Select(ListenerDrawer.GetTypeLabel))})";

            ReorderableList list = new(
                listeners.serializedObject,
                listeners,
                true,
                true,
                true,
                true);

            list.drawHeaderCallback = rect =>
                EditorGUI.LabelField(rect, header);

            list.elementHeightCallback = index =>
            {
                SerializedProperty array = list.serializedProperty;

                if (index < 0 || index >= array.arraySize)
                    return EditorGUIUtility.singleLineHeight;

                return ListenerDrawer.GetListenerHeight(
                           array.GetArrayElementAtIndex(index))
                       + ElementPadding;
            };

            list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                SerializedProperty array = list.serializedProperty;

                if (index < 0 || index >= array.arraySize)
                    return;

                rect.y += ElementPadding * 0.5f;
                rect.height -= ElementPadding;

                ListenerDrawer.DrawListener(
                    rect,
                    array.GetArrayElementAtIndex(index),
                    argTypes);
            };

            list.onAddCallback = l =>
            {
                SerializedProperty array = l.serializedProperty;

                int index = array.arraySize;
                array.arraySize++;
                l.index = index;

                // Adding duplicates the last element, so start clean.
                ListenerDrawer.ResetListener(
                    array.GetArrayElementAtIndex(index));

                array.serializedObject.ApplyModifiedProperties();
            };

            lists[key] = list;
            return list;
        }

        /// <summary>
        /// Finds UEvent&lt;T0, T1, ...&gt; from the field type, including
        /// fields that are arrays / lists of events or subclasses of them.
        /// </summary>
        private Type[] ResolveArgTypes()
        {
            Type type = fieldInfo?.FieldType;

            if (type == null)
                return Type.EmptyTypes;

            if (type.IsArray)
            {
                type = type.GetElementType();
            }
            else if (type.IsGenericType &&
                     type.GetGenericTypeDefinition() == typeof(List<>))
            {
                type = type.GetGenericArguments()[0];
            }

            while (type != null && type != typeof(object))
            {
                if (type.IsGenericType)
                    return type.GetGenericArguments();

                type = type.BaseType;
            }

            return Type.EmptyTypes;
        }
    }
}