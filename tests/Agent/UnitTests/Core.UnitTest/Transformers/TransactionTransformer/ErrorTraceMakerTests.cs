// Copyright 2020 New Relic, Inc. All rights reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using NewRelic.Agent.Configuration;
using NewRelic.Agent.Core.Attributes;
using NewRelic.Agent.Core.Errors;
using NewRelic.Agent.Core.Segments;
using NewRelic.Agent.Core.Transactions;
using NewRelic.Agent.Core.Utilities;
using NewRelic.Testing.Assertions;
using NUnit.Framework;
using Telerik.JustMock;

namespace NewRelic.Agent.Core.Transformers.TransactionTransformer.UnitTest
{
    [TestFixture]
    public class ErrorTraceMakerTests
    {
        private IConfiguration _configuration;
        private IConfigurationService _configurationService;
        private ErrorTraceMaker _errorTraceMaker;
        private ErrorService _errorService;
        private AttributeDefinitionService _attribDefSvc;
        private IAttributeDefinitions _attribDefs => _attribDefSvc.AttributeDefs;
        private IAgentTimerService _agentTimerService;
        private Func<IReadOnlyDictionary<string, object>, string> _errorGroupCallback;
        private const string _expectedErrorGroupAttributeName = "error.group.name";
        private OutOfMemoryException _exception;
        private const string StripExceptionMessagesMessage = "Message removed by New Relic based on your currently enabled security settings.";
        private const string ErrorDataCustomAttributeKey = "myAttribute";

        [SetUp]
        public void SetUp()
        {
            _errorGroupCallback = null;
            _configuration = Mock.Create<IConfiguration>();
            Mock.Arrange(() => _configuration.ErrorGroupCallback).Returns(() => _errorGroupCallback);
            Mock.Arrange(() => _configuration.StackTraceMaximumFrames).Returns(() => 1);

            _configurationService = Mock.Create<IConfigurationService>();
            Mock.Arrange(() => _configurationService.Configuration).Returns(_configuration);

            _attribDefSvc = new AttributeDefinitionService((f) => new AttributeDefinitions(f));
            _agentTimerService = Mock.Create<IAgentTimerService>();
            _errorTraceMaker = new ErrorTraceMaker(_configurationService, _attribDefSvc, _agentTimerService);
            _errorService = new ErrorService(_configurationService);

            _exception = new OutOfMemoryException("Out of Memory Message");
        }

        private ErrorData GetErrorDataFromException(object value)
        {
            Dictionary<string, object> customAttributes = null;
            if (value != null)
            {
                customAttributes = new Dictionary<string, object> { { ErrorDataCustomAttributeKey, value } };
            }

            return _errorService.FromException(_exception, customAttributes);
        }

        private ErrorData GetErrorDataFromMessage(object value)
        {
            Dictionary<string, object> customAttributes = null;
            if (value != null)
            {
                customAttributes = new Dictionary<string, object> { { ErrorDataCustomAttributeKey, value } };
            }

            return _errorService.FromMessage("Out of Memory Message", customAttributes, false);
        }

        [Test]
        public void GetErrorTrace_ReturnsErrorTrace_IfStatusCodeIs404()
        {
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value");
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");

            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);

            Assert.NotNull(errorTrace);
            NrAssert.Multiple(
                () => Assert.AreEqual("WebTransaction/Name", errorTrace.Path),
                () => Assert.AreEqual("Not Found", errorTrace.Message),
                () => Assert.AreEqual("404", errorTrace.ExceptionClassName),
                () => Assert.AreEqual(transaction.Guid, errorTrace.Guid),
                () => Assert.AreEqual(null, errorTrace.Attributes.StackTrace)
                );
        }

        [Test]
        public void GetErrorTrace_ReturnsErrorTrace_IfExceptionIsNoticed()
        {
            var errorData = GetErrorDataFromMessage(null);
            var transaction = BuildTestTransaction(uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");

            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);

            Assert.NotNull(errorTrace);
            NrAssert.Multiple(
                () => Assert.AreEqual("WebTransaction/Name", errorTrace.Path),
                () => Assert.AreEqual("Out of Memory Message", errorTrace.Message),
                () => Assert.AreEqual("Custom Error", errorTrace.ExceptionClassName),
                () => Assert.AreEqual(transaction.Guid, errorTrace.Guid)
            );
        }

