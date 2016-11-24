using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class HubEndPointTests
    {
        [Fact]
        public async Task ConnectAndDisconnect()
        {
            var s = new ServiceCollection();
            s.AddOptions()
                .AddLogging()
                .AddSignalR();

            s.AddSingleton(typeof(HubLifetimeManager<>), typeof(TestHubLifetimeManager<>));

            var serviceProvider = s.BuildServiceProvider();
            var endPoint = serviceProvider.GetService<HubEndPoint<TestHub>>();

            using (var factory = new PipelineFactory())
            using (var channel = new HttpConnection(factory))
            using (var connectionManager = new ConnectionManager())
            {
                var connection = connectionManager.AddNewConnection(channel).Connection;
                connection.Metadata["formatType"] = "json";

                var endPointTask = Task.Run(() => endPoint.OnConnectedAsync(connection));

                Thread.Sleep(100);

                // kill the connection
                connection.Channel.Dispose();

                await endPointTask;

                Assert.Equal(1, TestHubLifetimeManager<TestHub>.OnConnected);
                Assert.Equal(1, TestHubLifetimeManager<TestHub>.OnDisconnected);
            }
        }

        [Fact]
        public async Task Race()
        {
            var s = new ServiceCollection();
            s.AddOptions()
                .AddLogging()
                .AddSignalR();

            s.AddSingleton(typeof(HubLifetimeManager<>), typeof(TestHubLifetimeManager<>));

            var serviceProvider = s.BuildServiceProvider();
            var endPoint = serviceProvider.GetService<HubEndPoint<TestHub>>();

            using (var factory = new PipelineFactory())
            using (var channel = new HttpConnection(factory))
            using (var connectionManager = new ConnectionManager())
            {
                var connection = connectionManager.AddNewConnection(channel).Connection;
                connection.Metadata["formatType"] = "json";

                var endPointTask = Task.Run(() => endPoint.OnConnectedAsync(connection));

                Thread.Sleep(100);

                var stream = connection.Channel.GetStream();
                var r = serviceProvider.GetService<InvocationAdapterRegistry>();
                var writer = r.GetInvocationAdapter("json");
                var tasks = new List<Task>(3);

                tasks.Add(Task.Run(() => writer.WriteInvocationDescriptorAsync(new InvocationDescriptor
                {
                    Arguments = new[] { "test" },
                    Method = typeof(TestHub).FullName + ".Send"
                }, stream)));
                //tasks.Add(Task.Run(() => writer.WriteInvocationDescriptorAsync(new InvocationDescriptor
                //{
                //    Arguments = new[] { "test" },
                //    Method = typeof(TestHub).FullName + ".Send"
                //}, stream)));
                //tasks.Add(Task.Run(() => writer.WriteInvocationDescriptorAsync(new InvocationDescriptor
                //{
                //    Arguments = new[] { "test" },
                //    Method = typeof(TestHub).FullName + ".Send"
                //}, stream)));

                await Task.WhenAll(tasks);
                Thread.Sleep(10000);

                // kill the connection
                connection.Channel.Dispose();

                await endPointTask;

                Assert.Equal(1, TestHubLifetimeManager<TestHub>.OnConnected);
                Assert.Equal(1, TestHubLifetimeManager<TestHub>.OnDisconnected);
            }
        }

        //[Fact]
        //public async Task T()
        //{
        //    var s = new ServiceCollection();
        //    s.AddSignalR();
        //    var serviceProvider = s.BuildServiceProvider();
        //    var endPoint = serviceProvider.GetService<HubEndPoint<TestHub>>();

        //    //var lifetimeManager = serviceProvider.GetService<HubLifetimeManager<TestHub>>();

        //    var connectionManager = new ConnectionManager();
        //    var connection = connectionManager.AddNewConnection(new HttpConnection(new PipelineFactory())).Connection;
        //    connection.Metadata["formatType"] = "json";

        //    await endPoint.OnConnectedAsync(connection);

        //    //var hubConnection = new HubConnection(connection, serviceProvider.GetService<InvocationAdapterRegistry>());
        //    //await lifetimeManager.OnConnectedAsync(hubConnection);

        //    var ss = serviceProvider.GetService<InvocationAdapterRegistry>().GetInvocationAdapter("json");

        //    var tasks = new List<Task>(3);
        //    tasks.Add();
        //    //tasks.Add(lifetimeManager.InvokeConnectionAsync(connection.ConnectionId, "Microsoft.AspNetCore.SignalR.Tests.HubEndPointTests.TestHub.Send", new[] { "test" }));
        //    //tasks.Add(lifetimeManager.InvokeConnectionAsync(connection.ConnectionId, "Microsoft.AspNetCore.SignalR.Tests.HubEndPointTests.TestHub.Send", new[] { "test" }));
        //    //tasks.Add(lifetimeManager.InvokeConnectionAsync(connection.ConnectionId, "Microsoft.AspNetCore.SignalR.Tests.HubEndPointTests.TestHub.Send", new[] { "test" }));

        //    await Task.WhenAll(tasks);

        //    Assert.Equal(3, TestHub.MethodCallCount);
        //}

        internal class TestHubLifetimeManager<THub> : HubLifetimeManager<THub>
        {
            public static int OnConnected = 0;
            public static int OnDisconnected = 0;

            public override Task AddGroupAsync(HubConnection connection, string groupName)
            {
                throw new NotImplementedException();
            }

            public override Task InvokeAllAsync(string methodName, object[] args)
            {
                throw new NotImplementedException();
            }

            public override Task InvokeConnectionAsync(string connectionId, string methodName, object[] args)
            {
                throw new NotImplementedException();
            }

            public override Task InvokeGroupAsync(string groupName, string methodName, object[] args)
            {
                throw new NotImplementedException();
            }

            public override Task InvokeUserAsync(string userId, string methodName, object[] args)
            {
                throw new NotImplementedException();
            }

            public override Task OnConnectedAsync(HubConnection connection)
            {
                OnConnected++;
                return Task.FromResult(0);
            }

            public override Task OnDisconnectedAsync(HubConnection connection)
            {
                OnDisconnected++;
                return Task.FromResult(0);
            }

            public override Task RemoveGroupAsync(HubConnection connection, string groupName)
            {
                throw new NotImplementedException();
            }
        }

        internal class TestHub : Hub
        {
            internal static int MethodCallCount = 0;
            public async Task Send(string message)
            {
                await Context.Connection.InvokeAsync("Send", message);
                Interlocked.Increment(ref MethodCallCount);
            }
        }
    }
}
