﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Tact.Diagnostics;
using Tact.Net.WebSockets;
using Tact.Practices;
using Tact.Practices.LifetimeManagers;
using Tact.Practices.LifetimeManagers.Attributes;
using Tact.Rpc.Configuration;
using Tact.Rpc.Models;
using Tact.Rpc.Serialization;
using Tact.Rpc.Services;

namespace Tact.Rpc.Hosts.Implementation
{
    [RegisterCondition, RegisterSingleton(typeof(IHost), nameof(WebSocketHost))]
    public class WebSocketHost : IHost
    {
        private readonly IResolver _resolver;
        private readonly IWebHost _webHost;
        private readonly IReadOnlyList<IRpcHandler> _rpcHandlers;
        private readonly IReadOnlyList<ISerializer> _serializers;
        private readonly ILog _log;

        public WebSocketHost(IResolver resolver, IReadOnlyList<IRpcHandler> rpcHandlers, IReadOnlyList<ISerializer> serializers, WebSocketHostConfig hostConfig, ILog log)
        {
            _resolver = resolver;

            _log = log;

            _rpcHandlers = rpcHandlers;

            _serializers = serializers;

            _webHost = new WebHostBuilder()
                .UseKestrel()
                .UseUrls(hostConfig.Urls.ToArray())
                .ConfigureServices(services =>
                {
                    services.AddSingleton(resolver);
                })
                .Configure(app =>
                {
                    var handler = new WebSocketHandler(string.Empty, OnConnection);
                    app.UseWebSockets().UseWebSocketHandler(handler);
                })
                .Build();
        }

        public void Dispose()
        {
            _webHost.Dispose();
        }

        public Task InitializeAsync(CancellationToken cancelToken)
        {
            _webHost.Start();
            return Task.CompletedTask;
        }

        private void OnConnection(IWebSocketConnection connection)
        {
            var semaphore = new SemaphoreSlim(1, 1);

            connection.OnBinary = b => Task.Run(() => HandleRequestAsync(connection, semaphore, b), connection.HttpContext.RequestAborted);
            connection.OnClose = (s, m) => _log.Debug("OnClose - {0}: {1}", s, m);
            connection.OnError = ex =>
            {
                _log.Error(ex, "OnError");
                return false;
            };
            connection.OnFatalError = ex => _log.Fatal(ex, "OnFatalError");
            connection.OnOpen = ws => _log.Debug("OnOpen");
            connection.OnMessage = m => Task.Run(() => HandleRequestAsync(connection, semaphore, m), connection.HttpContext.RequestAborted);
        }

        private Task HandleRequestAsync(IWebSocketConnection connection, SemaphoreSlim semaphore, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            return HandleRequestAsync(connection, semaphore, bytes);
        }

        private async Task HandleRequestAsync(IWebSocketConnection connection, SemaphoreSlim semaphore, byte[] requestBytes)
        {
            try
            {
                var serializer = _serializers
                    .FirstOrDefault(s => connection.HttpContext.Request.ContentType
                    .StartsWith(s.ContentType, StringComparison.OrdinalIgnoreCase));

                using (var stream = new MemoryStream())
                {
                    stream.Write(requestBytes, 0, requestBytes.Length);

                    stream.Position = 0;

                    var callInfo = await serializer
                        .DeserializeAsync<RemoteCallInfo>(stream)
                        .ConfigureAwait(false);

                    var callInfoPosition = stream.Position;

                    foreach (var rpcService in _rpcHandlers)
                        if (rpcService.CanHandle(callInfo.Service, callInfo.Method, out Type type))
                        {
                            var model = await serializer
                                .DeserializeAsync(type, stream)
                                .ConfigureAwait(false);
                            
                            var result = await rpcService
                                .HandleAsync(_resolver, callInfo.Method, model)
                                .ConfigureAwait(false);

                            stream.Position = callInfoPosition;

                            await serializer
                                .SerializeToStreamAsync(result, stream)
                                .ConfigureAwait(false);

                            var resultBytes = stream.ToArray();

                            using (await semaphore.UseAsync(connection.HttpContext.RequestAborted).ConfigureAwait(false))
                            {
                                await connection
                                    .SendAsync(resultBytes, connection.HttpContext.RequestAborted)
                                    .ConfigureAwait(false);
                            }

                            return;
                        }

                    throw new InvalidOperationException("Handler Not Found");
                }
            }
            catch (Exception ex)
            {
                var messageBytes = Encoding.UTF8.GetBytes(ex.Message);
                
                using (await semaphore.UseAsync(connection.HttpContext.RequestAborted).ConfigureAwait(false))
                {
                    await connection
                        .SendAsync(messageBytes, connection.HttpContext.RequestAborted)
                        .ConfigureAwait(false);
                }

                _log.Error(ex);
            }
        }

        public class RegisterConditionAttribute : Attribute, IRegisterConditionAttribute
        {
            public bool ShouldRegister(IContainer container, Type toType)
            {
                var config = container.Resolve<WebSocketHostConfig>();
                return config.IsEnabled;
            }
        }
    }
}
