﻿/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System;
using System.Collections.Generic;
using System.Linq;
using NewRelic.Agent.Configuration;
using NewRelic.Agent.Core.Aggregators;
using NewRelic.Agent.Core.CallStack;
using NewRelic.Agent.Core.Database;
using NewRelic.Agent.Core.Errors;
using NewRelic.Agent.Core.Metrics;
using NewRelic.Agent.Core.Timing;
using NewRelic.Agent.Core.Transactions;
using NewRelic.Agent.Core.Transactions.TransactionNames;
using NewRelic.Agent.Core.WireModels;
using NewRelic.Agent.Core.Wrapper.AgentWrapperApi.Builders;
using NewRelic.Agent.Core.Wrapper.AgentWrapperApi.Data;
using NewRelic.Testing.Assertions;
using NUnit.Framework;
using Telerik.JustMock;


namespace NewRelic.Agent.Core.Transformers.TransactionTransformer
{
    [TestFixture]
    public class TransactionTransformerTests
    {
        private TransactionTransformer _transactionTransformer;
        private ITransactionMetricNameMaker _transactionMetricNameMaker;
        private ISegmentTreeMaker _segmentTreeMaker;
        private IMetricBuilder _metricBuilder;
        private IMetricNameService _metricNameService;
        private IMetricAggregator _metricAggregator;
        private IConfigurationService _configurationService;
        private IConfiguration _configuration;
        private ITransactionTraceAggregator _transactionTraceAggregator;
        private ITransactionTraceMaker _transactionTraceMaker;
        private ITransactionEventAggregator _transactionEventAggregator;
        private ITransactionEventMaker _transactionEventMaker;
        private ITransactionAttributeMaker _transactionAttributeMaker;
        private IErrorTraceAggregator _errorTraceAggregator;
        private IErrorTraceMaker _errorTraceMaker;
        private IErrorEventAggregator _errorEventAggregator;
        private IErrorEventMaker _errorEventMaker;
        private ISqlTraceAggregator _sqlTraceAggregator;
        private ISqlTraceMaker _sqlTraceMaker;
        private ITransactionSegmentState _transactionSegmentState;

        [SetUp]
        public void SetUp()
        {
            _transactionMetricNameMaker = Mock.Create<ITransactionMetricNameMaker>();
            Mock.Arrange(() => _transactionMetricNameMaker.GetTransactionMetricName(Arg.Matches<ITransactionName>(txName => txName.IsWeb)))
                .Returns(new TransactionMetricName("WebTransaction", "TransactionName"));
            Mock.Arrange(() => _transactionMetricNameMaker.GetTransactionMetricName(Arg.Matches<ITransactionName>(txName => !txName.IsWeb))).Returns(new TransactionMetricName("OtherTransaction", "TransactionName"));

            _transactionSegmentState = Mock.Create<ITransactionSegmentState>();

            Mock.Arrange(() => _transactionSegmentState.GetRelativeTime()).Returns(() => TimeSpan.Zero);

            _segmentTreeMaker = Mock.Create<ISegmentTreeMaker>();
            Mock.Arrange(() => _segmentTreeMaker.BuildSegmentTrees(Arg.IsAny<IEnumerable<Segment>>()))
                .Returns(new[] { BuildNode() });

            _metricBuilder = GetSimpleMetricBuilder();
            _metricAggregator = Mock.Create<IMetricAggregator>();

            _configuration = GetDefaultConfiguration();
            _configurationService = Mock.Create<IConfigurationService>();
            Mock.Arrange(() => _configurationService.Configuration).Returns(_configuration);

            _transactionTraceAggregator = Mock.Create<ITransactionTraceAggregator>();
            _transactionTraceMaker = Mock.Create<ITransactionTraceMaker>();
            _transactionEventAggregator = Mock.Create<ITransactionEventAggregator>();
            _transactionEventMaker = Mock.Create<ITransactionEventMaker>();
            _transactionAttributeMaker = Mock.Create<ITransactionAttributeMaker>();
            _errorTraceAggregator = Mock.Create<IErrorTraceAggregator>();
            _errorTraceMaker = Mock.Create<IErrorTraceMaker>();
            _errorEventAggregator = Mock.Create<IErrorEventAggregator>();
            _errorEventMaker = Mock.Create<IErrorEventMaker>();
            _sqlTraceAggregator = Mock.Create<ISqlTraceAggregator>();
            _sqlTraceMaker = Mock.Create<ISqlTraceMaker>();

            _transactionTransformer = new TransactionTransformer(_transactionMetricNameMaker, _segmentTreeMaker, _metricNameService, _metricAggregator, _configurationService, _transactionTraceAggregator, _transactionTraceMaker, _transactionEventAggregator, _transactionEventMaker, _transactionAttributeMaker, _errorTraceAggregator, _errorTraceMaker, _errorEventAggregator, _errorEventMaker, _sqlTraceAggregator, _sqlTraceMaker);
        }
        public IMetricBuilder GetSimpleMetricBuilder()
        {
            _metricNameService = Mock.Create<IMetricNameService>();
            Mock.Arrange(() => _metricNameService.RenameMetric(Arg.IsAny<string>())).Returns<string>(name => name);
            return new MetricWireModel.MetricBuilder(_metricNameService);
        }

