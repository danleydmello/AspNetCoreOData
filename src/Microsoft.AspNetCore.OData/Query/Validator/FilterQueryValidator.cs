﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.OData.Edm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder.Config;
using Microsoft.OData.UriParser;

namespace Microsoft.AspNetCore.OData.Query.Validator
{
    /// <summary>
    /// Represents a validator used to validate a <see cref="FilterQueryOption" /> based on the <see cref="ODataValidationSettings"/>.
    /// </summary>
    /// <remarks>
    /// Please note this class is not thread safe.
    /// </remarks>
    public class FilterQueryValidator
    {
        private int _currentAnyAllExpressionDepth;
        private int _currentNodeCount;
        private DefaultQuerySettings _defaultQuerySettings;
        private IEdmProperty _property;
        private IEdmStructuredType _structuredType;

        private IEdmModel _model;

        /// <summary>
        /// Validates a <see cref="FilterQueryOption" />.
        /// </summary>
        /// <param name="filterQueryOption">The $filter query.</param>
        /// <param name="settings">The validation settings.</param>
        /// <remarks>
        /// Please note this method is not thread safe.
        /// </remarks>
        public virtual void Validate(FilterQueryOption filterQueryOption, ODataValidationSettings settings)
        {
            if (filterQueryOption == null)
            {
                throw Error.ArgumentNull(nameof(filterQueryOption));
            }

            if (settings == null)
            {
                throw Error.ArgumentNull(nameof(settings));
            }

            if (filterQueryOption.Context.Path != null)
            {
                _property = filterQueryOption.Context.TargetProperty;
                _structuredType = filterQueryOption.Context.TargetStructuredType;
            }

            _defaultQuerySettings = filterQueryOption.Context.DefaultQuerySettings;

            Validate(filterQueryOption.FilterClause, settings, filterQueryOption.Context.Model);
        }

        internal virtual void Validate(IEdmProperty property, IEdmStructuredType structuredType,
            FilterClause filterClause, ODataValidationSettings settings, IEdmModel model, DefaultQuerySettings querySettings)
        {
            _property = property;
            _structuredType = structuredType;
            _defaultQuerySettings = querySettings;
            Validate(filterClause, settings, model);
        }

        /// <summary>
        /// Validates a <see cref="FilterClause" />.
        /// </summary>
        /// <param name="filterClause">The <see cref="FilterClause" />.</param>
        /// <param name="settings">The validation settings.</param>
        /// <param name="model">The EdmModel.</param>
        /// <remarks>
        /// Please note this method is not thread safe.
        /// </remarks>
        public virtual void Validate(FilterClause filterClause, ODataValidationSettings settings, IEdmModel model)
        {
            if (filterClause == null)
            {
                throw Error.ArgumentNull(nameof(filterClause));
            }

            _currentAnyAllExpressionDepth = 0;
            _currentNodeCount = 0;
            _model = model;

            ValidateQueryNode(filterClause.Expression, settings);
        }

        /// <summary>
        /// Override this method to restrict the 'all' query inside the filter query.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="allNode">The all node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateAllNode(AllNode allNode, ODataValidationSettings settings)
        {
            Contract.Assert(allNode != null);

            ValidateFunction("all", settings);
            EnterLambda(settings);

            try
            {
                ValidateQueryNode(allNode.Source, settings);

                ValidateQueryNode(allNode.Body, settings);
            }
            finally
            {
                ExitLambda();
            }
        }

        /// <summary>
        /// Override this method to restrict the 'any' query inside the filter query.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="anyNode">The any node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateAnyNode(AnyNode anyNode, ODataValidationSettings settings)
        {
            Contract.Assert(anyNode != null);

            ValidateFunction("any", settings);
            EnterLambda(settings);

            try
            {
                ValidateQueryNode(anyNode.Source, settings);

                if (anyNode.Body != null && anyNode.Body.Kind != QueryNodeKind.Constant)
                {
                    ValidateQueryNode(anyNode.Body, settings);
                }
            }
            finally
            {
                ExitLambda();
            }
        }

