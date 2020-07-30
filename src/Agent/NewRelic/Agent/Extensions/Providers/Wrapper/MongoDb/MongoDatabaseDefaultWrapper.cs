﻿/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using NewRelic.Agent.Extensions.Providers.Wrapper;
using NewRelic.SystemExtensions;

namespace NewRelic.Providers.Wrapper.MongoDb
{
    public class MongoDatabaseDefaultWrapper : IWrapper
    {
        public bool IsTransactionRequired => true;

        public CanWrapResponse CanWrap(InstrumentedMethodInfo methodInfo)
        {
            var method = methodInfo.Method;
            var canWrap = method.MatchesAny(assemblyName: "MongoDB.Driver", typeName: "MongoDB.Driver.MongoDatabase",
                methodName: "CreateCollection", parameterSignature: "System.String,MongoDB.Driver.IMongoCollectionOptions");
            return new CanWrapResponse(canWrap);
        }

        public AfterWrappedMethodDelegate BeforeWrappedMethod(InstrumentedMethodCall instrumentedMethodCall, IAgentWrapperApi agentWrapperApi, ITransaction transaction)
        {
            var operation = instrumentedMethodCall.MethodCall.Method.MethodName;
            var model = GetCollectionName(instrumentedMethodCall.MethodCall);
            var segment = transaction.StartDatastoreSegment(instrumentedMethodCall.MethodCall, operation, DatastoreVendor.MongoDB, model);

            return Delegates.GetDelegateFor(segment);
        }

        private string GetCollectionName(MethodCall methodCall)
        {
            return methodCall.MethodArguments.ExtractNotNullAs<string>(0);
        }
    }
}