        #region Invalid/ignored transactions

        [Test]
        public void TransformerTransaction_DoesNotGenerateData_IfTransactionIsIgnored()
        {

            var transaction = new Transaction(_configuration, new WebTransactionName("foo", "bar"), Mock.Create<ITimer>(), DateTime.UtcNow, Mock.Create<ICallStackManager>(), SqlObfuscator.GetObfuscatingSqlObfuscator());
            transaction.Ignore();

            _transactionTransformer.Transform(transaction);

            AssertNoDataGenerated();
        }

        [Test]
        public void TransformerTransaction_DoesNotGenerateData_IfTransactionMetricNameIsIgnored()
        {
            Mock.Arrange(() => _transactionMetricNameMaker.GetTransactionMetricName(Arg.IsAny<ITransactionName>()))
                .Returns(new TransactionMetricName("a", "b", true));
            var transaction = TestTransactions.CreateDefaultTransaction();

            _transactionTransformer.Transform(transaction);

            AssertNoDataGenerated();
        }

        [Test]
        public void TransformerTransaction_Throws_IfTransactionHasNoSegments()
        {
            var transaction = TestTransactions.CreateDefaultTransaction(addSegment: false);

            Assert.Throws<ArgumentException>(() => _transactionTransformer.Transform(transaction));

            AssertNoDataGenerated();
        }

        #endregion Invalid/ignored transactions

        #region Metrics

        [Test]
        public void SegmentTransformers_AreGivenAllSegments()
        {
            var node1 = GetNodeBuilder("seg1");
            var node2 = GetNodeBuilder("seg2");
            node1.Children.Add(node2);
            var node3 = GetNodeBuilder("seg3");
            Mock.Arrange(() => _segmentTreeMaker.BuildSegmentTrees(Arg.IsAny<IEnumerable<Segment>>()))
                .Returns(new[] { node1.Build(), node3.Build() });
            TransactionMetricStatsCollection txStats = null;
            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(stats => txStats = stats);

            var transaction = TestTransactions.CreateDefaultTransaction(segments: new List<Segment>() { node1.Segment, node2.Segment, node3.Segment });

            _transactionTransformer.Transform(transaction);

            var scopedStats = txStats.GetScopedForTesting();
            Assert.AreEqual(3, scopedStats.Count);
            var oneStat = scopedStats["DotNet/seg1"];
            Assert.AreEqual(1, oneStat.Value0);
            oneStat = scopedStats["DotNet/seg2"];
            Assert.AreEqual(1, oneStat.Value0);
            oneStat = scopedStats["DotNet/seg3"];
            Assert.AreEqual(1, oneStat.Value0);

            var unscopedStats = txStats.GetUnscopedForTesting();
            Assert.AreEqual(3, scopedStats.Count);
            oneStat = unscopedStats["DotNet/seg1"];
            Assert.AreEqual(1, oneStat.Value0);
            oneStat = unscopedStats["DotNet/seg2"];
            Assert.AreEqual(1, oneStat.Value0);
            oneStat = unscopedStats["DotNet/seg3"];
            Assert.AreEqual(1, oneStat.Value0);
        }