        /// <summary>
        /// override this method to restrict the binary operators inside the filter query. That includes all the logical operators except 'not' and all math operators.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="binaryOperatorNode">The binary operator node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateBinaryOperatorNode(BinaryOperatorNode binaryOperatorNode, ODataValidationSettings settings)
        {
            Contract.Assert(binaryOperatorNode != null);

            // base case goes
            switch (binaryOperatorNode.OperatorKind)
            {
                case BinaryOperatorKind.Equal:
                case BinaryOperatorKind.NotEqual:
                case BinaryOperatorKind.And:
                case BinaryOperatorKind.GreaterThan:
                case BinaryOperatorKind.GreaterThanOrEqual:
                case BinaryOperatorKind.LessThan:
                case BinaryOperatorKind.LessThanOrEqual:
                case BinaryOperatorKind.Or:
                case BinaryOperatorKind.Has:
                    // binary logical operators
                    ValidateLogicalOperator(binaryOperatorNode, settings);
                    break;
                default:
                    // math operators
                    ValidateArithmeticOperator(binaryOperatorNode, settings);
                    break;
            }
        }

        /// <summary>
        /// Override this method to validate the LogicalOperators such as 'eq', 'ne', 'gt', 'ge', 'lt', 'le', 'and', 'or'.
        /// 
        /// Please note that 'not' is not included here. Please override ValidateUnaryOperatorNode to customize 'not'.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="binaryNode">The binary operator node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateLogicalOperator(BinaryOperatorNode binaryNode, ODataValidationSettings settings)
        {
            Contract.Assert(binaryNode != null);
            Contract.Assert(settings != null);

            AllowedLogicalOperators logicalOperator = ToLogicalOperator(binaryNode);

            if ((settings.AllowedLogicalOperators & logicalOperator) != logicalOperator)
            {
                // this means the given logical operator is not allowed
                throw new ODataException(Error.Format(SRResources.NotAllowedLogicalOperator, logicalOperator, "AllowedLogicalOperators"));
            }

            // recursion case goes here
            ValidateQueryNode(binaryNode.Left, settings);
            ValidateQueryNode(binaryNode.Right, settings);
        }

        /// <summary>
        /// Override this method for the Arithmetic operators, including add, sub, mul, div, mod.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="binaryNode">The binary operator node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateArithmeticOperator(BinaryOperatorNode binaryNode, ODataValidationSettings settings)
        {
            Contract.Assert(binaryNode != null);
            Contract.Assert(settings != null);

            AllowedArithmeticOperators arithmeticOperator = ToArithmeticOperator(binaryNode);

            if ((settings.AllowedArithmeticOperators & arithmeticOperator) != arithmeticOperator)
            {
                // this means the given logical operator is not allowed
                throw new ODataException(Error.Format(SRResources.NotAllowedArithmeticOperator, arithmeticOperator, "AllowedArithmeticOperators"));
            }

            // recursion case goes here
            ValidateQueryNode(binaryNode.Left, settings);
            ValidateQueryNode(binaryNode.Right, settings);
        }

        /// <summary>
        /// Override this method to restrict the 'constant' inside the filter query.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="constantNode">The constant node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateConstantNode(ConstantNode constantNode, ODataValidationSettings settings)
        {
            // No default validation logic here.
        }

        /// <summary>
        /// Override this method to restrict the 'cast' inside the filter query.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="convertNode">The convert node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateConvertNode(ConvertNode convertNode, ODataValidationSettings settings)
        {
            Contract.Assert(convertNode != null);

            // Validate child nodes but not the ConvertNode itself.
            ValidateQueryNode(convertNode.Source, settings);
        }

        /// <summary>
        /// Override this method to restrict the '$count' inside the filter query.
        /// </summary>
        /// <param name="countNode">The count node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateCountNode(CountNode countNode, ODataValidationSettings settings)
        {
            Contract.Assert(countNode != null);
            Contract.Assert(settings != null);

            ValidateQueryNode(countNode.Source, settings);

            if (countNode.FilterClause != null)
            {
                ValidateQueryNode(countNode.FilterClause.Expression, settings);
            }

            if (countNode.SearchClause != null)
            {
                ValidateQueryNode(countNode.SearchClause.Expression, settings);
            }
        }

