﻿// Copyright 2020 New Relic, Inc. All rights reserved.
// SPDX-License-Identifier: Apache-2.0

using NewRelic.Agent.Api;
using NewRelic.Agent.Extensions.Providers.Wrapper;
using NewRelic.Agent.Core.Segments;
using NewRelic.Reflection;
using System;

namespace NewRelic.Providers.Wrapper.GrpcNetClient
{
    public class FinishCallWrapper:IWrapper
    {
        public bool IsTransactionRequired => true;

        private const string WrapperName = "FinishCallWrapper";

        public CanWrapResponse CanWrap(InstrumentedMethodInfo methodInfo)
        {
            return new CanWrapResponse(WrapperName.Equals(methodInfo.RequestedWrapperName));
        }

        public AfterWrappedMethodDelegate BeforeWrappedMethod(InstrumentedMethodCall instrumentedMethodCall, IAgent agent, ITransaction transaction)
        {
            var status = (Grpc.Core.Status)instrumentedMethodCall.MethodCall.MethodArguments[3];

            var segment = transaction.CurrentSegment as Segment;

            var externalData = segment.Data as ExternalSegmentData;

            externalData.SetGrpcStatusCode((int)status.StatusCode);

            segment.End();
            transaction.Release();

            return Delegates.NoOp;
        }
    }
}
