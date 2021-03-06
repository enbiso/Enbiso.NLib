﻿using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Enbiso.NLib.EventBus.RabbitMq
{
    public interface IRabbitMqConnection : IDisposable
    {
        /// <summary>
        /// Verify Connection
        /// </summary>
        void VerifyConnection();

        /// <summary>
        /// Create Model
        /// </summary>
        /// <returns></returns>
        IModel CreateModel();
    }

    public class RabbitMqConnection : IRabbitMqConnection
    {
        private IConnection _connection;
        private bool _disposed;
        private readonly IConnectionFactory _connectionFactory;
        private readonly ILogger<RabbitMqConnection> _logger;
        private readonly int _retryCount;
        private readonly object _syncRoot = new object();

        /// <summary>
        /// Create default Rabbit connection
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="option"></param>
        public RabbitMqConnection(
            ILogger<RabbitMqConnection> logger,
            IOptions<RabbitMqOption> option)
        {
            var optVal = option.Value;
            _connectionFactory = new ConnectionFactory
            {
                HostName = optVal.Server ?? throw new ArgumentNullException(nameof(optVal.Server)),
                Port = optVal.Port
            };
            if (!string.IsNullOrEmpty(optVal.Username))
                _connectionFactory.UserName = optVal.Username;
            if (!string.IsNullOrEmpty(optVal.Password))
                _connectionFactory.Password = optVal.Password;
            if (!string.IsNullOrEmpty(optVal.VirtualHost))
                _connectionFactory.VirtualHost = optVal.VirtualHost;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _retryCount = optVal.RetryCount;
        }
        
        private bool IsConnected
            => _connection != null && _connection.IsOpen && !_disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex.ToString());
            }
        }

        private bool TryConnect()
        {
            _logger.LogInformation("RabbitMQ Client is trying to connect");
            lock (_syncRoot)
            {
                var policy = Policy.Handle<SocketException>()
                    .Or<BrokerUnreachableException>()
                    .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (ex, time) =>
                        {
                            _logger.LogWarning(ex.ToString());
                        });

                policy.Execute(() =>
                {
                    _connection = _connectionFactory.CreateConnection();
                });

                if (!IsConnected)
                {
                    _logger.LogCritical("FATAL ERROR: RabbitMQ connections could not be created and opened");
                    return false;
                }

                _connection.ConnectionShutdown += OnConnectionShutdown;
                _connection.CallbackException += OnCallbackException;
                _connection.ConnectionBlocked += OnConnectionBlocked;

                _logger.LogInformation(
                    $"RabbitMQ persistent connection acquired a connection {_connection.Endpoint.HostName} and is subscribed to failure events");
                return true;
            }
        }

        public void VerifyConnection()
        {
            if(!IsConnected && !TryConnect())
                throw new Exception("Unable to connect to Rabbit MQ");
        }
        
        public IModel CreateModel()
        {
            VerifyConnection();
            return _connection.CreateModel();
        }

        #region private methods

        private void OnConnectionBlocked(object sender, ConnectionBlockedEventArgs e)
        {
            if (_disposed) return;
            _logger.LogWarning("A RabbitMQ connection is shutdown. Trying to re-connect...");
            TryConnect();
        }

        private void OnCallbackException(object sender, CallbackExceptionEventArgs e)
        {
            if (_disposed) return;
            _logger.LogWarning("A RabbitMQ connection throw exception. Trying to re-connect...");
            TryConnect();
        }

        private void OnConnectionShutdown(object sender, ShutdownEventArgs reason)
        {
            if (_disposed) return;
            _logger.LogWarning("A RabbitMQ connection is on shutdown. Trying to re-connect...");
            TryConnect();
        }

        #endregion
    }
}
