﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using Microsoft.AspNetCore.OData.Formatter.Serialization;
using Microsoft.AspNetCore.OData.Tests.Commons;
using Microsoft.OData;
using Xunit;

namespace Microsoft.AspNetCore.OData.Tests.Formatter.Serialization
{
    public class ODataValueExtensionsTest
    {
        public static TheoryDataSet<ODataValue, object> GetInnerValueTestData
        {
            get
            {
                ODataCollectionValue collectionValue = new ODataCollectionValue();
                ODataStreamReferenceValue streamReferenceValue = new ODataStreamReferenceValue();

                return new TheoryDataSet<ODataValue, object>
                {
                    { new ODataPrimitiveValue(100), 100 },
                    { new ODataNullValue(), null },
                    { collectionValue, collectionValue },
                    { streamReferenceValue, streamReferenceValue },
                    { null, null } 
                };
            }
        }

        [Theory]
        [MemberData(nameof(GetInnerValueTestData))]
        public void GetInnerValue_Returns_CorrectObject(ODataValue value, object expectedResult)
        {
            Assert.Equal(expectedResult, value.GetInnerValue());
        }
    }
}
