﻿// Copyright 2020 New Relic, Inc. All rights reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Nest;
using NewRelic.Agent.IntegrationTests.Shared;
using NewRelic.IntegrationTests.Models;

namespace MultiFunctionApplicationHelpers.NetStandardLibraries.Elasticsearch
{
    internal class ElasticsearchNestClient : ElasticsearchTestClient
    {
        private ElasticClient _client;

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override void Connect()
        {
            var settings = new ConnectionSettings(Address).
                BasicAuthentication(ElasticSearchConfiguration.ElasticUserName,
                ElasticSearchConfiguration.ElasticPassword).
                DefaultIndex(IndexName);

            _client = new ElasticClient(settings);

            // TODO: This isn't necessary but will log the response, which can help troubleshoot if
            // you're having connection errors
            _client.Ping();
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override void Index()
        {
            var record = FlightRecord.GetSample();
            var response = _client.IndexDocument(record);

            // TODO: Validate that it worked
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override async Task<bool> IndexAsync()
        {
            var record = FlightRecord.GetSample();
            var response = await _client.IndexDocumentAsync(record);

            // TODO: Validate that it worked

            return response.IsValid;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override void IndexMany()
        {
            var records = FlightRecord.GetSamples(3);
            var response = _client.IndexMany(records);

            // TODO: Validate that it worked
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override async Task<bool> IndexManyAsync()
        {
            var record = FlightRecord.GetSample();
            var response = await _client.IndexDocumentAsync(record);

            // TODO: Validate that it worked

            return response.IsValid;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override void Search()
        {
            var searchResponse = _client.Search<FlightRecord>(s => s
                .From(0)
                .Size(10)
                .Query(q => q
                    .Match(m => m
                    .Field(f => f.Departure)
                    .Query(FlightRecord.GetSample().Departure)
                    )
                   )
                );

            // TODO: Validate that it worked
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override async Task<long> SearchAsync()
        {
            var response = await _client.SearchAsync<FlightRecord>(s => s
                .From(0)
                .Size(10)
                .Query(q => q
                    .Match(m => m
                    .Field(f => f.Departure)
                    .Query(FlightRecord.GetSample().Departure)
                    )
                   )
                );

            // TODO: Validate that it worked

            return response.Total;
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override void MultiSearch()
        {
            var msd = new MultiSearchDescriptor();

            var records = FlightRecord.GetSamples(2);
            foreach (var record in records)
            {
                msd.Search<FlightRecord>(s => s
                    .From(0)
                    .Size(10)
                    .Query(q => q
                        .Match(m => m
                        .Field(f => f.Departure)
                        .Query(record.Departure)
                        )
                       ));
            }

            var response = _client.MultiSearch(msd);
        }

        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public override async Task<long> MultiSearchAsync()
        {
            var msd = new MultiSearchDescriptor();

            var records = FlightRecord.GetSamples(2);
            foreach (var record in records)
            {
                msd.Search<FlightRecord>(s => s
                    .From(0)
                    .Size(10)
                    .Query(q => q
                        .Match(m => m
                        .Field(f => f.Departure)
                        .Query(record.Departure)
                        )
                       ));
            }

            var response = await _client.MultiSearchAsync(msd);

            // TODO: Validate that it worked

            return response.TotalResponses;
        }
    }
}