        /// <summary>
        /// Override this method for the navigation property node.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="sourceNode">The source node to validate.</param>
        /// <param name="navigationProperty">The navigation property.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateNavigationPropertyNode(QueryNode sourceNode, IEdmNavigationProperty navigationProperty, ODataValidationSettings settings)
        {
            Contract.Assert(navigationProperty != null);

            // Check whether the property is not filterable
            if (EdmHelpers.IsNotFilterable(navigationProperty, _property, _structuredType, _model,
                _defaultQuerySettings.EnableFilter))
            {
                throw new ODataException(Error.Format(SRResources.NotFilterablePropertyUsedInFilter,
                    navigationProperty.Name));
            }

            // recursion
            if (sourceNode != null)
            {
                ValidateQueryNode(sourceNode, settings);
            }
        }

        /// <summary>
        /// Override this method to validate the parameter used in the filter query.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="rangeVariable">The range variable node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateRangeVariable(RangeVariable rangeVariable, ODataValidationSettings settings)
        {
            // No default validation logic here.
        }

        /// <summary>
        /// Override this method to validate property accessors.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="propertyAccessNode">The single value property access node.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateSingleValuePropertyAccessNode(SingleValuePropertyAccessNode propertyAccessNode, ODataValidationSettings settings)
        {
            Contract.Assert(propertyAccessNode != null);
            Contract.Assert(settings != null);

            // Check whether the property is filterable.
            IEdmProperty property = propertyAccessNode.Property;
            bool notFilterable = false;
            if (propertyAccessNode.Source != null)
            {
                if (propertyAccessNode.Source.Kind == QueryNodeKind.SingleNavigationNode)
                {
                    SingleNavigationNode singleNavigationNode = propertyAccessNode.Source as SingleNavigationNode;
                    notFilterable = EdmHelpers.IsNotFilterable(property, singleNavigationNode.NavigationProperty,
                        singleNavigationNode.NavigationProperty.ToEntityType(), _model,
                        _defaultQuerySettings.EnableFilter);
                }
                else if (propertyAccessNode.Source.Kind == QueryNodeKind.SingleComplexNode)
                {
                    SingleComplexNode singleComplexNode = propertyAccessNode.Source as SingleComplexNode;
                    notFilterable = EdmHelpers.IsNotFilterable(property, singleComplexNode.Property,
                        property.DeclaringType, _model, _defaultQuerySettings.EnableFilter);
                }
                else
                {
                    notFilterable = EdmHelpers.IsNotFilterable(property, _property, _structuredType, _model,
                        _defaultQuerySettings.EnableFilter);
                }
            }

            if (notFilterable)
            {
                throw new ODataException(Error.Format(SRResources.NotFilterablePropertyUsedInFilter, property.Name));
            }

            ValidateQueryNode(propertyAccessNode.Source, settings);
        }

        /// <summary>
        /// Override this method to validate single complex property accessors.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="singleComplexNode">The single complex node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateSingleComplexNode(SingleComplexNode singleComplexNode, ODataValidationSettings settings)
        {
            Contract.Assert(singleComplexNode != null);
            Contract.Assert(settings != null);

            // Check whether the property is filterable.
            IEdmProperty property = singleComplexNode.Property;
            if (EdmHelpers.IsNotFilterable(property, _property, _structuredType, _model,
                _defaultQuerySettings.EnableFilter))
            {
                throw new ODataException(Error.Format(SRResources.NotFilterablePropertyUsedInFilter, property.Name));
            }

            ValidateQueryNode(singleComplexNode.Source, settings);
        }

        /// <summary>
        /// Override this method to validate collection property accessors.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="propertyAccessNode">The collection property access node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateCollectionPropertyAccessNode(CollectionPropertyAccessNode propertyAccessNode, ODataValidationSettings settings)
        {
            Contract.Assert(propertyAccessNode != null);
            Contract.Assert(settings != null);

            // Check whether the property is filterable.
            IEdmProperty property = propertyAccessNode.Property;
            if (EdmHelpers.IsNotFilterable(property, _property, _structuredType, _model,
                _defaultQuerySettings.EnableFilter))
            {
                throw new ODataException(Error.Format(SRResources.NotFilterablePropertyUsedInFilter, property.Name));
            }

            ValidateQueryNode(propertyAccessNode.Source, settings);
        }

