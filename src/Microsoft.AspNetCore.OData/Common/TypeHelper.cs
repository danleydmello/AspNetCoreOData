﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OData.Query.Wrapper;
using Microsoft.OData.ModelBuilder;

namespace Microsoft.AspNetCore.OData.Common
{
    /// <summary>
    /// The type related helper methods.
    /// </summary>
    internal static class TypeHelper
    {
        /// <summary>
        /// Test whether the input type is <see cref="DynamicTypeWrapper"/>
        /// </summary>
        /// <param name="type">The test type.</param>
        /// <returns>true/false</returns>
        public static bool IsDynamicTypeWrapper(this Type type)
        {
            return (type != null && typeof(DynamicTypeWrapper).IsAssignableFrom(type));
        }

        /// <summary>
        /// Return the collection element type.
        /// </summary>
        /// <param name="clrType">The type to convert.</param>
        /// <returns>The collection element type from a type.</returns>
        public static Type GetInnerElementType(Type clrType)
        {
            IsCollection(clrType, out Type elementType);
            Contract.Assert(elementType != null);

            return elementType;
        }

        /// <summary>
        /// Return the underlying type or itself.
        /// </summary>
        /// <param name="type">The input type.</param>
        /// <returns>The underlying type.</returns>
        public static Type GetUnderlyingTypeOrSelf(Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }

        /// <summary>
        /// Determine if a type is a DateTime.
        /// </summary>
        /// <param name="clrType">The type to test.</param>
        /// <returns>True if the type is a DateTime; false otherwise.</returns>
        public static bool IsDateTime(Type clrType)
        {
            Type underlyingTypeOrSelf = GetUnderlyingTypeOrSelf(clrType);
            return Type.GetTypeCode(underlyingTypeOrSelf) == TypeCode.DateTime;
        }

        /// <summary>
        /// Determine if a type is a TimeSpan.
        /// </summary>
        /// <param name="clrType">The type to test.</param>
        /// <returns>True if the type is a TimeSpan; false otherwise.</returns>
        public static bool IsTimeSpan(Type clrType)
        {
            Type underlyingTypeOrSelf = GetUnderlyingTypeOrSelf(clrType);
            return underlyingTypeOrSelf == typeof(TimeSpan);
        }

        /// <summary>
        /// Determine if a type is assignable from another type.
        /// </summary>
        /// <param name="clrType">The type to test.</param>
        /// <param name="fromType">The type to assign from.</param>
        /// <returns>True if the type is assignable; false otherwise.</returns>
        public static bool IsTypeAssignableFrom(Type clrType, Type fromType)
        {
            return clrType.IsAssignableFrom(fromType);
        }

        /// <summary>
        /// Return the reflected type from a member info.
        /// </summary>
        /// <param name="memberInfo">The member info to convert.</param>
        /// <returns>The reflected type from a member info.</returns>
        public static Type GetReflectedType(MemberInfo memberInfo)
        {
            return memberInfo.ReflectedType;
        }

        /// <summary>
        /// Determine if a type is an enumeration.
        /// </summary>
        /// <param name="clrType">The type to test.</param>
        /// <returns>True if the type is an enumeration; false otherwise.</returns>
        public static bool IsEnum(Type clrType)
        {
            Type underlyingTypeOrSelf = GetUnderlyingTypeOrSelf(clrType);
            return underlyingTypeOrSelf.IsEnum;
        }

        /// <summary>
        /// Determine if a type is null-able.
        /// </summary>
        /// <param name="clrType">The type to test.</param>
        /// <returns>True if the type is null-able; false otherwise.</returns>
        public static bool IsNullable(this Type clrType)
        {
            if (clrType == null)
            {
                return false;
            }

            if (clrType.IsValueType)
            {
                // value types are only nullable if they are Nullable<T>
                return clrType.IsGenericType && clrType.GetGenericTypeDefinition() == typeof(Nullable<>);
            }
            else
            {
                // reference types are always nullable
                return true;
            }
        }

