﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.OData.Routing.Template;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Validation;
using Microsoft.OData.UriParser;

namespace Microsoft.AspNetCore.OData.Edm
{
    internal static class EdmModelExtensions
    {
        /// <summary>
        /// Resolve the alternate key properties.
        /// </summary>
        /// <param name="model">The Edm model.</param>
        /// <param name="keySegment">The key segment.</param>
        /// <returns>The resolved Edm properties.</returns>
        public static IDictionary<string, IEdmProperty> ResolveAlternateKeyProperties(this IEdmModel model, KeySegment keySegment)
        {
            if (model == null)
            {
                throw Error.ArgumentNull(nameof(model));
            }

            if (keySegment == null)
            {
                throw Error.ArgumentNull(nameof(keySegment));
            }

            IEdmEntityType entityType = (IEdmEntityType)keySegment.EdmType;
            var alternateKeys = model.GetAlternateKeys(entityType);
            if (alternateKeys == null)
            {
                return null;
            }

            // It should be case-sensitive, then we can support "Id" & "ID", they are different, but it's valid.
            HashSet<string> keyNames = keySegment.Keys.Select(k => k.Key).ToHashSet(/*StringComparer.OrdinalIgnoreCase*/);

            // Let's find the alternate key in alternate keys
            // The count should match
            // The keys should match the alias
            int count = keySegment.Keys.Count();
            IDictionary<string, IEdmPathExpression> foundAlternateKey = alternateKeys.FirstOrDefault(a => a.Count == count && keyNames.SetEquals(a.Keys));

            if (foundAlternateKey == null)
            {
                return null;
            }

            IDictionary<string, IEdmProperty> properties = null;
            foreach (var alternateKey in foundAlternateKey)
            {
                IEdmProperty edmProperty = model.FindProperty(entityType, alternateKey.Value);
                if (edmProperty == null)
                {
                    throw new ODataException(Error.Format(SRResources.PropertyNotFoundOnPathExpression, alternateKey.Value.Path, entityType.FullName()));
                }

                if (properties == null)
                {
                    properties = new Dictionary<string, IEdmProperty>();
                }

                properties[alternateKey.Key] = edmProperty;
            }

            return properties;
        }

        /// <summary>
        /// Resolve the <see cref="IEdmProperty"/> using the property name. This method supports the property name case insensitive.
        /// However, ODL only support case-sensitive.
        /// </summary>
        /// <param name="structuredType">The given structural type </param>
        /// <param name="propertyName">The given property name.</param>
        /// <returns>The resolved <see cref="IEdmProperty"/>.</returns>
        public static IEdmProperty ResolveProperty(this IEdmStructuredType structuredType, string propertyName)
        {
            if (structuredType == null)
            {
                throw Error.ArgumentNull(nameof(structuredType));
            }

            bool ambiguous = false;
            IEdmProperty edmProperty = null;
            foreach (var property in structuredType.Properties())
            {
                string name = property.Name;
                if (name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (name.Equals(propertyName, StringComparison.Ordinal))
                    {
                        return property;
                    }
                    else if (edmProperty != null)
                    {
                        ambiguous = true;
                    }
                    else
                    {
                        edmProperty = property;
                    }
                }
            }

            if (ambiguous)
            {
                throw new ODataException(Error.Format(SRResources.AmbiguousPropertyNameFound, propertyName));
            }

            return edmProperty;
        }

        /// <summary>
        /// Resolve the <see cref="IEdmSchemaType"/> using the type name. This method supports the type name case insensitive.
        /// </summary>
        /// <param name="model">The Edm model.</param>
        /// <param name="typeName">The type name.</param>
        /// <returns>The Edm schema type.</returns>
        public static IEdmSchemaType ResolveType(this IEdmModel model, string typeName)
        {
            IEdmSchemaType type = model.FindType(typeName);
            if (type != null)
            {
                return type;
            }

            var types = model.SchemaElements.OfType<IEdmSchemaType>()
                .Where(e => string.Equals(typeName, e.FullName(), StringComparison.OrdinalIgnoreCase));

            foreach (var refModels in model.ReferencedModels)
            {
                var refedTypes = refModels.SchemaElements.OfType<IEdmSchemaType>()
                    .Where(e => string.Equals(typeName, e.FullName(), StringComparison.OrdinalIgnoreCase));

                types = types.Concat(refedTypes);
            }

            if (types.Count() > 1)
            {
                throw new ODataException(Error.Format(SRResources.AmbiguousTypeNameFound, typeName));
            }

            return types.SingleOrDefault();
        }