        /// <summary>
        /// Override this method to validate collection complex property accessors.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="collectionComplexNode">The collection complex node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateCollectionComplexNode(CollectionComplexNode collectionComplexNode, ODataValidationSettings settings)
        {
            Contract.Assert(collectionComplexNode != null);
            Contract.Assert(settings != null);

            // Check whether the property is filterable.
            IEdmProperty property = collectionComplexNode.Property;
            if (EdmHelpers.IsNotFilterable(property, _property, _structuredType, _model,
                _defaultQuerySettings.EnableFilter))
            {
                throw new ODataException(Error.Format(SRResources.NotFilterablePropertyUsedInFilter, property.Name));
            }

            ValidateQueryNode(collectionComplexNode.Source, settings);
        }

        /// <summary>
        /// Override this method to validate Function calls, such as 'length', 'year', etc.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="node">The single value function call node to validate.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateSingleValueFunctionCallNode(SingleValueFunctionCallNode node, ODataValidationSettings settings)
        {
            Contract.Assert(node != null);
            Contract.Assert(settings != null);

            ValidateFunction(node.Name, settings);

            foreach (QueryNode argumentNode in node.Parameters)
            {
                ValidateQueryNode(argumentNode, settings);
            }
        }

        /// <summary>
        /// Override this method to validate single resource function calls, such as 'cast'.
        /// </summary>
        /// <param name="node">The node to validate.</param>
        /// <param name="settings">The settings to use while validating.</param>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit
        /// testing scenarios and is not intended to be called from user code. Call the Validate method to validate a
        /// <see cref="FilterQueryOption" /> instance.
        /// </remarks>
        protected virtual void ValidateSingleResourceFunctionCallNode(SingleResourceFunctionCallNode node, ODataValidationSettings settings)
        {
            Contract.Assert(node != null);
            Contract.Assert(settings != null);

            ValidateFunction(node.Name, settings);
            foreach (QueryNode argumentNode in node.Parameters)
            {
                ValidateQueryNode(argumentNode, settings);
            }
        }

        /// <summary>
        /// Override this method to validate the Not operator.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="unaryOperatorNode">The unary operator node.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateUnaryOperatorNode(UnaryOperatorNode unaryOperatorNode, ODataValidationSettings settings)
        {
            Contract.Assert(unaryOperatorNode != null);
            Contract.Assert(settings != null);

            ValidateQueryNode(unaryOperatorNode.Operand, settings);

            switch (unaryOperatorNode.OperatorKind)
            {
                case UnaryOperatorKind.Negate:
                case UnaryOperatorKind.Not:
                    if ((settings.AllowedLogicalOperators & AllowedLogicalOperators.Not) != AllowedLogicalOperators.Not)
                    {
                        throw new ODataException(Error.Format(SRResources.NotAllowedLogicalOperator, unaryOperatorNode.OperatorKind, "AllowedLogicalOperators"));
                    }
                    break;

                default:
                    throw Error.NotSupported(SRResources.UnaryNodeValidationNotSupported, unaryOperatorNode.OperatorKind, typeof(FilterQueryValidator).Name);
            }
        }

        /// <summary>
        /// Override this method if you want to visit each query node. 
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="node">The query node.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateQueryNode(QueryNode node, ODataValidationSettings settings)
        {
            Contract.Assert(settings != null);

            // Recursion guard to avoid stack overflows
            RuntimeHelpers.EnsureSufficientExecutionStack();

            SingleValueNode singleNode = node as SingleValueNode;
            CollectionNode collectionNode = node as CollectionNode;

            IncrementNodeCount(settings);

            if (singleNode != null)
            {
                ValidateSingleValueNode(singleNode, settings);
            }
            else if (collectionNode != null)
            {
                ValidateCollectionNode(collectionNode, settings);
            }
        }

        /// <summary>
        /// Override this method if you want to validate casts on resource collections.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="collectionResourceCastNode">The collection resource cast node.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateCollectionResourceCastNode(CollectionResourceCastNode collectionResourceCastNode, ODataValidationSettings settings)
        {
            Contract.Assert(collectionResourceCastNode != null);
            Contract.Assert(settings != null);

            ValidateQueryNode(collectionResourceCastNode.Source, settings);
        }

