/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
namespace NewRelic.Agent.Core.Exceptions
{
    public class SerializationException : RPMException
    {
        public SerializationException(string message)
            : base(message)
        {
        }
    }
}
