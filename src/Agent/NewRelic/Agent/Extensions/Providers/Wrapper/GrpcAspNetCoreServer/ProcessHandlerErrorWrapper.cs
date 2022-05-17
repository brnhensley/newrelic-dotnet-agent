﻿// Copyright 2020 New Relic, Inc. All rights reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
using NewRelic.Agent.Api;
using NewRelic.Agent.Extensions.Providers.Wrapper;
using NewRelic.Reflection;

namespace NewRelic.Providers.Wrapper.GrpcAspNetCoreServer
{
    /// <summary>
    /// This wrapper is used to capture critical grpc error and status code.
    /// </summary>
    public class ProcessHandlerErrorWrapper : IWrapper
    {
        private static PropertyInfo _statusCodeProperty;

        private static Func<object, object> _getStatusFunc;
        public static Func<object, object> GetStatusFunc => _getStatusFunc ??= VisibilityBypasser.Instance.GeneratePropertyAccessor<object>("Grpc.AspNetCore.Server", "Grpc.AspNetCore.Server.Internal.HttpContextServerCallContext", "StatusCore");

        public bool IsTransactionRequired => true;

        private const string WrapperName = "ProcessHandlerErrorWrapper";

        public CanWrapResponse CanWrap(InstrumentedMethodInfo methodInfo)
        {
            return new CanWrapResponse(WrapperName.Equals(methodInfo.RequestedWrapperName));
        }

        public AfterWrappedMethodDelegate BeforeWrappedMethod(InstrumentedMethodCall instrumentedMethodCall, IAgent agent, ITransaction transaction)
        {
            return Delegates.GetDelegateFor(onComplete: ()=>
            {

                var status = GetStatusFunc(instrumentedMethodCall.MethodCall.InvocationTarget);

                if (_statusCodeProperty == null)
                {
                    _statusCodeProperty = status.GetType().GetProperty("StatusCode");
                }

                var statusCode = _statusCodeProperty.GetValue(status);

                transaction.SetGrpcStatusCode((int)statusCode);

                //Need a grpc error configuration to toggle on/off error captureing here. 

                var ex = instrumentedMethodCall.MethodCall.MethodArguments[0] as Exception;

                transaction.NoticeError(ex);
            });
        }
    }
}