        /// <summary>
        /// Override this method if you want to validate casts on single resource.
        /// </summary>
        /// <remarks>
        /// This method is intended to be called from method overrides in subclasses. This method also supports unit-testing scenarios and is not intended to be called from user code.
        /// Call the Validate method to validate a <see cref="FilterQueryOption"/> instance.
        /// </remarks>
        /// <param name="singleResourceCastNode">The single resource cast node.</param>
        /// <param name="settings">The validation settings.</param>
        protected virtual void ValidateSingleResourceCastNode(SingleResourceCastNode singleResourceCastNode, ODataValidationSettings settings)
        {
            Contract.Assert(singleResourceCastNode != null);
            Contract.Assert(settings != null);

            ValidateQueryNode(singleResourceCastNode.Source, settings);
        }

        internal static FilterQueryValidator GetFilterQueryValidator(ODataQueryContext context)
        {
            return context?.RequestContainer?.GetRequiredService<FilterQueryValidator>()
                ?? new FilterQueryValidator();
        }

        private void EnterLambda(ODataValidationSettings validationSettings)
        {
            Contract.Assert(validationSettings != null);

            if (_currentAnyAllExpressionDepth >= validationSettings.MaxAnyAllExpressionDepth)
            {
                throw new ODataException(Error.Format(SRResources.MaxAnyAllExpressionLimitExceeded, validationSettings.MaxAnyAllExpressionDepth, "MaxAnyAllExpressionDepth"));
            }

            _currentAnyAllExpressionDepth++;
        }

        private void ExitLambda()
        {
            Contract.Assert(_currentAnyAllExpressionDepth > 0);
            _currentAnyAllExpressionDepth--;
        }

        private void IncrementNodeCount(ODataValidationSettings validationSettings)
        {
            if (_currentNodeCount >= validationSettings.MaxNodeCount)
            {
                throw new ODataException(Error.Format(SRResources.MaxNodeLimitExceeded, validationSettings.MaxNodeCount, "MaxNodeCount"));
            }

            _currentNodeCount++;
        }

        private void ValidateCollectionNode(CollectionNode node, ODataValidationSettings settings)
        {
            switch (node.Kind)
            {
                case QueryNodeKind.CollectionPropertyAccess:
                    CollectionPropertyAccessNode propertyAccessNode = node as CollectionPropertyAccessNode;
                    ValidateCollectionPropertyAccessNode(propertyAccessNode, settings);
                    break;

                case QueryNodeKind.CollectionComplexNode:
                    CollectionComplexNode collectionComplexNode = node as CollectionComplexNode;
                    ValidateCollectionComplexNode(collectionComplexNode, settings);
                    break;

                case QueryNodeKind.CollectionNavigationNode:
                    CollectionNavigationNode navigationNode = node as CollectionNavigationNode;
                    ValidateNavigationPropertyNode(navigationNode.Source, navigationNode.NavigationProperty, settings);
                    break;

                case QueryNodeKind.CollectionResourceCast:
                    ValidateCollectionResourceCastNode(node as CollectionResourceCastNode, settings);
                    break;

                case QueryNodeKind.CollectionFunctionCall:
                case QueryNodeKind.CollectionResourceFunctionCall:
                case QueryNodeKind.CollectionOpenPropertyAccess:
                // Unused or have unknown uses.
                default:
                    throw Error.NotSupported(SRResources.QueryNodeValidationNotSupported, node.Kind, typeof(FilterQueryValidator).Name);
            }
        }