        [Test]
        public void SegmentTransformers_AreGivenCorrectTransactionName()
        {
            var node = BuildNode();
            Mock.Arrange(() => _segmentTreeMaker.BuildSegmentTrees(Arg.IsAny<IEnumerable<Segment>>()))
                .Returns(new[] { node });
            Mock.Arrange(() => _transactionMetricNameMaker.GetTransactionMetricName(Arg.IsAny<ITransactionName>()))
                .Returns(new TransactionMetricName("WebTransaction", "TransactionName"));
            TransactionMetricStatsCollection txStats = null;
            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(stats => txStats = stats);

            var transaction = TestTransactions.CreateDefaultTransaction(segments: new List<Segment>() { node.Segment });

            _transactionTransformer.Transform(transaction);

            Assert.AreEqual("WebTransaction/TransactionName", txStats.GetTransactionName().PrefixedName);
        }

        [Test]
        public void SegmentTransformers_AreGivenCorrectChildDuration()
        {
            var baseTime = DateTime.Now;
            var node1 = GetNodeBuilder("seg1", new TimeSpan(), TimeSpan.FromSeconds(5)); // 0-5
            var node2 = GetNodeBuilder("seg2", new TimeSpan(), TimeSpan.FromSeconds(1)); // 0-1
            var node3 = GetNodeBuilder("seg3", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(999)); // 3-999, but should be capped at 3-5 by parent time
            node1.Segment.ChildFinished(node2.Segment);
            node1.Children.Add(node2);
            node1.Segment.ChildFinished(node3.Segment);
            node1.Children.Add(node3);
            Mock.Arrange(() => _segmentTreeMaker.BuildSegmentTrees(Arg.IsAny<IEnumerable<Segment>>()))
                .Returns(new[] { node1.Build() });

            TransactionMetricStatsCollection txStats = null;
            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(stats => txStats = stats);

            var transaction = TestTransactions.CreateDefaultTransaction(segments: new List<Segment>() { node1.Segment, node2.Segment, node3.Segment });

            _transactionTransformer.Transform(transaction);

            var scopedStats = txStats.GetScopedForTesting();
            Assert.AreEqual(3, scopedStats.Count);
            var oneStat = scopedStats["DotNet/seg1"];
            Assert.AreEqual(1, oneStat.Value0);
            Assert.AreEqual(5, oneStat.Value1);
            Assert.AreEqual(0, oneStat.Value2);
            oneStat = scopedStats["DotNet/seg2"];
            Assert.AreEqual(1, oneStat.Value0);
            Assert.AreEqual(1, oneStat.Value1);
            Assert.AreEqual(1, oneStat.Value2);
            oneStat = scopedStats["DotNet/seg3"];
            Assert.AreEqual(1, oneStat.Value0);
            Assert.AreEqual(999, oneStat.Value1);
            Assert.AreEqual(999, oneStat.Value2);
        }

        [Test]
        public void TransactionRollupMetricIsGeneratedWebTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());

            var transaction = TestTransactions.CreateDefaultTransaction();
            _transactionTransformer.Transform(transaction);