        /// <summary>
        /// Return the type from a nullable type.
        /// </summary>
        /// <param name="clrType">The type to convert.</param>
        /// <returns>The type from a nullable type.</returns>
        public static Type ToNullable(Type clrType)
        {
            if (IsNullable(clrType))
            {
                return clrType;
            }
            else
            {
                return typeof(Nullable<>).MakeGenericType(clrType);
            }
        }

        /// <summary>
        /// Determine if a type is a collection.
        /// </summary>
        /// <param name="clrType">The type to test.</param>
        /// <returns>True if the type is an enumeration; false otherwise.</returns>
        public static bool IsCollection(Type clrType) => IsCollection(clrType, out _);

        /// <summary>
        /// Determine if a type is a collection.
        /// </summary>
        /// <param name="clrType">The type to test.</param>
        /// <param name="elementType">out: the element type of the collection.</param>
        /// <returns>True if the type is an enumeration; false otherwise.</returns>
        public static bool IsCollection(Type clrType, out Type elementType)
        {
            if (clrType == null)
            {
                throw Error.ArgumentNull(nameof(clrType));
            }

            elementType = clrType;

            // see if this type should be ignored.
            if (clrType == typeof(string))
            {
                return false;
            }

            Type collectionInterface
                = clrType.GetInterfaces()
                    .Union(new[] { clrType })
                    .FirstOrDefault(
                        t => t.IsGenericType
                             && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (collectionInterface != null)
            {
                elementType = collectionInterface.GetGenericArguments().Single();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns type of T if the type implements IEnumerable of T, otherwise, return null.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal static Type GetImplementedIEnumerableType(Type type)
        {
            // get inner type from Task<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                type = type.GetGenericArguments().First();
            }

            if (type.IsGenericType && type.IsInterface &&
                (type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQueryable<>)))
            {
                // special case the IEnumerable<T>
                return GetInnerGenericType(type);
            }
            else
            {
                // for the rest of interfaces and strongly Type collections
                Type[] interfaces = type.GetInterfaces();
                foreach (Type interfaceType in interfaces)
                {
                    if (interfaceType.IsGenericType &&
                        (interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                         interfaceType.GetGenericTypeDefinition() == typeof(IQueryable<>)))
                    {
                        // special case the IEnumerable<T>
                        return GetInnerGenericType(interfaceType);
                    }
                }
            }

            return null;
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Catching all exceptions in this case is the right to do.")]
        // This code is copied from DefaultHttpControllerTypeResolver.GetControllerTypes.
        internal static IEnumerable<Type> GetLoadedTypes(IAssemblyResolver assembliesResolver)
        {
            List<Type> result = new List<Type>();

            if (assembliesResolver == null)
            {
                return result;
            }

            // Go through all assemblies referenced by the application and search for types matching a predicate
            IEnumerable<Assembly> assemblies = assembliesResolver.Assemblies;
            foreach (Assembly assembly in assemblies)
            {
                Type[] exportedTypes = null;
                if (assembly == null || assembly.IsDynamic)
                {
                    // can't call GetTypes on a null (or dynamic?) assembly
                    continue;
                }

                try
                {
                    exportedTypes = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    exportedTypes = ex.Types;
                }
                catch
                {
                    continue;
                }

                if (exportedTypes != null)
                {
                    result.AddRange(exportedTypes.Where(t => t != null && t.IsVisible));
                }
            }

            return result;
        }

        internal static Type GetTaskInnerTypeOrSelf(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return type.GetGenericArguments().First();
            }

            return type;
        }

        private static Type GetInnerGenericType(Type interfaceType)
        {
            // Getting the type T definition if the returning type implements IEnumerable<T>
            Type[] parameterTypes = interfaceType.GetGenericArguments();

            if (parameterTypes.Length == 1)
            {
                return parameterTypes[0];
            }

            return null;
        }
    }
}