        /// <summary>
        /// The recursive method that validate most of the query node type is of SingleValueNode type.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="settings"></param>
        private void ValidateSingleValueNode(SingleValueNode node, ODataValidationSettings settings)
        {
            switch (node.Kind)
            {
                case QueryNodeKind.BinaryOperator:
                    ValidateBinaryOperatorNode(node as BinaryOperatorNode, settings);
                    break;

                case QueryNodeKind.Constant:
                    ValidateConstantNode(node as ConstantNode, settings);
                    break;

                case QueryNodeKind.Convert:
                    ValidateConvertNode(node as ConvertNode, settings);
                    break;

                case QueryNodeKind.Count:
                    ValidateCountNode(node as CountNode, settings);
                    break;

                case QueryNodeKind.ResourceRangeVariableReference:
                    ValidateRangeVariable((node as ResourceRangeVariableReferenceNode).RangeVariable, settings);
                    break;

                case QueryNodeKind.NonResourceRangeVariableReference:
                    ValidateRangeVariable((node as NonResourceRangeVariableReferenceNode).RangeVariable, settings);
                    break;

                case QueryNodeKind.SingleValuePropertyAccess:
                    ValidateSingleValuePropertyAccessNode(node as SingleValuePropertyAccessNode, settings);
                    break;

                case QueryNodeKind.SingleComplexNode:
                    ValidateSingleComplexNode(node as SingleComplexNode, settings);
                    break;

                case QueryNodeKind.UnaryOperator:
                    ValidateUnaryOperatorNode(node as UnaryOperatorNode, settings);
                    break;

                case QueryNodeKind.SingleValueFunctionCall:
                    ValidateSingleValueFunctionCallNode(node as SingleValueFunctionCallNode, settings);
                    break;

                case QueryNodeKind.SingleResourceFunctionCall:
                    ValidateSingleResourceFunctionCallNode((SingleResourceFunctionCallNode)node, settings);
                    break;

                case QueryNodeKind.SingleNavigationNode:
                    SingleNavigationNode navigationNode = node as SingleNavigationNode;
                    ValidateNavigationPropertyNode(navigationNode.Source, navigationNode.NavigationProperty, settings);
                    break;

                case QueryNodeKind.SingleResourceCast:
                    ValidateSingleResourceCastNode(node as SingleResourceCastNode, settings);
                    break;

                case QueryNodeKind.Any:
                    ValidateAnyNode(node as AnyNode, settings);
                    break;

                case QueryNodeKind.All:
                    ValidateAllNode(node as AllNode, settings);
                    break;

                case QueryNodeKind.SingleValueOpenPropertyAccess:
                    //no validation on open values?
                    break;

                case QueryNodeKind.In:
                    // No setting validations
                    break;

                case QueryNodeKind.NamedFunctionParameter:
                case QueryNodeKind.ParameterAlias:
                case QueryNodeKind.EntitySet:
                case QueryNodeKind.KeyLookup:
                case QueryNodeKind.SearchTerm:
                // Unused or have unknown uses.
                default:
                    throw Error.NotSupported(SRResources.QueryNodeValidationNotSupported, node.Kind, typeof(FilterQueryValidator).Name);
            }
        }

        private static void ValidateFunction(string functionName, ODataValidationSettings settings)
        {
            Contract.Assert(settings != null);

            AllowedFunctions convertedFunction = ToODataFunction(functionName);
            if ((settings.AllowedFunctions & convertedFunction) != convertedFunction)
            {
                // this means the given function is not allowed
                throw new ODataException(Error.Format(SRResources.NotAllowedFunction, functionName, "AllowedFunctions"));
            }
        }