        /// <summary>
        /// Find the property using the given <see cref="IEdmPathExpression"/> starting from the given <see cref="IEdmStructuredType"/>.
        /// </summary>
        /// <param name="model">The Edm model.</param>
        /// <param name="structuredType">The structured type.</param>
        /// <param name="path">The property path.</param>
        /// <returns>Null or the found edm property.</returns>
        public static IEdmProperty FindProperty(this IEdmModel model, IEdmStructuredType structuredType, IEdmPathExpression path)
        {
            if (model == null)
            {
                throw Error.ArgumentNull(nameof(model));
            }

            if (structuredType == null)
            {
                throw Error.ArgumentNull(nameof(structuredType));
            }

            if (path == null)
            {
                throw Error.ArgumentNull(nameof(path));
            }

            IEdmProperty property = null;
            IEdmStructuredType startingType = structuredType;
            foreach (var segment in path.PathSegments)
            {
                if (string.IsNullOrEmpty(segment))
                {
                    // Let's simply ignore the empty segment
                    continue;
                }

                // So far, we only support "property and type cast in the path"
                if (segment.Contains('.', StringComparison.Ordinal))
                {
                    startingType = model.ResolveType(segment) as IEdmStructuredType;
                    if (startingType == null)
                    {
                        throw new ODataException(Error.Format(SRResources.ResourceTypeNotInModel, segment));
                    }
                }
                else
                {
                    if (startingType == null)
                    {
                        return null;
                    }

                    property = startingType.ResolveProperty(segment);
                    if (property == null)
                    {
                        throw new ODataException(Error.Format(SRResources.PropertyNotFoundOnPathExpression, path.Path, structuredType.FullTypeName()));
                    }

                    startingType = property.Type.GetElementTypeOrSelf().Definition as IEdmStructuredType;
                }
            }

            return property;
        }

        /// <summary>
        /// Resolve the navigation source using the input identifier
        /// </summary>
        /// <param name="model">The Edm model.</param>
        /// <param name="identifier">The identifier</param>
        /// <param name="enableCaseInsensitive">Enable case insensitive</param>
        /// <returns>Null or the found navigation source.</returns>
        public static IEdmNavigationSource ResolveNavigationSource(this IEdmModel model, string identifier, bool enableCaseInsensitive = false)
        {
            if (model == null)
            {
                throw Error.ArgumentNull(nameof(model));
            }

            IEdmNavigationSource navSource = model.FindDeclaredNavigationSource(identifier);
            if (navSource != null || !enableCaseInsensitive)
            {
                return navSource;
            }

            IEdmEntityContainer container = model.EntityContainer;
            if (container == null)
            {
                return null;
            }

            var result = container.Elements.OfType<IEdmNavigationSource>()
                .Where(source => string.Equals(identifier, source.Name, StringComparison.OrdinalIgnoreCase)).ToList();

            if (result.Count > 1)
            {
                throw new ODataException(Error.Format(SRResources.AmbiguousNavigationSourceNameFound, identifier));
            }

            return result.SingleOrDefault();
        }

        public static IEnumerable<IEdmOperationImport> ResolveOperationImports(this IEdmModel model,
            string identifier,
            bool enableCaseInsensitive = false)
        {
            IEnumerable<IEdmOperationImport> results = model.FindDeclaredOperationImports(identifier);
            if (results.Any() || !enableCaseInsensitive)
            {
                return results;
            }

            IEdmEntityContainer container = model.EntityContainer;
            if (container == null)
            {
                return null;
            }

            return container.Elements.OfType<IEdmOperationImport>()
                .Where(source => string.Equals(identifier, source.Name, StringComparison.OrdinalIgnoreCase));
        }

        internal static IEdmEntitySetBase GetTargetEntitySet(this IEdmOperation operation, IEdmNavigationSource source, IEdmModel model)
        {
            if (source == null)
            {
                return null;
            }

            if (operation.IsBound && operation.Parameters.Any())
            {
                IEdmOperationParameter parameter;
                Dictionary<IEdmNavigationProperty, IEdmPathExpression> path;
                IEdmEntityType lastEntityType;

                if (operation.TryGetRelativeEntitySetPath(model, out parameter, out path, out lastEntityType, out IEnumerable<EdmError> _))
                {
                    IEdmNavigationSource target = source;

                    foreach (var navigation in path)
                    {
                        target = target.FindNavigationTarget(navigation.Key, navigation.Value);
                    }

                    return target as IEdmEntitySetBase;
                }
            }

            return null;
        }

