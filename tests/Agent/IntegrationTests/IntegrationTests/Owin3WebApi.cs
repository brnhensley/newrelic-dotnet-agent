﻿/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System;
using System.Collections.Generic;
using System.Linq;
using NewRelic.Agent.IntegrationTestHelpers;
using NewRelic.Agent.IntegrationTestHelpers.Models;
using NewRelic.Testing.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace NewRelic.Agent.IntegrationTests
{
    public class Owin3WebApi : IClassFixture<RemoteServiceFixtures.Owin3WebApi>
    {
        private readonly RemoteServiceFixtures.Owin3WebApi _fixture;

        public Owin3WebApi(RemoteServiceFixtures.Owin3WebApi fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _fixture.TestLogger = output;
            _fixture.Actions
            (
                setupConfiguration: () =>
                {
                    var configPath = fixture.DestinationNewRelicConfigFilePath;
                    var configModifier = new NewRelicConfigModifier(configPath);

                    configModifier.ForceTransactionTraces();

                    CommonUtils.ModifyOrCreateXmlAttributeInNewRelicConfig(configPath, new[] { "configuration", "log" }, "level", "debug");
                    CommonUtils.ModifyOrCreateXmlAttributeInNewRelicConfig(configPath, new[] { "configuration", "requestParameters" }, "enabled", "true");
                },
                exerciseApplication: () =>
                {
                    _fixture.GetData();
                    _fixture.Get();
                    _fixture.Get404();
                    _fixture.GetId();
                    _fixture.Post();

                    _fixture.AgentLog.WaitForLogLine(AgentLogFile.AnalyticsEventDataLogLineRegex, TimeSpan.FromMinutes(2));
                    _fixture.AgentLog.WaitForLogLine(AgentLogFile.TransactionSampleLogLineRegex, TimeSpan.FromMinutes(2));
                }
            );
            _fixture.Initialize();
        }

        [Fact]
        public void Test()
        {
            var expectedMetrics = new List<Assertions.ExpectedMetric>
            {
                new Assertions.ExpectedMetric {metricName = @"WebTransaction", callCount = 5},
                new Assertions.ExpectedMetric {metricName = @"WebTransaction/WebAPI/Values/Get", callCount = 3},
                new Assertions.ExpectedMetric {metricName = @"WebTransaction/WebAPI/Values/Post", callCount = 1},
                new Assertions.ExpectedMetric {metricName = @"WebTransaction/WebAPI/Values/Get404", callCount = 1},
                new Assertions.ExpectedMetric {metricName = @"DotNet/Values/Get", callCount = 3},
                new Assertions.ExpectedMetric {metricName = @"DotNet/Values/Post", callCount = 1},
                new Assertions.ExpectedMetric {metricName = @"DotNet/Values/Get404", callCount = 1},
                new Assertions.ExpectedMetric {metricName = @"External/all", callCount = 1},
                new Assertions.ExpectedMetric {metricName = @"External/allWeb", callCount = 1},
                new Assertions.ExpectedMetric {metricName = @"External/www.google.com/all", callCount = 1},
                new Assertions.ExpectedMetric {metricName = @"External/www.google.com/Stream/GET", callCount = 1}
            };
            var unexpectedMetrics = new List<Assertions.ExpectedMetric>
            {
                new Assertions.ExpectedMetric {metricName = @"OtherTransaction/all", callCount = 5},
            };

            var expectedAttributes = new Dictionary<string, string>
            {
                // self-hosted applications don't support request parameter capturing
                // { "request.parameters.data", "mything" },
            };
            var unexpectedAttributes = new List<string>
            {
                "service.request.otherValue",
            };

            var metrics = _fixture.AgentLog.GetMetrics().ToList();

            var transactionSamples = _fixture.AgentLog.GetTransactionSamples();
            //this is the transaction trace that is generally returned, but this 
            //is not necessarily always the case
            var getTransactionSample = transactionSamples
                .Where(sample => sample.Path == "WebTransaction/WebAPI/Values/Get")
                .FirstOrDefault();
            var get404TransactionSample = transactionSamples
                .Where(sample => sample.Path == "WebTransaction/WebAPI/Values/Get404")
                .FirstOrDefault();
            var postTransactionSample = transactionSamples
                .Where(sample => sample.Path == "WebTransaction/WebAPI/Values/Post")
                .FirstOrDefault();

            var transactionEventWithExternal = _fixture.AgentLog.GetTransactionEvents()
                .Where(e => e.IntrinsicAttributes.ContainsKey("externalDuration"))
                .FirstOrDefault();

            NrAssert.Multiple(
                () => Assertions.MetricsExist(expectedMetrics, metrics),
                () => Assertions.MetricsDoNotExist(unexpectedMetrics, metrics),
                () => Assert.NotNull(transactionEventWithExternal),
                () => Assert.Empty(_fixture.AgentLog.GetErrorTraces()),
                () => Assert.Empty(_fixture.AgentLog.GetErrorEvents())
            );

            // check the transaction trace samples
            TransactionSample traceToCheck = null;
            List<string> expectedTransactionTraceSegments = null;
            List<string> doNotExistTraceSegments = null;
            if (getTransactionSample != null)
            {
                traceToCheck = getTransactionSample;
                expectedTransactionTraceSegments = new List<string>
                {
                    @"DotNet/Microsoft.Owin.Host.HttpListener.OwinHttpListener/ProcessRequestAsync",
                    @"DotNet/Values/Get"
                };
                doNotExistTraceSegments = new List<string>
                {
                    @"DotNet/Values/Get404",
                    @"DotNet/Values/Post"
                };
            }
            else if (get404TransactionSample != null)
            {
                traceToCheck = get404TransactionSample;
                expectedTransactionTraceSegments = new List<string>
                {
                    @"DotNet/Microsoft.Owin.Host.HttpListener.OwinHttpListener/ProcessRequestAsync",
                    @"DotNet/Values/Get404"
                };
                doNotExistTraceSegments = new List<string>
                {
                    @"External/www.google.com/Stream/GET",
                    @"DotNet/Values/Get",
                    @"DotNet/Values/Post"
                };
            }
            else if (postTransactionSample != null)
            {
                traceToCheck = postTransactionSample;
                expectedTransactionTraceSegments = new List<string>
                {
                    @"DotNet/Microsoft.Owin.Host.HttpListener.OwinHttpListener/ProcessRequestAsync",
                    @"DotNet/Values/Post"
                };
                doNotExistTraceSegments = new List<string>
                {
                    @"External/www.google.com/Stream/GET",
                    @"DotNet/Values/Get404",
                    @"DotNet/Values/Get"
                };
            }

            NrAssert.Multiple(
                () => Assert.NotNull(traceToCheck),
                () => Assertions.TransactionTraceDoesNotHaveAttributes(unexpectedAttributes, TransactionTraceAttributeType.Agent,
                    traceToCheck),
                () => Assertions.TransactionTraceSegmentsExist(expectedTransactionTraceSegments, traceToCheck),
                () => Assertions.TransactionTraceSegmentsNotExist(doNotExistTraceSegments, traceToCheck),
                () => Assertions.TransactionTraceHasAttributes(expectedAttributes, TransactionTraceAttributeType.Agent,
                    traceToCheck));
        }
    }
}