        private static AllowedFunctions ToODataFunction(string functionName)
        {
            AllowedFunctions result = AllowedFunctions.None;

            switch (functionName)
            {
                case "any":
                    result = AllowedFunctions.Any;
                    break;
                case "all":
                    result = AllowedFunctions.All;
                    break;
                case "cast":
                    result = AllowedFunctions.Cast;
                    break;
                case ClrCanonicalFunctions.CeilingFunctionName:
                    result = AllowedFunctions.Ceiling;
                    break;
                case ClrCanonicalFunctions.ConcatFunctionName:
                    result = AllowedFunctions.Concat;
                    break;
                case ClrCanonicalFunctions.ContainsFunctionName:
                    result = AllowedFunctions.Contains;
                    break;
                case ClrCanonicalFunctions.DayFunctionName:
                    result = AllowedFunctions.Day;
                    break;
                case ClrCanonicalFunctions.EndswithFunctionName:
                    result = AllowedFunctions.EndsWith;
                    break;
                case ClrCanonicalFunctions.FloorFunctionName:
                    result = AllowedFunctions.Floor;
                    break;
                case ClrCanonicalFunctions.HourFunctionName:
                    result = AllowedFunctions.Hour;
                    break;
                case ClrCanonicalFunctions.IndexofFunctionName:
                    result = AllowedFunctions.IndexOf;
                    break;
                case "isof":
                    result = AllowedFunctions.IsOf;
                    break;
                case ClrCanonicalFunctions.LengthFunctionName:
                    result = AllowedFunctions.Length;
                    break;
                case ClrCanonicalFunctions.MinuteFunctionName:
                    result = AllowedFunctions.Minute;
                    break;
                case ClrCanonicalFunctions.MonthFunctionName:
                    result = AllowedFunctions.Month;
                    break;
                case ClrCanonicalFunctions.RoundFunctionName:
                    result = AllowedFunctions.Round;
                    break;
                case ClrCanonicalFunctions.SecondFunctionName:
                    result = AllowedFunctions.Second;
                    break;
                case ClrCanonicalFunctions.StartswithFunctionName:
                    result = AllowedFunctions.StartsWith;
                    break;
                case ClrCanonicalFunctions.SubstringFunctionName:
                    result = AllowedFunctions.Substring;
                    break;
                case ClrCanonicalFunctions.TolowerFunctionName:
                    result = AllowedFunctions.ToLower;
                    break;
                case ClrCanonicalFunctions.ToupperFunctionName:
                    result = AllowedFunctions.ToUpper;
                    break;
                case ClrCanonicalFunctions.TrimFunctionName:
                    result = AllowedFunctions.Trim;
                    break;
                case ClrCanonicalFunctions.YearFunctionName:
                    result = AllowedFunctions.Year;
                    break;
                case ClrCanonicalFunctions.DateFunctionName:
                    result = AllowedFunctions.Date;
                    break;
                case ClrCanonicalFunctions.TimeFunctionName:
                    result = AllowedFunctions.Time;
                    break;
                case ClrCanonicalFunctions.FractionalSecondsFunctionName:
                    result = AllowedFunctions.FractionalSeconds;
                    break;
                default:
                    // should never be here
                    Contract.Assert(true, "ToODataFunction should never be here.");
                    break;
            }

            return result;
        }

        private static AllowedLogicalOperators ToLogicalOperator(BinaryOperatorNode binaryNode)
        {
            AllowedLogicalOperators result = AllowedLogicalOperators.None;

            switch (binaryNode.OperatorKind)
            {
                case BinaryOperatorKind.Equal:
                    result = AllowedLogicalOperators.Equal;
                    break;

                case BinaryOperatorKind.NotEqual:
                    result = AllowedLogicalOperators.NotEqual;
                    break;

                case BinaryOperatorKind.And:
                    result = AllowedLogicalOperators.And;
                    break;

                case BinaryOperatorKind.GreaterThan:
                    result = AllowedLogicalOperators.GreaterThan;
                    break;

                case BinaryOperatorKind.GreaterThanOrEqual:
                    result = AllowedLogicalOperators.GreaterThanOrEqual;
                    break;

                case BinaryOperatorKind.LessThan:
                    result = AllowedLogicalOperators.LessThan;
                    break;

                case BinaryOperatorKind.LessThanOrEqual:
                    result = AllowedLogicalOperators.LessThanOrEqual;
                    break;

                case BinaryOperatorKind.Or:
                    result = AllowedLogicalOperators.Or;
                    break;

                case BinaryOperatorKind.Has:
                    result = AllowedLogicalOperators.Has;
                    break;

                default:
                    // should never be here
                    Contract.Assert(false, "ToLogicalOperator should never be here.");
                    break;
            }

            return result;
        }

        private static AllowedArithmeticOperators ToArithmeticOperator(BinaryOperatorNode binaryNode)
        {
            AllowedArithmeticOperators result = AllowedArithmeticOperators.None;

            switch (binaryNode.OperatorKind)
            {
                case BinaryOperatorKind.Add:
                    result = AllowedArithmeticOperators.Add;
                    break;

                case BinaryOperatorKind.Divide:
                    result = AllowedArithmeticOperators.Divide;
                    break;

                case BinaryOperatorKind.Modulo:
                    result = AllowedArithmeticOperators.Modulo;
                    break;

                case BinaryOperatorKind.Multiply:
                    result = AllowedArithmeticOperators.Multiply;
                    break;

                case BinaryOperatorKind.Subtract:
                    result = AllowedArithmeticOperators.Subtract;
                    break;

                default:
                    // should never be here
                    Contract.Assert(false, "ToArithmeticOperator should never be here.");
                    break;
            }

            return result;
        }
    }
}