        public static IEdmNavigationSource FindNavigationTarget(this IEdmNavigationSource navigationSource,
            IEdmNavigationProperty navigationProperty,
            IList<ODataSegmentTemplate> parsedSegments,
            out IEdmPathExpression bindingPath)
        {
            bindingPath = null;

            if (navigationProperty.ContainsTarget)
            {
                return navigationSource;
            }

            IEnumerable<IEdmNavigationPropertyBinding> bindings =
                navigationSource.FindNavigationPropertyBindings(navigationProperty);

            if (bindings != null)
            {
                foreach (var binding in bindings)
                {
                    if (BindingPathHelper.MatchBindingPath(binding.Path, parsedSegments))
                    {
                        bindingPath = binding.Path;
                        return binding.Target;
                    }
                }
            }

            return null;
        }

        public static bool IsEntityOrEntityCollectionType(this IEdmType edmType, out IEdmEntityType entityType)
        {
            if (edmType.TypeKind == EdmTypeKind.Entity)
            {
                entityType = (IEdmEntityType)edmType;
                return true;
            }

            if (edmType.TypeKind != EdmTypeKind.Collection)
            {
                entityType = null;
                return false;
            }

            entityType = ((IEdmCollectionType)edmType).ElementType.Definition as IEdmEntityType;
            return entityType != null;
        }

        internal static bool IsResourceOrCollectionResource(this IEdmTypeReference edmType)
        {
            if (edmType.IsEntity() || edmType.IsComplex())
            {
                return true;
            }

            if (edmType.IsCollection())
            {
                return IsResourceOrCollectionResource(edmType.AsCollection().ElementType());
            }

            return false;
        }

        /// <summary>
        /// Tests type reference is enum or collection enum
        /// </summary>
        /// <param name="edmType"></param>
        /// <returns></returns>
        public static bool IsEnumOrCollectionEnum(this IEdmTypeReference edmType)
        {
            if (edmType.IsEnum())
            {
                return true;
            }

            if (edmType.IsCollection())
            {
                return IsEnumOrCollectionEnum(edmType.AsCollection().ElementType());
            }

            return false;
        }

        /// <summary>
        /// Find the given type in a structured type inheritance, include itself.
        /// </summary>
        /// <param name="structuralType">The starting structural type.</param>
        /// <param name="model">The Edm model.</param>
        /// <param name="typeName">The searching type name.</param>
        /// <returns>The found type.</returns>
        public static IEdmStructuredType FindTypeInInheritance(this IEdmStructuredType structuralType, IEdmModel model, string typeName)
        {
            IEdmStructuredType baseType = structuralType;
            while (baseType != null)
            {
                if (GetName(baseType) == typeName)
                {
                    return baseType;
                }

                baseType = baseType.BaseType;
            }

            return model.FindAllDerivedTypes(structuralType).FirstOrDefault(c => GetName(c) == typeName);
        }

        private static string GetName(IEdmStructuredType type)
        {
            IEdmEntityType entityType = type as IEdmEntityType;
            if (entityType != null)
            {
                return entityType.Name;
            }

            return ((IEdmComplexType)type).Name;
        }

        public static IEdmTypeReference GetElementTypeOrSelf(this IEdmTypeReference typeReference)
        {
            if (typeReference.TypeKind() == EdmTypeKind.Collection)
            {
                IEdmCollectionTypeReference collectType = typeReference.AsCollection();
                return collectType.ElementType();
            }

            return typeReference;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <param name="entityType"></param>
        /// <returns></returns>
        public static IEnumerable<IEdmAction> GetAvailableActions(this IEdmModel model, IEdmEntityType entityType)
        {
            return model.GetAvailableOperations(entityType, false).OfType<IEdmAction>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <param name="entityType"></param>
        /// <returns></returns>
        public static IEnumerable<IEdmFunction> GetAvailableFunctions(this IEdmModel model, IEdmEntityType entityType)
        {
            return model.GetAvailableOperations(entityType, false).OfType<IEdmFunction>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <param name="entityType"></param>
        /// <returns></returns>
        public static IEnumerable<IEdmOperation> GetAvailableOperationsBoundToCollection(this IEdmModel model, IEdmEntityType entityType)
        {
            return model.GetAvailableOperations(entityType, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <param name="entityType"></param>
        /// <param name="boundToCollection"></param>
        /// <returns></returns>
        public static IEnumerable<IEdmOperation> GetAvailableOperations(this IEdmModel model, IEdmEntityType entityType, bool boundToCollection = false)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            BindableOperationFinder annotation = model.GetAnnotationValue<BindableOperationFinder>(model);
            if (annotation == null)
            {
                annotation = new BindableOperationFinder(model);
                model.SetAnnotationValue(model, annotation);
            }

            if (boundToCollection)
            {
                return annotation.FindOperationsBoundToCollection(entityType);
            }
            else
            {
                return annotation.FindOperations(entityType);
            }
        }
    }
}
