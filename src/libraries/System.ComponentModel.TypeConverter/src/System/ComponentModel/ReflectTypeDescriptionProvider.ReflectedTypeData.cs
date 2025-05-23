// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace System.ComponentModel
{
    internal sealed partial class ReflectTypeDescriptionProvider : TypeDescriptionProvider
    {
        /// <summary>
        /// This class contains all the reflection information for a
        /// given type.
        /// </summary>
        private sealed class ReflectedTypeData
        {
            private readonly Type _type;
            private AttributeCollection? _attributes;
            private EventDescriptorCollection? _events;
            private PropertyDescriptorCollection? _properties;
            private TypeConverter? _converter;
            private object[]? _editors;
            private Type[]? _editorTypes;
            private int _editorCount;
            private bool _isRegistered;

            internal ReflectedTypeData(Type type, bool isRegisteredType)
            {
                _type = type;
                _isRegistered = isRegisteredType;
            }

            /// <summary>
            /// This method returns true if the data cache in this reflection
            /// type descriptor has data in it.
            /// </summary>
            internal bool IsPopulated => (_attributes != null) | (_events != null) | (_properties != null);

            internal bool IsRegistered => _isRegistered;

            /// <summary>
            /// Retrieves custom attributes.
            /// </summary>
            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2062:UnrecognizedReflectionPattern",
                Justification = "_type is annotated as preserve All members, so any Types returned from GetInterfaces should be preserved as well.")]
            internal AttributeCollection GetAttributes()
            {
                // Worst case collision scenario:  we don't want the perf hit
                // of taking a lock, so if we collide we will query for
                // attributes twice. Not a big deal.
                if (_attributes == null)
                {
                    // Obtaining attributes follows a very critical order: we must take care that
                    // we merge attributes the right way. Consider this:
                    //
                    // [A4]
                    // interface IBase;
                    //
                    // [A3]
                    // interface IDerived;
                    //
                    // [A2]
                    // class Base : IBase;
                    //
                    // [A1]
                    // class Derived : Base, IDerived
                    //
                    // Calling GetAttributes on type Derived must merge attributes in the following
                    // order:  A1 - A4. Interfaces always lose to types, and interfaces and types
                    // must be merged in the same order. At the same time, we must be careful
                    // that we don't always go through reflection here, because someone could have
                    // created a custom provider for a type. Because there is only one instance
                    // of ReflectTypeDescriptionProvider created for typeof(object), if our code
                    // is invoked here we can be sure that there is no custom provider for
                    // _type all the way up the base class chain.
                    // We cannot be sure that there is no custom provider for
                    // interfaces that _type implements, however, because they are not derived
                    // from _type. So, for interfaces, we must go through TypeDescriptor
                    // again to get the interfaces attributes.

                    // Get the type's attributes. This does not recurse up the base class chain.
                    // We append base class attributes to this array so when walking we will
                    // walk from Length - 1 to zero.
                    //

                    var attributes = new List<Attribute>(ReflectGetAttributes(_type));
                    Type? baseType = _type.BaseType;

                    while (baseType != null && baseType != typeof(object))
                    {
                        attributes.AddRange(ReflectGetAttributes(baseType));
                        baseType = baseType.BaseType;
                    }

                    // Next, walk the type's interfaces. We append these to
                    // the attribute array as well.
                    int ifaceStartIdx = attributes.Count;
                    Type[] interfaces = TrimSafeReflectionHelper.GetInterfaces(_type);
                    for (int idx = 0; idx < interfaces.Length; idx++)
                    {
                        // Only do this for public interfaces.
                        Type iface = interfaces[idx];
                        if ((iface.Attributes & (TypeAttributes.Public | TypeAttributes.NestedPublic)) != 0)
                        {
                            // No need to pass an instance into GetTypeDescriptor here because, if someone provided a custom
                            // provider based on object, it already would have hit.
                            attributes.AddRange(TypeDescriptor.GetAttributes(iface).Attributes);
                        }
                    }

                    // Finally, filter out duplicates.
                    if (attributes.Count != 0)
                    {
                        var filter = new HashSet<object>(attributes.Count);
                        int next = 0;

                        for (int idx = 0; idx < attributes.Count; idx++)
                        {
                            Attribute attr = attributes[idx];

                            bool addAttr = true;
                            if (idx >= ifaceStartIdx)
                            {
                                for (int ifaceSkipIdx = 0; ifaceSkipIdx < s_skipInterfaceAttributeList.Length; ifaceSkipIdx++)
                                {
                                    if (s_skipInterfaceAttributeList[ifaceSkipIdx].IsInstanceOfType(attr))
                                    {
                                        addAttr = false;
                                        break;
                                    }
                                }
                            }

                            if (addAttr && filter.Add(attr.TypeId))
                            {
                                attributes[next++] = attributes[idx];
                            }
                        }

                        attributes.RemoveRange(next, attributes.Count - next);
                    }

                    _attributes = new AttributeCollection(attributes.ToArray());
                }

                return _attributes;
            }

            /// <summary>
            /// Retrieves the class name for our type.
            /// </summary>
            internal string? GetClassName() => _type.FullName;

            /// <summary>
            /// Retrieves the component name from the site.
            /// </summary>
            internal static string? GetComponentName(object? instance)
            {
                IComponent? comp = instance as IComponent;
                ISite? site = comp?.Site;
                if (site != null)
                {
                    INestedSite? nestedSite = site as INestedSite;
                    return (nestedSite?.FullName) ?? site.Name;
                }

                return null;
            }

            /// <summary>
            /// Retrieves the type converter. If instance is non-null,
            /// it will be used to retrieve attributes. Otherwise, _type
            /// will be used.
            /// </summary>
            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
                Justification = "_type is annotated as preserve All members, so any Types returned from GetAttributes.")]
            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072:UnrecognizedReflectionPattern",
                Justification = "_type is annotated as preserve All members, so any Types returned from CreateInstance.")]
            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2077:UnrecognizedReflectionPattern",
                Justification = "_type is annotated as preserve All members, so any Types returned from GetAttributes.")]
            internal TypeConverter GetConverter(object? instance, bool verifyIsRegisteredType)
            {
                TypeConverterAttribute? typeAttr = null;

                // For instances, the design time object for them may want to redefine the
                // attributes. So, we search the attribute here based on the instance. If found,
                // we then search on the same attribute based on type. If the two don't match, then
                // we cannot cache the value and must re-create every time. It is rare for a designer
                // to override these attributes, so we want to be smart here.
                if (instance != null)
                {
                    typeAttr = (TypeConverterAttribute?)TypeDescriptor.GetAttributes(_type)[typeof(TypeConverterAttribute)];
                    TypeConverterAttribute instanceAttr = (TypeConverterAttribute)TypeDescriptor.GetAttributes(instance)[typeof(TypeConverterAttribute)]!;
                    if (typeAttr != instanceAttr)
                    {
                        Type? converterType = GetTypeFromName(instanceAttr.ConverterTypeName);
                        if (converterType != null && typeof(TypeConverter).IsAssignableFrom(converterType))
                        {
                            if (verifyIsRegisteredType && !_isRegistered && !IsIntrinsicType(_type))
                            {
                                TypeDescriptor.ThrowHelper.ThrowInvalidOperationException_RegisterTypeRequired(_type);
                            }

                            return (TypeConverter)ReflectTypeDescriptionProvider.CreateInstance(converterType, _type)!;
                        }
                    }
                }

                // If we got here, we return our type-based converter.
                if (_converter == null)
                {
                    typeAttr ??= (TypeConverterAttribute?)TypeDescriptor.GetAttributes(_type)[typeof(TypeConverterAttribute)];

                    if (typeAttr != null)
                    {
                        Type? converterType = GetTypeFromName(typeAttr.ConverterTypeName);
                        if (converterType != null && typeof(TypeConverter).IsAssignableFrom(converterType))
                        {
                            _converter = (TypeConverter)CreateInstance(converterType, _type)!;
                        }
                    }

                    if (_converter == null)
                    {
                        // We did not get a converter. Traverse up the base class chain until
                        // we find one in the stock hashtable.
                        _converter = GetIntrinsicTypeConverter(_type);

                        Debug.Assert(_converter != null, "There is no intrinsic setup in the hashtable for the Object type");
                    }
                }

                if (verifyIsRegisteredType && !_isRegistered && !IsIntrinsicType(_type))
                {
                    TypeDescriptor.ThrowHelper.ThrowInvalidOperationException_RegisterTypeRequired(_type);
                }

                return _converter;
            }

            /// <summary>
            /// Return the default event. The default event is determined by the
            /// presence of a DefaultEventAttribute on the class.
            /// </summary>
            [RequiresUnreferencedCode("The Type of instance cannot be statically discovered.")]
            internal EventDescriptor? GetDefaultEvent(object? instance)
            {
                AttributeCollection attributes;

                if (instance != null)
                {
                    attributes = TypeDescriptor.GetAttributes(instance);
                }
                else
                {
                    attributes = TypeDescriptor.GetAttributes(_type);
                }

                DefaultEventAttribute? attr = (DefaultEventAttribute?)attributes[typeof(DefaultEventAttribute)];
                if (attr != null && attr.Name != null)
                {
                    if (instance != null)
                    {
                        return TypeDescriptor.GetEvents(instance)[attr.Name];
                    }
                    else
                    {
                        return TypeDescriptor.GetEvents(_type)[attr.Name];
                    }
                }

                return null;
            }

            /// <summary>
            /// Return the default property.
            /// </summary>
            [RequiresUnreferencedCode(PropertyDescriptor.PropertyDescriptorPropertyTypeMessage + " The Type of instance cannot be statically discovered.")]
            internal PropertyDescriptor? GetDefaultProperty(object? instance)
            {
                AttributeCollection attributes;

                if (instance != null)
                {
                    attributes = TypeDescriptor.GetAttributes(instance);
                }
                else
                {
                    attributes = TypeDescriptor.GetAttributes(_type);
                }

                DefaultPropertyAttribute? attr = (DefaultPropertyAttribute?)attributes[typeof(DefaultPropertyAttribute)];
                if (attr != null && attr.Name != null)
                {
                    if (instance != null)
                    {
                        return TypeDescriptor.GetProperties(instance)[attr.Name];
                    }
                    else
                    {
                        return TypeDescriptor.GetProperties(_type)[attr.Name];
                    }
                }

                return null;
            }

            /// <summary>
            /// Retrieves the editor for the given base type.
            /// </summary>
            [RequiresUnreferencedCode(TypeDescriptor.DesignTimeAttributeTrimmed + " The Type of instance cannot be statically discovered.")]
            internal object? GetEditor(object? instance, Type editorBaseType)
            {
                EditorAttribute? typeAttr;

                // For instances, the design time object for them may want to redefine the
                // attributes. So, we search the attribute here based on the instance. If found,
                // we then search on the same attribute based on type. If the two don't match, then
                // we cannot cache the value and must re-create every time. It is rare for a designer
                // to override these attributes, so we want to be smart here.
                if (instance != null)
                {
                    typeAttr = GetEditorAttribute(TypeDescriptor.GetAttributes(_type), editorBaseType);
                    EditorAttribute? instanceAttr = GetEditorAttribute(TypeDescriptor.GetAttributes(instance), editorBaseType);
                    if (typeAttr != instanceAttr)
                    {
                        Type? editorType = GetTypeFromName(instanceAttr!.EditorTypeName);
                        if (editorType != null && editorBaseType.IsAssignableFrom(editorType))
                        {
                            return CreateInstance(editorType, _type);
                        }
                    }
                }

                // If we got here, we return our type-based editor.
                lock (this)
                {
                    for (int idx = 0; idx < _editorCount; idx++)
                    {
                        if (_editorTypes![idx] == editorBaseType)
                        {
                            return _editors![idx];
                        }
                    }
                }

                // Editor is not cached yet. Look in the attributes.
                object? editor = null;

                typeAttr = GetEditorAttribute(TypeDescriptor.GetAttributes(_type), editorBaseType);
                if (typeAttr != null)
                {
                    Type? editorType = GetTypeFromName(typeAttr.EditorTypeName);
                    if (editorType != null && editorBaseType.IsAssignableFrom(editorType))
                    {
                        editor = CreateInstance(editorType, _type);
                    }
                }

                // Editor is not in the attributes. Search intrinsic tables.
                if (editor == null)
                {
                    Hashtable? intrinsicEditors = GetEditorTable(editorBaseType);
                    if (intrinsicEditors != null)
                    {
                        editor = GetIntrinsicTypeEditor(intrinsicEditors, _type);
                    }

                    // As a quick sanity check, check to see that the editor we got back is of
                    // the correct type.
                    if (editor != null && !editorBaseType.IsInstanceOfType(editor))
                    {
                        Debug.Fail($"Editor {editor.GetType().FullName} is not an instance of {editorBaseType.FullName} but it is in that base types table.");
                        editor = null;
                    }
                }

                if (editor != null)
                {
                    lock (this)
                    {
                        if (_editorTypes == null || _editorTypes.Length == _editorCount)
                        {
                            int newLength = (_editorTypes == null ? 4 : _editorTypes.Length * 2);

                            Type[] newTypes = new Type[newLength];
                            object[] newEditors = new object[newLength];

                            if (_editorTypes != null)
                            {
                                _editorTypes.CopyTo(newTypes, 0);
                                _editors!.CopyTo(newEditors, 0);
                            }

                            _editorTypes = newTypes;
                            _editors = newEditors;

                            _editorTypes[_editorCount] = editorBaseType;
                            _editors[_editorCount++] = editor;
                        }
                    }
                }

                return editor;
            }

            /// <summary>
            /// Helper method to return an editor attribute of the correct base type.
            /// </summary>
            [RequiresUnreferencedCode("The type referenced by the Editor attribute may be trimmed away.")]
            private static EditorAttribute? GetEditorAttribute(AttributeCollection attributes, Type editorBaseType)
            {
                foreach (Attribute attr in attributes)
                {
                    if (attr is EditorAttribute edAttr)
                    {
                        Type? attrEditorBaseType = Type.GetType(edAttr.EditorBaseTypeName!);

                        if (attrEditorBaseType != null && attrEditorBaseType == editorBaseType)
                        {
                            return edAttr;
                        }
                    }
                }

                return null;
            }

            /// <summary>
            /// Retrieves the events for this type.
            /// </summary>
            internal EventDescriptorCollection GetEvents()
            {
                // Worst case collision scenario:  we don't want the perf hit
                // of taking a lock, so if we collide we will query for
                // events twice. Not a big deal.
                //
                if (_events == null)
                {
                    EventDescriptor[] eventArray;
                    Dictionary<string, EventDescriptor> eventList = new Dictionary<string, EventDescriptor>(16);
                    Type? baseType = _type;
                    Type objType = typeof(object);

                    do
                    {
                        eventArray = ReflectGetEvents(baseType);
                        foreach (EventDescriptor ed in eventArray)
                        {
                            eventList.TryAdd(ed.Name, ed);
                        }
                        baseType = baseType.BaseType;
                    }
                    while (baseType != null && baseType != objType);

                    eventArray = new EventDescriptor[eventList.Count];
                    eventList.Values.CopyTo(eventArray, 0);
                    _events = new EventDescriptorCollection(eventArray, true);
                }

                return _events;
            }

            /// <summary>
            /// Retrieves the properties for this type.
            /// </summary>
            [RequiresUnreferencedCode(PropertyDescriptor.PropertyDescriptorPropertyTypeMessage)]
            internal PropertyDescriptorCollection GetProperties()
            {
                // Worst case collision scenario:  we don't want the perf hit
                // of taking a lock, so if we collide we will query for
                // properties twice. Not a big deal.
                if (_properties == null)
                {
                    PropertyDescriptor[] propertyArray;
                    Dictionary<string, PropertyDescriptor> propertyList = new Dictionary<string, PropertyDescriptor>(10);
                    Type? baseType = _type;
                    Type objType = typeof(object);

                    do
                    {
                        propertyArray = ReflectGetProperties(baseType);
                        foreach (PropertyDescriptor p in propertyArray)
                        {
                            propertyList.TryAdd(p.Name, p);
                        }
                        baseType = baseType.BaseType;
                    }
                    while (baseType != null && baseType != objType);

                    propertyArray = new PropertyDescriptor[propertyList.Count];
                    propertyList.Values.CopyTo(propertyArray, 0);
                    _properties = new PropertyDescriptorCollection(propertyArray, true);
                }

                return _properties;
            }

            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
                Justification = "Before this method was called, the type was validated to be registered.")]
            internal PropertyDescriptorCollection GetPropertiesFromRegisteredType() => GetProperties();

            /// <summary>
            /// Retrieves a type from a name. The Assembly of the type
            /// that this PropertyDescriptor came from is first checked,
            /// then a global Type.GetType is performed.
            /// </summary>
            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
                Justification = "Trimming requires fully-qualified type names for strings annotated with DynamicallyAccessedMembers, so the call to _type.Assembly.GetType should be unreachable in an app without trim warnings.")]
            [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2057:TypeGetType",
                Justification = "Using the non-assembly qualified type name will still work.")]
            private Type? GetTypeFromName(
                // this method doesn't create the type, but all callers are annotated with PublicConstructors,
                // so use that value to ensure the Type will be preserved
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] string typeName)
            {
                if (string.IsNullOrEmpty(typeName))
                {
                    return null;
                }

                int commaIndex = typeName.IndexOf(',');
                Type? t = null;

                if (commaIndex == -1)
                {
                    t = _type.Assembly.GetType(typeName);
                }

                t ??= Type.GetType(typeName);

                if (t == null && commaIndex != -1)
                {
                    // At design time, it's possible for us to reuse
                    // an assembly but add new types. The app domain
                    // will cache the assembly based on identity, however,
                    // so it could be looking in the previous version
                    // of the assembly and not finding the type. We work
                    // around this by looking for the non-assembly qualified
                    // name, which causes the domain to raise a type
                    // resolve event.
                    t = Type.GetType(typeName.Substring(0, commaIndex));
                }

                return t;
            }

            /// <summary>
            /// Refreshes the contents of this type descriptor. This does not
            /// actually requery, but it will clear our state so the next
            /// query re-populates.
            /// </summary>
            internal void Refresh()
            {
                _attributes = null;
                _events = null;
                _properties = null;
                _converter = null;
                _editors = null;
                _editorTypes = null;
                _editorCount = 0;
            }
        }
    }
}
