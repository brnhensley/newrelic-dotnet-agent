﻿/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System;
using System.Collections.Generic;
using NewRelic.Agent.Core.JsonConverters;
using NewRelic.SystemExtensions.Collections.Generic;
using Newtonsoft.Json;

namespace NewRelic.Agent.Core.WireModels
{
    [JsonConverter(typeof(JsonArrayConverter))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TransactionEventWireModel
    {
        [JsonArrayIndex(Index = 0)]
        public readonly ReadOnlyDictionary<string, object> IntrinsicAttributes;

        [JsonArrayIndex(Index = 1)]
        public readonly ReadOnlyDictionary<string, object> UserAttributes;

        [JsonArrayIndex(Index = 2)]
        public readonly ReadOnlyDictionary<string, object> AgentAttributes;

        private readonly bool _isSynthetics;

        public TransactionEventWireModel(IEnumerable<KeyValuePair<string, object>> userAttributes, IEnumerable<KeyValuePair<string, object>> agentAttributes, IEnumerable<KeyValuePair<string, object>> intrinsicAttributes, bool isSynthetics)
        {
            IntrinsicAttributes = new ReadOnlyDictionary<string, object>(intrinsicAttributes.ToDictionary<String, Object>());
            UserAttributes = new ReadOnlyDictionary<string, object>(userAttributes.ToDictionary<String, Object>());
            AgentAttributes = new ReadOnlyDictionary<string, object>(agentAttributes.ToDictionary<String, Object>());
            _isSynthetics = isSynthetics;
        }

        public bool IsSynthetics()
        {
            // An event will always contain either all of the synthetics keys or none of them.
            // There is no need to check for the presence of each synthetics key.
            return _isSynthetics;
        }
    }
}