            //Because we mock the segment tree builder, that creates a root node with the name MyMockedRootNode.
            Assert.AreEqual(9, generatedMetrics.Count);
            string[] unscoped = new string[] { "DotNet/MyMockedRootNode", "WebTransaction", "WebTransaction/TransactionName",
                "WebTransactionTotalTime", "WebTransactionTotalTime/TransactionName", "HttpDispatcher",
                "ApdexAll", "Apdex", "Apdex/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current));
                var data = generatedMetrics[current];
                Assert.AreEqual(1, data.Value0);
            }
        }

        [Test]
        public void TransactionRollupMetricIsGeneratedOtherTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());

            var transaction = TestTransactions.CreateDefaultTransaction(false);
            _transactionTransformer.Transform(transaction);

            Assert.AreEqual(5, generatedMetrics.Count);
            string[] unscoped = new string[] { "DotNet/MyMockedRootNode", "OtherTransaction/all", "OtherTransaction/TransactionName",
                "OtherTransactionTotalTime", "OtherTransactionTotalTime/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current), "Failed on " + current);
                var data = generatedMetrics[current];
                Assert.AreEqual(1, data.Value0);
            }
        }

        [Test]
        public void TransactionTotalTimeRollupMetricIsGeneratedOther()
        {
            var node1 = GetNodeBuilder(new TimeSpan(), TimeSpan.FromSeconds(5)); // 0-5
            var node2 = GetNodeBuilder(new TimeSpan(), TimeSpan.FromSeconds(3)); // 0-3
            var node3 = GetNodeBuilder(new TimeSpan(), TimeSpan.FromSeconds(3)); // 0-3
            node1.Children.Add(node2);
            node1.Segment.ChildFinished(node2.Segment);
            node1.Children.Add(node3);
            node1.Segment.ChildFinished(node3.Segment);

            Mock.Arrange(() => _segmentTreeMaker.BuildSegmentTrees(Arg.IsAny<IEnumerable<Segment>>()))
                .Returns(new[] { node1.Build() });

            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());

            var transaction = TestTransactions.CreateDefaultTransaction(isWebTransaction: false, segments: new List<Segment>() { node1.Segment, node2.Segment, node3.Segment });
            _transactionTransformer.Transform(transaction);

            //check the total time metrics
            string[] unscoped = new string[] {
                "OtherTransactionTotalTime", "OtherTransactionTotalTime/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.TryGetValue(current, out MetricDataWireModel data));
                Assert.AreEqual(1, data.Value0);
                Assert.AreEqual(6, data.Value1);
            }

        }

        [Test]
        public void TransactionTotalTimeRollupMetricIsGeneratedWeb()
        {
            var node1 = GetNodeBuilder(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(7)); // 0-8
            var node2 = GetNodeBuilder(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(3)); // 0-3
            var node3 = GetNodeBuilder(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)); // 3-6
            node1.Children.Add(node2);
            node1.Segment.ChildFinished(node2.Segment);
            node1.Children.Add(node3);
            node1.Segment.ChildFinished(node3.Segment);
            Mock.Arrange(() => _segmentTreeMaker.BuildSegmentTrees(Arg.IsAny<IEnumerable<Segment>>()))
                .Returns(new[] { node1.Build() });

            Assert.AreEqual(1, node1.Segment.ExclusiveDurationOrZero.TotalSeconds);
            Assert.AreEqual(3, node2.Segment.ExclusiveDurationOrZero.TotalSeconds);
            Assert.AreEqual(3, node3.Segment.ExclusiveDurationOrZero.TotalSeconds);

            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());

            var transaction = TestTransactions.CreateDefaultTransaction(isWebTransaction: true, segments: new List<Segment>() { node1.Segment, node2.Segment, node3.Segment });
            _transactionTransformer.Transform(transaction);

            Assert.AreEqual(9, generatedMetrics.Count);
            Assert.IsTrue(generatedMetrics.ContainsKey("DotNet/MyOtherMockedRootNode"));
            //check the total time metrics
            string[] unscoped = new string[] {
                "WebTransactionTotalTime", "WebTransactionTotalTime/TransactionName"};

            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.TryGetValue(current, out MetricDataWireModel data));
                Assert.AreEqual(1, data.Value0);
                Assert.AreEqual(7, data.Value1);
            }

        }

        private static Transaction AddDummySegment(Transaction transaction)
        {
            transaction.Add(SimpleSegmentDataTests.createSimpleSegmentBuilder(TimeSpan.Zero, TimeSpan.Zero, 0, null, null, Enumerable.Empty<KeyValuePair<string, object>>(), "", false));
            return transaction;
        }

        [Test]
        public void QueueTimeMetricIsGenerated_IfQueueTimeIsNotNull()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());

            var transaction = new Transaction(_configuration, new WebTransactionName("foo", "bar"), Mock.Create<ITimer>(), DateTime.UtcNow, Mock.Create<ICallStackManager>(), SqlObfuscator.GetObfuscatingSqlObfuscator());
            transaction.TransactionMetadata.SetQueueTime(TimeSpan.FromSeconds(1));
            AddDummySegment(transaction);

            _transactionTransformer.Transform(transaction);

            //check for webfrontend queue time (and a few others). This is not the entire list of unscoped.
            string[] unscoped = new string[] {
                "WebFrontend/QueueTime", "HttpDispatcher", "WebTransaction",
            "WebTransactionTotalTime"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current));
                var data = generatedMetrics[current];
                Assert.AreEqual(1, data.Value0);
            }
        }

        [Test]
        public void ApdexRollupMetricIsGenerated_IfApdexTIsNotNullAndIsNotErrorTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());
            Mock.Arrange(() => _metricNameService.TryGetApdex_t(Arg.IsAny<string>())).Returns(TimeSpan.FromSeconds(1));
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>())).Returns(null as ErrorTraceWireModel);

            var transaction = TestTransactions.CreateDefaultTransaction();
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "ApdexAll", "Apdex", "Apdex/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current));
                var data = generatedMetrics[current];
                //satisfying
                Assert.AreEqual(1, data.Value0);
                // 3 and 4 are total time
                Assert.AreEqual(1, data.Value3);
                Assert.AreEqual(1, data.Value4);
            }
        }

        [Test]
        public void FrustratedApdexRollupMetricIsGenerated_IfApdexTIsNotNullAndIsErrorTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());
            Mock.Arrange(() => _metricNameService.TryGetApdex_t(Arg.IsAny<string>())).Returns(TimeSpan.FromSeconds(1));
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>())).Returns(GetError());

            var transaction = TestTransactions.CreateDefaultTransaction(statusCode: 404);
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "ApdexAll", "Apdex", "Apdex/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current));
                var data = generatedMetrics[current];
                //satisfying
                Assert.AreEqual(0, data.Value0);
                //tolerating
                Assert.AreEqual(0, data.Value1);
                // frustration
                Assert.AreEqual(1, data.Value2);
            }
        }

        [Test]
        public void FrustratedApdexRollupMetricIsNotGenerated_IfApdexTIsNullAndIsErrorTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());
            Mock.Arrange(() => _metricNameService.TryGetApdex_t(Arg.IsAny<string>())).Returns((TimeSpan?)null);
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>())).Returns(GetError());

            var transaction = TestTransactions.CreateDefaultTransaction(false);
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "ApdexAll", "Apdex", "Apdex/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsFalse(generatedMetrics.ContainsKey(current));
            }
        }

        [Test]
        public void ErrorsAllMetricIsGenerated_IfIsErrorTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>()))
                .Returns(GetError());

            var transaction = TestTransactions.CreateDefaultTransaction(statusCode: 404);
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "Errors/all", "Errors/allWeb", "Errors/WebTransaction/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current), "Metric is not contained: " + current);
                Assert.AreEqual(1, generatedMetrics[current].Value0);
            }
        }

        [Test]
        public void ErrorsAllMetricIsGenerated_OtherTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>()))
                .Returns(GetError());

            var transaction = TestTransactions.CreateDefaultTransaction(false, statusCode: 404);
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "Errors/all", "Errors/allOther", "Errors/OtherTransaction/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current), "Metric is not contained: " + current);
                Assert.AreEqual(1, generatedMetrics[current].Value0);
            }
        }

        [Test]
        public void ErrorsAllMetricIsNotGenerated_IfIsNotErrorTransaction()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>()))
               .Returns(null as ErrorTraceWireModel);

            var transaction = TestTransactions.CreateDefaultTransaction(false);
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "Errors/all", "Errors/allOther", "Errors/OtherTransaction/TransactionName"};
            foreach (string current in unscoped)
            {
                Assert.IsFalse(generatedMetrics.ContainsKey(current), "Metric is contained: " + current);
            }
        }

        [Test]
        public void ClientApplicationMetricIsGenerated_IfReferringCrossProcessIdIsNotNull()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());

            var transaction = TestTransactions.CreateDefaultTransaction();
            transaction.TransactionMetadata.SetCrossApplicationReferrerProcessId("123#456");
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "ClientApplication/123#456/all", "HttpDispatcher", "WebTransaction"};
            foreach (string current in unscoped)
            {
                Assert.IsTrue(generatedMetrics.ContainsKey(current), "Metric is not contained: " + current);
                Assert.AreEqual(1, generatedMetrics[current].Value0);
            }
        }

        [Test]
        public void ClientApplicationMetricIsNotGenerated_IfReferringCrossProcessIdIsNull()
        {
            var generatedMetrics = new MetricStatsDictionary<string, MetricDataWireModel>();

            Mock.Arrange(() => _metricAggregator.Collect(Arg.IsAny<TransactionMetricStatsCollection>())).DoInstead<TransactionMetricStatsCollection>(txStats => generatedMetrics = txStats.GetUnscopedForTesting());

            var transaction = TestTransactions.CreateDefaultTransaction(referrerCrossProcessId: null);
            _transactionTransformer.Transform(transaction);

            string[] unscoped = new string[] {
                "ClientApplication/123#456/all"};
            foreach (string current in unscoped)
            {
                Assert.IsFalse(generatedMetrics.ContainsKey(current), "Metric is not contained: " + current);
            }
        }

        #endregion Metrics

        #region Transaction Traces

        [Test]
        public void TransformSendsCorrectParametersToTraceMaker()
        {
            // ARRANGE
            var expectedAttributes = Mock.Create<Attributes>();
            var expectedSegmentTreeNodes = new List<ImmutableSegmentTreeNode> { BuildNode() };
            var expectedTransactionMetricName = new TransactionMetricName("WebTransaction", "TransactionName");

            Mock.Arrange(() => _transactionAttributeMaker.GetAttributes(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<TimeSpan?>(), Arg.IsAny<TimeSpan>(), Arg.IsAny<ErrorData>(), Arg.IsAny<TransactionMetricStatsCollection>()))
                .Returns(expectedAttributes);
            Mock.Arrange(() => _segmentTreeMaker.BuildSegmentTrees(Arg.IsAny<IEnumerable<Segment>>()))
                .Returns(expectedSegmentTreeNodes);
            Mock.Arrange(() => _transactionMetricNameMaker.GetTransactionMetricName(Arg.IsAny<ITransactionName>()))
                .Returns(expectedTransactionMetricName);

            var transaction = new Transaction(_configuration, new OtherTransactionName("transactionCategory", "transactionName"), Mock.Create<ITimer>(), DateTime.UtcNow, Mock.Create<ICallStackManager>(), SqlObfuscator.GetObfuscatingSqlObfuscator());
            AddDummySegment(transaction);

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(_transactionTraceMaker.GetTransactionTrace(transaction.ConvertToImmutableTransaction(), expectedSegmentTreeNodes, expectedTransactionMetricName, expectedAttributes));
        }

        [Test]
        public void TransactionTraceIsSentToAggregator()
        {
            var transaction = TestTransactions.CreateDefaultTransaction();
            _transactionTransformer.Transform(transaction);

            Mock.Assert(() => _transactionTraceAggregator.Collect(Arg.IsAny<TransactionTraceWireModelComponents>()));
        }

        [Test]
        public void TransactionTraceIsNotSentToAggregatorWhenTraceIsDisabled()
        {
            Mock.Arrange(() => _configuration.TransactionTracerEnabled).Returns(false);

            var transaction = TestTransactions.CreateDefaultTransaction();
            _transactionTransformer.Transform(transaction);

            Mock.Assert(() => _transactionTraceAggregator.Collect(Arg.IsAny<TransactionTraceWireModelComponents>()), Occurs.Never());
        }

        #endregion Transaction Traces

        #region Transaction Events

        [Test]
        public void TransformSendsCorrectParametersToEventMaker()
        {
            // ARRANGE
            var expectedAttributes = Mock.Create<Attributes>();
            Mock.Arrange(() => _transactionAttributeMaker.GetAttributes(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<TimeSpan?>(), Arg.IsAny<TimeSpan>(), Arg.IsAny<ErrorData>(), Arg.IsAny<TransactionMetricStatsCollection>()))
                .Returns(expectedAttributes);

            var transaction = new Transaction(_configuration, new OtherTransactionName("transactionCategory", "transactionName"), Mock.Create<ITimer>(), DateTime.UtcNow, Mock.Create<ICallStackManager>(), SqlObfuscator.GetObfuscatingSqlObfuscator());
            AddDummySegment(transaction);

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(_transactionEventMaker.GetTransactionEvent(transaction.ConvertToImmutableTransaction(), expectedAttributes));
        }

        [Test]
        public void TransactionEventIsSentToAggregator()
        {
            // ARRANGE
            var transactionEvent = Mock.Create<TransactionEventWireModel>();
            Mock.Arrange(() => _transactionEventMaker.GetTransactionEvent(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>())).Returns(transactionEvent);
            var transaction = TestTransactions.CreateDefaultTransaction();

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(() => _transactionEventAggregator.Collect(transactionEvent));
        }

        [Test]
        public void TransactionEvent_IsNotCreatedOrSentToAggregator_IfTransactionEventsEnabledIsFalse()
        {
            // ARRANGE
            Mock.Arrange(() => _configuration.TransactionEventsEnabled).Returns(false);
            var transaction = TestTransactions.CreateDefaultTransaction();

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(() => _transactionEventAggregator.Collect(Arg.IsAny<TransactionEventWireModel>()), Occurs.Never());
            Mock.Assert(() => _transactionEventMaker.GetTransactionEvent(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>()), Occurs.Never());
        }

        [Test]
        public void TransactionEvent_IsNotCreatedOrSentToAggregator_IfTransactionEventsTransactionsEnabledIsFalse()
        {
            // ARRANGE
            Mock.Arrange(() => _configuration.TransactionEventsTransactionsEnabled).Returns(false);
            var transaction = TestTransactions.CreateDefaultTransaction();

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(() => _transactionEventAggregator.Collect(Arg.IsAny<TransactionEventWireModel>()), Occurs.Never());
            Mock.Assert(() => _transactionEventMaker.GetTransactionEvent(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>()), Occurs.Never());
        }

        #endregion Transaction Events

        #region Error Traces

        [Test]
        public void TransformSendsCorrectParametersToErrorTraceMaker()
        {
            // ARRANGE
            var expectedAttributes = Mock.Create<Attributes>();
            var expectedTransactionMetricName = new TransactionMetricName("WebTransaction", "TransactionName");

            Mock.Arrange(() => _transactionAttributeMaker.GetAttributes(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<TimeSpan?>(), Arg.IsAny<TimeSpan>(), Arg.IsAny<ErrorData>(), Arg.IsAny<TransactionMetricStatsCollection>()))
                .Returns(expectedAttributes);
            Mock.Arrange(() => _transactionMetricNameMaker.GetTransactionMetricName(Arg.IsAny<ITransactionName>()))
                .Returns(expectedTransactionMetricName);
            var transaction = TestTransactions.CreateDefaultTransaction(statusCode: 404, transactionCategory: "transactionCategory", transactionName: "transactionName");

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), expectedAttributes, expectedTransactionMetricName, Arg.IsAny<ErrorData>()));
        }

        [Test]
        public void ErrorTraceIsSentToAggregator()
        {
            var errorTraceWireModel = Mock.Create<ErrorTraceWireModel>();
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>()))
                .Returns(errorTraceWireModel);
            var transaction = TestTransactions.CreateDefaultTransaction(uri: "http://www.newrelic.com/test?param=value", statusCode: 404);

            var privateTransactionTransformer = new PrivateAccessor(_transactionTransformer);
            var args = new object[] { transaction };
            privateTransactionTransformer.CallMethod("Transform", args);

            Mock.Assert(() => _errorTraceAggregator.Collect(errorTraceWireModel));
        }

        [Test]
        public void ErrorTraceIsNotSentToAggregatorWhenErrorTraceIsNull()
        {
            Mock.Arrange(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>()))
                .Returns(null as ErrorTraceWireModel);
            var transaction = TestTransactions.CreateDefaultTransaction();
            _transactionTransformer.Transform(transaction);
            Mock.Assert(() => _errorTraceAggregator.Collect(Arg.IsAny<ErrorTraceWireModel>()), Occurs.Never());
        }

        [Test]
        public void ErrorTrace_IsNotCreatedOrSentToAggregator_IfErrorCollectorEnabledIsFalse()
        {
            // ARRANGE
            Mock.Arrange(() => _configuration.ErrorCollectorEnabled).Returns(false);

            var transaction = new Transaction(_configuration, new OtherTransactionName("transactionCategory", "transactionName"), Mock.Create<ITimer>(), DateTime.UtcNow, Mock.Create<ICallStackManager>(), SqlObfuscator.GetObfuscatingSqlObfuscator());
            AddDummySegment(transaction);

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(() => _errorTraceMaker.GetErrorTrace(Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>(), Arg.IsAny<TransactionMetricName>(), Arg.IsAny<ErrorData>()), Occurs.Never());
            Mock.Assert(() => _errorTraceAggregator.Collect(Arg.IsAny<ErrorTraceWireModel>()), Occurs.Never());
        }

        #endregion Error Traces

        #region Error Events

        [Test]
        public void ErrorEventIsSentToAggregator()
        {
            // ARRANGE
            var errorEvent = Mock.Create<ErrorEventWireModel>();
            Mock.Arrange(() => _errorEventMaker.GetErrorEvent(Arg.IsAny<ErrorData>(), Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>())).Returns(errorEvent);
            var transaction = TestTransactions.CreateDefaultTransaction(uri: "http://www.newrelic.com/test?param=value", statusCode: 404);

            // ACT
            var privateTransactionTransformer = new PrivateAccessor(_transactionTransformer);
            var args = new object[] { transaction };
            privateTransactionTransformer.CallMethod("Transform", args);

            // ASSERT
            Mock.Assert(() => _errorEventAggregator.Collect(errorEvent));
        }

        [Test]
        public void ErrorEvent_IsNotCreatedOrSentToAggregator_IfErrorCollectorEnabledIsFalse()
        {
            // ARRANGE
            Mock.Arrange(() => _configuration.ErrorCollectorEnabled).Returns(false);
            var transaction = TestTransactions.CreateDefaultTransaction();

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(() => _errorEventAggregator.Collect(Arg.IsAny<ErrorEventWireModel>()), Occurs.Never());
            Mock.Assert(() => _errorEventMaker.GetErrorEvent(Arg.IsAny<ErrorData>(), Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>()), Occurs.Never());
        }

        [Test]
        public void ErrorEvent_IsNotCreatedOrSentToAggregator_IfErrorCollectorCaptureEventsIsFalse()
        {
            // ARRANGE
            Mock.Arrange(() => _configuration.ErrorCollectorCaptureEvents).Returns(false);
            var transaction = TestTransactions.CreateDefaultTransaction();

            // ACT
            _transactionTransformer.Transform(transaction);

            // ASSERT
            Mock.Assert(() => _errorEventAggregator.Collect(Arg.IsAny<ErrorEventWireModel>()), Occurs.Never());
            Mock.Assert(() => _errorEventMaker.GetErrorEvent(Arg.IsAny<ErrorData>(), Arg.IsAny<ImmutableTransaction>(), Arg.IsAny<Attributes>()), Occurs.Never());
        }

        #endregion Error Events

        #region Helpers

        private void AssertNoDataGenerated()
        {
            NrAssert.Multiple(
                () => Mock.Assert(() => _metricAggregator.Collect(Arg.IsAny<MetricWireModel>()), Occurs.Never()),
                () => Mock.Assert(() => _transactionTraceAggregator.Collect(Arg.IsAny<TransactionTraceWireModelComponents>()), Occurs.Never()),
                () => Mock.Assert(() => _transactionEventAggregator.Collect(Arg.IsAny<TransactionEventWireModel>()), Occurs.Never()),
                () => Mock.Assert(() => _errorTraceAggregator.Collect(Arg.IsAny<ErrorTraceWireModel>()), Occurs.Never()),
                () => Mock.Assert(() => _errorEventAggregator.Collect(Arg.IsAny<ErrorEventWireModel>()), Occurs.Never()),
                () => Mock.Assert(() => _sqlTraceAggregator.Collect(Arg.IsAny<SqlTraceStatsCollection>()), Occurs.Never())
            );
        }
        private ImmutableSegmentTreeNode BuildNode(TimeSpan startTime = new TimeSpan(), TimeSpan duration = new TimeSpan())
        {
            return new SegmentTreeNodeBuilder(
                GetSegment("MyMockedRootNode", duration.TotalSeconds, startTime)).
                Build();
        }
        private SegmentTreeNodeBuilder GetNodeBuilder(TimeSpan startTime = new TimeSpan(), TimeSpan duration = new TimeSpan())
        {
            return new SegmentTreeNodeBuilder(
                GetSegment("MyOtherMockedRootNode", duration.TotalSeconds, startTime));
        }
        private SegmentTreeNodeBuilder GetNodeBuilder(string name, TimeSpan startTime = new TimeSpan(), TimeSpan duration = new TimeSpan())
        {
            return new SegmentTreeNodeBuilder(
                GetSegment(name, duration.TotalSeconds, startTime));
        }
        private Segment GetSegment(string name)
        {
            var builder = new TypedSegment<SimpleSegmentData>(_transactionSegmentState, new MethodCallData("foo", "bar", 1), new SimpleSegmentData(name));
            builder.End();
            return builder;
        }

        public TypedSegment<SimpleSegmentData> GetSegment(string name, double duration, TimeSpan start = new TimeSpan())
        {
            var methodCallData = new MethodCallData("foo", "bar", 1);
            return new TypedSegment<SimpleSegmentData>(start, TimeSpan.FromSeconds(duration), GetSegment(name));
        }
        public static IConfiguration GetDefaultConfiguration()
        {
            return TestTransactions.GetDefaultConfiguration();
        }
        private static ErrorTraceWireModel GetError()
        {
            var attributes = new List<KeyValuePair<string, object>>();
            var stackTrace = new List<string>();
            var errorTraceAttributes = new ErrorTraceWireModel.ErrorTraceAttributesWireModel("requestUri", attributes, attributes, attributes, stackTrace);
            return new ErrorTraceWireModel(DateTime.Now, "path", "message", "exceptionClassName", errorTraceAttributes, "guid");
        }

        #endregion Helpers
    }
}
