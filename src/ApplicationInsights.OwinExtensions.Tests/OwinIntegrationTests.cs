﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ApplicationInsights.OwinExtensions.Tests.Utils;
using FluentAssertions;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.Owin.Hosting;
using Owin;
using Xunit;

namespace ApplicationInsights.OwinExtensions.Tests
{
    using AppFunc = Func<IDictionary<string, object>, Task>;

    [Collection("Integration tests")]
    public class OwinIntegrationTests : IDisposable
    {
        public class Startup
        {
            public static MockTelemetryChannel Channel;
            public static DependencyTrackingTelemetryModule DependencyTrackingTelemetryModule;
            public static string ActualOperationId;
            public static AutoResetEvent RequestCompleted = new AutoResetEvent(false);

            public void Configuration(IAppBuilder app)
            {
                SignalOnRequestCompletion(app);

                ConfigureApplicationInsights(app);

                ServeRequest(app);
            }

            private static void ConfigureApplicationInsights(IAppBuilder app)
            {
                Channel = new MockTelemetryChannel();

                var config = new TelemetryConfigurationBuilder()
                    .WithChannel(Channel)
                    .WithTelemetryInitializer(new OperationIdTelemetryInitializer())
                    .Build();

                DependencyTrackingTelemetryModule = new DependencyTrackingTelemetryModule();
                DependencyTrackingTelemetryModule.Initialize(config);
                app.UseApplicationInsights(config);
            }

            private static void SignalOnRequestCompletion(IAppBuilder app)
            {
                app.Use(new Func<AppFunc, AppFunc>(next => (async env =>
                {
                    await next.Invoke(env);
                    RequestCompleted.Set();
                })));
            }

            private static void ServeRequest(IAppBuilder app)
            {
                app.Run(async context =>
                {
                    ActualOperationId = OperationIdContext.Get();

                    using (var client = new HttpClient())
                        await client.GetAsync("http://google.com");

                    context.Response.StatusCode = 200;
                    context.Response.Write("ok");
                });
            }
        }

        [Fact]
        public async Task Self_Hosting_Should_Work()
        {
            using (WebApp.Start<Startup>("http://localhost:7690"))
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync("http://localhost:7690");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be("ok");
            }
        }

        [Fact]
        public async Task Can_Send_Request_Telemetry()
        {
            using (WebApp.Start<Startup>("http://localhost:7690"))
            using (var client = new HttpClient())
                await client.GetAsync("http://localhost:7690");

            Startup.RequestCompleted.WaitOne(1000);

            var telemetry = Startup.Channel.SentTelemetries.FirstOrDefault(t => t is RequestTelemetry);
            Assert.True(telemetry != null, "Request telemetry was not sent");
            telemetry.Context.Operation.Id.Should().Be(Startup.ActualOperationId);
        }

        [Fact]
        public async Task Can_Send_Dependency_Telemetry()
        {
            using (WebApp.Start<Startup>("http://localhost:7690"))
            using (var client = new HttpClient())
                await client.GetAsync("http://localhost:7690");

            Startup.RequestCompleted.WaitOne(1000);

            var telemetry = Startup.Channel.SentTelemetries.FirstOrDefault(t => t is DependencyTelemetry);
            Assert.True(telemetry != null, "Dependency telemetry was not sent");
            telemetry.Context.Operation.Id.Should().Be(Startup.ActualOperationId);
        }

        public void Dispose()
        {
            if (Startup.DependencyTrackingTelemetryModule != null)
                Startup.DependencyTrackingTelemetryModule.Dispose();
        }
    }
}
