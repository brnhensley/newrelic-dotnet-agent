﻿/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System.Collections.Generic;
using System.Linq;
using NewRelic.Agent.IntegrationTestHelpers;
using NewRelic.Agent.IntegrationTestHelpers.Models;
using NewRelic.Agent.IntegrationTests.Shared;
using NewRelic.Testing.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace NewRelic.Agent.UnboundedIntegrationTests
{
    public class OracleAsyncTests : IClassFixture<RemoteServiceFixtures.OracleBasicMvcFixture>
    {
        private readonly RemoteServiceFixtures.OracleBasicMvcFixture _fixture;

        public OracleAsyncTests(RemoteServiceFixtures.OracleBasicMvcFixture fixture, ITestOutputHelper output)
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

                    CommonUtils.ModifyOrCreateXmlAttributeInNewRelicConfig(configPath, new[] { "configuration", "transactionTracer" }, "explainThreshold", "1");

                    var instrumentationFilePath = $@"{fixture.DestinationNewRelicExtensionsDirectoryPath}\NewRelic.Providers.Wrapper.SqlAsync.Instrumentation.xml";
                    CommonUtils.SetAttributeOnTracerFactoryInNewRelicInstrumentation(
                       instrumentationFilePath,
                        "", "enabled", "true");
                },
                exerciseApplication: () =>
                {
                    _fixture.GetOracleAsync();
                }
            );
            _fixture.Initialize();
        }

        [Fact]
        public void Test()
        {
            var expectedMetrics = new List<Assertions.ExpectedMetric>
            {
                // The Oracle driver executes an unrelated DECLARE query while connecting
                new Assertions.ExpectedMetric { metricName = @"Datastore/all", callCount = 6 },
                new Assertions.ExpectedMetric { metricName = @"Datastore/allWeb", callCount = 6 },
                new Assertions.ExpectedMetric { metricName = @"Datastore/Oracle/all", callCount = 6 },
                new Assertions.ExpectedMetric { metricName = @"Datastore/Oracle/allWeb", callCount = 6 },
                new Assertions.ExpectedMetric { metricName = $@"Datastore/instance/Oracle/{OracleConfiguration.OracleServer}/{OracleConfiguration.OraclePort}", callCount = 6},
                new Assertions.ExpectedMetric { metricName = @"Datastore/operation/Oracle/declare", callCount = 1 },
                new Assertions.ExpectedMetric { metricName = @"Datastore/operation/Oracle/select", callCount = 3 },
                new Assertions.ExpectedMetric { metricName = @"Datastore/statement/Oracle/user_tables/select", callCount = 1 },
                new Assertions.ExpectedMetric { metricName = @"Datastore/statement/Oracle/user_tables/select", callCount = 1, metricScope = "WebTransaction/MVC/DefaultController/OracleAsync"},
                //ExecuteScalar() double instrumented: DOTNET-1800
                new Assertions.ExpectedMetric { metricName = $@"Datastore/statement/Oracle/{_fixture.TableName}/select", callCount = 2 },
                new Assertions.ExpectedMetric { metricName = $@"Datastore/statement/Oracle/{_fixture.TableName}/select", callCount = 2, metricScope = "WebTransaction/MVC/DefaultController/OracleAsync"},
                new Assertions.ExpectedMetric { metricName = @"Datastore/operation/Oracle/insert", callCount = 1 },
                new Assertions.ExpectedMetric { metricName = $@"Datastore/statement/Oracle/{_fixture.TableName}/insert", callCount = 1 },
                new Assertions.ExpectedMetric { metricName = $@"Datastore/statement/Oracle/{_fixture.TableName}/insert", callCount = 1, metricScope = "WebTransaction/MVC/DefaultController/OracleAsync"},
                new Assertions.ExpectedMetric { metricName = @"Datastore/operation/Oracle/delete", callCount = 1 },
                new Assertions.ExpectedMetric { metricName = $@"Datastore/statement/Oracle/{_fixture.TableName}/delete", callCount = 1 },
                new Assertions.ExpectedMetric { metricName = $@"Datastore/statement/Oracle/{_fixture.TableName}/delete", callCount = 1, metricScope = "WebTransaction/MVC/DefaultController/OracleAsync"}
                // We don't currently have instrumentation for OracleDataReader.ReadAsync
                //new Assertions.ExpectedMetric { metricName = @"DotNet/DatabaseResult/Iterate", callCount = 3 },
                //new Assertions.ExpectedMetric { metricName = @"DotNet/DatabaseResult/Iterate", callCount = 3, metricScope = "WebTransaction/MVC/DefaultController/OracleAsync"}
            };
            var unexpectedMetrics = new List<Assertions.ExpectedMetric>
            {
                // The datastore operation happened inside a web transaction so there should be no allOther metrics
                new Assertions.ExpectedMetric { metricName = @"Datastore/allOther" },
                new Assertions.ExpectedMetric { metricName = @"Datastore/Oracle/allOther" },

                // The operation metric should not be scoped because the statement metric is scoped instead
                new Assertions.ExpectedMetric { metricName = @"Datastore/operation/Oracle/select", metricScope = "WebTransaction/MVC/DefaultController/OracleAsync" },
                new Assertions.ExpectedMetric { metricName = @"Datastore/operation/Oracle/insert", metricScope = "WebTransaction/MVC/DefaultController/OracleAsync" },
                new Assertions.ExpectedMetric { metricName = @"Datastore/operation/Oracle/delete", metricScope = "WebTransaction/MVC/DefaultController/OracleAsync" }
            };

            var expectedTransactionTraceSegments = new List<string>
            {
                "Datastore/statement/Oracle/user_tables/select"
            };

            var expectedTransactionEventIntrinsicAttributes = new List<string>
            {
                "databaseDuration"
            };

            var expectedTransactionTraceSegmentParameters = new List<Assertions.ExpectedSegmentParameter>
            {
                new Assertions.ExpectedSegmentParameter { segmentName = "Datastore/statement/Oracle/user_tables/select", parameterName = "sql", parameterValue = "SELECT DEGREE FROM user_tables WHERE ROWNUM <= ?"}
            };

            var expectedSqlTraces = new List<Assertions.ExpectedSqlTrace>
            {
                new Assertions.ExpectedSqlTrace
                {
                    TransactionName = "WebTransaction/MVC/DefaultController/OracleAsync",
                    Sql = "SELECT DEGREE FROM user_tables WHERE ROWNUM <= ?",
                    DatastoreMetricName = "Datastore/statement/Oracle/user_tables/select",
                    HasExplainPlan = false
                },
                new Assertions.ExpectedSqlTrace
                {
                    TransactionName = "WebTransaction/MVC/DefaultController/OracleAsync",
                    Sql = $"SELECT COUNT(*) FROM {_fixture.TableName}",
                    DatastoreMetricName = $"Datastore/statement/Oracle/{_fixture.TableName}/select",

                    HasExplainPlan = false
                },
                new Assertions.ExpectedSqlTrace
                {
                    TransactionName = "WebTransaction/MVC/DefaultController/OracleAsync",
                    Sql = $"INSERT INTO {_fixture.TableName} (HOTEL_ID, BOOKING_DATE) VALUES (?, SYSDATE)",
                    DatastoreMetricName = $"Datastore/statement/Oracle/{_fixture.TableName}/insert",

                    HasExplainPlan = false
                },
                new Assertions.ExpectedSqlTrace
                {
                    TransactionName = "WebTransaction/MVC/DefaultController/OracleAsync",
                    Sql = $"DELETE FROM {_fixture.TableName} WHERE HOTEL_ID = ?",
                    DatastoreMetricName = $"Datastore/statement/Oracle/{_fixture.TableName}/delete",

                    HasExplainPlan = false
                }
            };

            var metrics = _fixture.AgentLog.GetMetrics().ToList();
            var transactionSample = _fixture.AgentLog.TryGetTransactionSample("WebTransaction/MVC/DefaultController/OracleAsync");
            var transactionEvent = _fixture.AgentLog.TryGetTransactionEvent("WebTransaction/MVC/DefaultController/OracleAsync");
            var sqlTraces = _fixture.AgentLog.GetSqlTraces().ToList();

            NrAssert.Multiple(
                () => Assert.NotNull(transactionSample),
                () => Assert.NotNull(transactionEvent)
                );

            NrAssert.Multiple
            (
                () => Assertions.MetricsExist(expectedMetrics, metrics),
                () => Assertions.MetricsDoNotExist(unexpectedMetrics, metrics),
                () => Assertions.TransactionTraceSegmentsExist(expectedTransactionTraceSegments, transactionSample),
                () => Assertions.TransactionEventHasAttributes(expectedTransactionEventIntrinsicAttributes, TransactionEventAttributeType.Intrinsic, transactionEvent),
                () => Assertions.SqlTraceExists(expectedSqlTraces, sqlTraces),
                () => Assertions.TransactionTraceSegmentParametersExist(expectedTransactionTraceSegmentParameters, transactionSample)
            );
        }
    }
}