        [Test]
        public void GetErrorTrace_ReturnsFirstException_IfMultipleExceptionsNoticed()
        {
            var errorData = GetErrorDataFromMessage(null);
            var errorData2 = _errorService.FromMessage("My message2", (Dictionary<string, object>)null, false);
            var transaction = BuildTestTransaction(uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData, errorData2 });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");

            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);

            Assert.NotNull(errorTrace);
            NrAssert.Multiple(
                () => Assert.AreEqual("WebTransaction/Name", errorTrace.Path),
                () => Assert.AreEqual("Out of Memory Message", errorTrace.Message),
                () => Assert.AreEqual("Custom Error", errorTrace.ExceptionClassName),
                () => Assert.AreEqual(transaction.Guid, errorTrace.Guid)
            );
        }

        [Test]
        public void GetErrorTrace_ReturnsExceptionsBeforeStatusCodes()
        {
            var errorData = GetErrorDataFromMessage(null);
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");

            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);

            Assert.NotNull(errorTrace);
            NrAssert.Multiple(
                () => Assert.AreEqual("WebTransaction/Name", errorTrace.Path),
                () => Assert.AreEqual("Out of Memory Message", errorTrace.Message),
                () => Assert.AreEqual("Custom Error", errorTrace.ExceptionClassName),
                () => Assert.AreEqual(transaction.Guid, errorTrace.Guid)
            );
        }

        [Test]
        public void GetErrorTrace_ReturnsExceptionWithoutMessage_IfStripExceptionMessageEnabled()
        {
            Mock.Arrange(() => _configurationService.Configuration.StripExceptionMessages).Returns(true);
            var errorData = GetErrorDataFromMessage(null);
            var transaction = BuildTestTransaction(uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");

            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);

            Assert.NotNull(errorTrace);
            NrAssert.Multiple(
                () => Assert.AreEqual("WebTransaction/Name", errorTrace.Path),
                () => Assert.AreEqual(StripExceptionMessagesMessage, errorTrace.Message),
                () => Assert.AreEqual("Custom Error", errorTrace.ExceptionClassName),
                () => Assert.AreEqual(transaction.Guid, errorTrace.Guid)
            );
        }


        #region ErrorGroup FromMessage

        [TestCase("value")]
        [TestCase(null)]
        public void GetErrorTrace_InTransaction_FromMessage_HasErrorGroup(object value)
        {
            _errorGroupCallback = ex => "test group";
            var errorData = GetErrorDataFromMessage(value);
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");
            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;
            var errorGroupAttribute = agentAttributes[_expectedErrorGroupAttributeName];

            Assert.AreEqual("test group", errorGroupAttribute);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("    ")]
        public void GetErrorTrace_InTransaction_FromMessage_DoesNotHaveErrorGroup(string callbackReturnValue)
        {
            _errorGroupCallback = ex => callbackReturnValue;
            var errorData = GetErrorDataFromMessage(null);
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");
            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, _expectedErrorGroupAttributeName);
        }

        [Test]
        public void GetErrorTrace_InTransaction_FromMessage_DoesNotHaveErrorGroup()
        {
            var errorData = GetErrorDataFromMessage(null);
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");
            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, "error_group");
        }

        [TestCase("value")]
        [TestCase(null)]
        public void GetErrorTrace_NoTransaction_FromMessage_HasErrorGroup(object value)
        {
            _errorGroupCallback = ex => "test group";
            var errorData = GetErrorDataFromMessage(value);
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var errorTrace = _errorTraceMaker.GetErrorTrace(attributes, errorData);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;
            var errorGroupAttribute = agentAttributes[_expectedErrorGroupAttributeName];

            Assert.AreEqual("test group", errorGroupAttribute);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("    ")]
        public void GetErrorTrace_NoTransaction_FromMessage_DoesNotHaveErrorGroup(string callbackReturnValue)
        {
            _errorGroupCallback = ex => callbackReturnValue;
            var errorData = GetErrorDataFromMessage(null);
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var errorTrace = _errorTraceMaker.GetErrorTrace(attributes, errorData);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, _expectedErrorGroupAttributeName);
        }

        [Test]
        public void GetErrorTrace_NoTransaction_FromMessage_DoesNotHaveErrorGroup()
        {
            var errorData = GetErrorDataFromMessage(null);
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var errorTrace = _errorTraceMaker.GetErrorTrace(attributes, errorData);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, "error_group");
        }

        #endregion

        #region ErrorGroup FromException

        [TestCase("value")]
        [TestCase(null)]
        public void GetErrorTrace_InTransaction_FromException_HasErrorGroup(object value)
        {
            IReadOnlyDictionary<string, object> passedInDict = null;
            _errorGroupCallback = ex =>
            {
                passedInDict = ex;
                return "test group";
            };
            var errorData = GetErrorDataFromException(value);
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");
            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;
            var errorGroupAttribute = agentAttributes[_expectedErrorGroupAttributeName];

            Assert.IsNotNull(passedInDict);
            Assert.IsTrue(passedInDict.ContainsKey("stack_trace"));
            Assert.IsTrue(passedInDict.ContainsKey("exception"));
            Assert.AreEqual("test group", errorGroupAttribute);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("    ")]
        public void GetErrorTrace_InTransaction_FromException_DoesNotHaveErrorGroup(string callbackReturnValue)
        {
            _errorGroupCallback = ex => callbackReturnValue;
            var errorData = GetErrorDataFromException(null);
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");
            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, _expectedErrorGroupAttributeName);
        }

        [Test]
        public void GetErrorTrace_InTransaction_FromException_DoesNotHaveErrorGroup()
        {
            var errorData = GetErrorDataFromException(null);
            var transaction = BuildTestTransaction(statusCode: 404, uri: "http://www.newrelic.com/test?param=value", transactionExceptionDatas: new[] { errorData });
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var transactionMetricName = new TransactionMetricName("WebTransaction", "Name");
            var errorTrace = _errorTraceMaker.GetErrorTrace(transaction, attributes, transactionMetricName);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, "error_group");
        }

        [TestCase("value")]
        [TestCase(null)]
        public void GetErrorTrace_NoTransaction_FromException_HasErrorGroup(object value)
        {
            IReadOnlyDictionary<string, object> passedInDict = null;
            _errorGroupCallback = ex =>
            {
                passedInDict = ex;
                return "test group";
            };
            var errorData = GetErrorDataFromException(value);
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var errorTrace = _errorTraceMaker.GetErrorTrace(attributes, errorData);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;
            var errorGroupAttribute = agentAttributes[_expectedErrorGroupAttributeName];

            Assert.IsNotNull(passedInDict);
            Assert.IsTrue(passedInDict.ContainsKey("stack_trace"));
            Assert.IsTrue(passedInDict.ContainsKey("exception"));
            Assert.AreEqual("test group", errorGroupAttribute);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("    ")]
        public void GetErrorTrace_NoTransaction_FromException_DoesNotHaveErrorGroup(string callbackReturnValue)
        {
            _errorGroupCallback = ex => callbackReturnValue;
            var errorData = GetErrorDataFromException(null);
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var errorTrace = _errorTraceMaker.GetErrorTrace(attributes, errorData);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, _expectedErrorGroupAttributeName);
        }

        [Test]
        public void GetErrorTrace_NoTransaction_FromException_DoesNotHaveErrorGroup()
        {
            var errorData = GetErrorDataFromException(null);
            var attributes = new AttributeValueCollection(AttributeDestinations.ErrorTrace);
            var errorTrace = _errorTraceMaker.GetErrorTrace(attributes, errorData);
            var agentAttributes = errorTrace.Attributes.AgentAttributes;

            CollectionAssert.DoesNotContain(agentAttributes.Keys, "error_group");
        }

        #endregion

        private ImmutableTransaction BuildTestTransaction(string uri = null, string guid = null, int? statusCode = null, int? subStatusCode = null, IEnumerable<ErrorData> transactionExceptionDatas = null)
        {
            var transactionMetadata = new TransactionMetadata(guid);
            if (uri != null)
                transactionMetadata.SetUri(uri);
            if (statusCode != null)
                transactionMetadata.SetHttpResponseStatusCode(statusCode.Value, subStatusCode, _errorService);
            if (transactionExceptionDatas != null)
                transactionExceptionDatas.ForEach(data => transactionMetadata.TransactionErrorState.AddExceptionData(data));

            var name = TransactionName.ForWebTransaction("foo", "bar");
            var segments = Enumerable.Empty<Segment>();
            var metadata = transactionMetadata.ConvertToImmutableMetadata();
            guid = guid ?? Guid.NewGuid().ToString();

            var attribDefSvc = new AttributeDefinitionService((f) => new AttributeDefinitions(f));

            return new ImmutableTransaction(name, segments, metadata, DateTime.UtcNow, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), guid, false, false, false, 0.5f, false, string.Empty, null, attribDefSvc.AttributeDefs);
        }
    }
}
