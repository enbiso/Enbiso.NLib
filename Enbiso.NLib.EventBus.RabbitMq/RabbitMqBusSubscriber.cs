using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Enbiso.NLib.EventBus.RabbitMq
{
    public class RabbitMqBusSubscriber: IEventSubscriber
    {
        private IModel _consumerChannel;
        private readonly IRabbitMqConnection _connection;
        private readonly IEventProcessor _eventProcessor;
        private readonly string _queueName;
        private readonly IEnumerable<string> _exchanges;

        public RabbitMqBusSubscriber(IRabbitMqConnection connection, IEventProcessor eventProcessor, IOptions<RabbitMqOption> optionWrap)
        {
            var option = optionWrap.Value;
            _queueName = option.Client;
            _exchanges = option.Exchanges?? new string[0];

            _connection = connection;
            _eventProcessor = eventProcessor;

            _eventProcessor.Setup(eventName =>
            {
                using var channel = _connection.CreateModel();
                foreach (var exchange in _exchanges)
                    channel.QueueBind(queue: _queueName, exchange: exchange, routingKey: eventName);
            });
        }
        
        private IModel CreateConsumerChannel()
        {
            _connection.VerifyConnection();
            var channel = _connection.CreateModel();
            foreach (var exchange in _exchanges)
            {
                channel.ExchangeDeclare(exchange: exchange, type: "direct");
            }

            channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            channel.CallbackException += (sender, ea) =>
            {
                _consumerChannel.Dispose();
                _consumerChannel = CreateConsumerChannel();
            };
            return channel;
        }
        
        public void Dispose()
        {
            _consumerChannel?.Dispose();
        }
        
        public Task Subscribe()
        {
            _consumerChannel = CreateConsumerChannel();
            var consumer = new EventingBasicConsumer(_consumerChannel);
            consumer.Received += async (model, ea) =>
            {
                var eventName = ea.RoutingKey;                
                await _eventProcessor.ProcessEvent(eventName, ea.Body.ToArray());
                // ACK
                _consumerChannel.BasicAck(ea.DeliveryTag, multiple:false);
            };
            _consumerChannel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }
    }
}