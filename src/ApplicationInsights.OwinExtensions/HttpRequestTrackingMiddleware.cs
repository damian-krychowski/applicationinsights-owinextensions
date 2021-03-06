﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Owin;

namespace ApplicationInsights.OwinExtensions
{
    public class HttpRequestTrackingMiddleware : OwinMiddleware
    {
        private readonly TelemetryClient _client;

        public HttpRequestTrackingMiddleware(OwinMiddleware next, TelemetryConfiguration configuration = null) : base(next)
        {
            _client = configuration != null ? new TelemetryClient(configuration) : new TelemetryClient();
        }

        public override async Task Invoke(IOwinContext context)
        {
            var method = context.Request.Method;
            var path = context.Request.Path.ToString();
            var uri = context.Request.Uri;

            var requestStartDate = DateTimeOffset.Now;
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                await Next.Invoke(context);
            }
            finally
            {
                stopWatch.Stop();

                TraceRequest(method, path, uri, context.Response.StatusCode, requestStartDate, stopWatch.Elapsed);
            }
        }

        private void TraceRequest(string method, string path, Uri uri, int responseCode, DateTimeOffset requestStartDate, TimeSpan duration)
        {
            var name = $"{method} {path}";

            var telemetry = new RequestTelemetry(
                name,
                requestStartDate,
                duration,
                responseCode.ToString(),
                responseCode < 400)
            {
                Id = OperationIdContext.Get(),
                HttpMethod = method,
                Url = uri
            };

            telemetry.Context.Operation.Name = name;

            _client.TrackRequest(telemetry);
        }
    }

}
