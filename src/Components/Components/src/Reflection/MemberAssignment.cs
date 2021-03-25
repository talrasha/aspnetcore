// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using static Microsoft.AspNetCore.Internal.LinkerFlags;

namespace Microsoft.AspNetCore.Components.Reflection
{
    internal class MemberAssignment
    {
        public static PropertyEnumerable GetPropertiesIncludingInherited(
            [DynamicallyAccessedMembers(Component)] Type type,
            BindingFlags bindingFlags)
        {
            var dictionary = new Dictionary<string, OneOrMoreProperties>(StringComparer.Ordinal);

            Type? currentType = type;

            while (currentType != null)
            {
                var properties = currentType.GetProperties(bindingFlags | BindingFlags.DeclaredOnly);
                foreach (var property in properties)
                {
                    if (!dictionary.TryGetValue(property.Name, out var others))
                    {
                        others = new OneOrMoreProperties { Single = property };
                        dictionary.Add(property.Name, others);
                    }
                    else if (!IsInheritedProperty(property, others))
                    {
                        others.Add(property);
                    }
                }

                currentType = currentType.BaseType;
            }

            return new PropertyEnumerable(dictionary);
        }

        private static bool IsInheritedProperty(PropertyInfo property, OneOrMoreProperties others)
        {
            if (others.Single is not null)
            {
                return others.Single.GetMethod?.GetBaseDefinition() == property.GetMethod?.GetBaseDefinition();
            }

            Debug.Assert(others.Many is not null);
            foreach (var other in CollectionsMarshal.AsSpan(others.Many))
            {
                if (other.GetMethod?.GetBaseDefinition() == property.GetMethod?.GetBaseDefinition())
                {
                    return true;
                }
            }

            return false;
        }

        public struct OneOrMoreProperties
        {
            public PropertyInfo? Single;
            public List<PropertyInfo>? Many;

            public void Add(PropertyInfo property)
            {
                if (Many is null)
                {
                    Many ??= new() { Single! };
                    Single = null;
                }

                Many.Add(property);
            }
        }

        public ref struct PropertyEnumerable
        {
            private readonly PropertyEnumerator _enumerator;

            public PropertyEnumerable(Dictionary<string, OneOrMoreProperties> dictionary)
            {
                _enumerator = new PropertyEnumerator(dictionary);
            }

            public PropertyEnumerator GetEnumerator() => _enumerator;
        }

        public ref struct PropertyEnumerator
        {
            // Do NOT make this readonly, or MoveNext will not work
            private Dictionary<string, OneOrMoreProperties>.Enumerator _dictionaryEnumerator;
            private Span<PropertyInfo>.Enumerator _spanEnumerator;

            public PropertyEnumerator(Dictionary<string, OneOrMoreProperties> dictionary)
            {
                _dictionaryEnumerator = dictionary.GetEnumerator();
                _spanEnumerator = Span<PropertyInfo>.Empty.GetEnumerator();
            }

            public PropertyInfo Current => _spanEnumerator.Current;

            public bool MoveNext()
            {
                if (_spanEnumerator.MoveNext())
                {
                    return true;
                }

                if (!_dictionaryEnumerator.MoveNext())
                {
                    return false;
                }

                var oneOrMoreProperties = _dictionaryEnumerator.Current.Value;
                var span = oneOrMoreProperties.Single is { } property ?
                    MemoryMarshal.CreateSpan(ref property, 1) :
                    CollectionsMarshal.AsSpan(oneOrMoreProperties.Many);

                _spanEnumerator = span.GetEnumerator();
                var moveNext = _spanEnumerator.MoveNext();
                Debug.Assert(moveNext, "We expect this to at least have one item.");
                return moveNext;
            }
        }
    }
}
