﻿/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System;

namespace NewRelic.Agent
{
    [Flags]
    public enum AttributeDestinations
    {
        None = 0,
        TransactionTrace = 1 << 0,
        TransactionEvent = 1 << 1,
        ErrorTrace = 1 << 2,
        JavaScriptAgent = 1 << 3,
        ErrorEvent = 1 << 4,
        All = 0xFF,
    }
}
