﻿/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System;
using NewRelic.Agent.Extensions.Providers.Wrapper;
using NewRelic.SystemExtensions;

namespace NewRelic.Providers.Wrapper.OpenRasta
{
    public class GetHandlerWrapper : IWrapper
    {
        private const string TypeName = "OpenRasta.Hosting.AspNet.OpenRastaHandler";
        private const string MethodName = "GetHandler";

        public bool IsTransactionRequired => true;

        public CanWrapResponse CanWrap(InstrumentedMethodInfo methodInfo)
        {
            var method = methodInfo.Method;
            var canWrap = method.MatchesAny(assemblyName: "OpenRasta.Hosting.AspNet", typeName: TypeName, methodName: MethodName);
            return new CanWrapResponse(canWrap);
        }

        public AfterWrappedMethodDelegate BeforeWrappedMethod(InstrumentedMethodCall instrumentedMethodCall, IAgentWrapperApi agentWrapperApi, ITransaction transaction)
        {
            //Handler name - much like the controller name from ASP .NET MVC / Web API
            var httpContext = instrumentedMethodCall.MethodCall.MethodArguments.ExtractNotNullAs<System.Web.HttpContext>(0);
            var handlerName = httpContext.Request.RawUrl.Substring(httpContext.Request.RawUrl.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase) + 1);

            //Since Open Rasta uses convention based routing (i.e. HTTP verbs) there will be little deviation in this name
            //however we should still pull it out of the MethodArguments for consistency and future-proofing
            var action = instrumentedMethodCall.MethodCall.MethodArguments.ExtractAs<string>(1);
            var actionName = action ?? instrumentedMethodCall.MethodCall.Method.MethodName;

            //Title casing actionName
            System.Globalization.TextInfo textInfo = new System.Globalization.CultureInfo("en-US", false).TextInfo;
            actionName = textInfo.ToLower(actionName);

            transaction.SetWebTransactionName(WebTransactionType.OpenRasta, $"{handlerName}/{actionName}", 6);

            var segment = transaction.StartMethodSegment(instrumentedMethodCall.MethodCall, handlerName, actionName);
            return segment == null ? Delegates.NoOp : Delegates.GetDelegateFor(segment.End);
        }
    }
}
