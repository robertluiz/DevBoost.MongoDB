using Confluent.Kafka;
using devboost.DroneDelivery.Kafka.Consumer.External;
using devboost.DroneDelivery.Kafka.Consumer.Model;
using devboost.DroneDelivery.Kafka.Consumer.Validators;
using Microsoft.Extensions.Configuration;
using Serilog.Core;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using devboost.DroneDelivery.Kafka.Consumer.Utils;

namespace devboost.DroneDelivery.Kafka.Consumer
{
    public class ConsoleApp
    {
        private readonly Logger _logger;
        private readonly IConfiguration _configuration;
        private readonly DeliveryExternalControl _deliveryExternalControl;

        public ConsoleApp(Logger logger, IConfiguration configuration, DeliveryExternalControl deliveryExternalControl)
        {
            _logger = logger;
            _configuration = configuration;
            _deliveryExternalControl = deliveryExternalControl;
        }

        public void Run()
        {
            _logger.Information("Recuperando login e senha");
            string usuario = _configuration["user_login"];
            string senha = _configuration["user_pass"];

            _logger.Information("Testando o consumo de mensagens com Kafka");
            var nomeTopic = _configuration["Kafka_Topic"];
            _logger.Information($"Topic = {nomeTopic}");

            try
            {
                var bootstrapServers = _configuration["Kafka_Broker"];
                TopicTools.CreateTopicAsync(bootstrapServers, nomeTopic);
            }
            catch (Exception e)
            {
                _logger.Warning("Topic j� existe");

            }


            try
            {
                using var consumer = GetConsumerBuilder();
                consumer.Subscribe(nomeTopic);

                try
                {
                    while (true)
                    {
                        try
                        {

                            var cr = consumer.Consume();
                            var dados = cr.Message.Value;

                            _logger.Information(
                                $"Mensagem lida: {dados}");

                            var pedido = JsonSerializer.Deserialize<Pedido>(dados,
                                new JsonSerializerOptions()
                                {
                                    PropertyNameCaseInsensitive = true
                                });

                            var validationResult = new PedidoValidator().Validate(pedido);
                            if (validationResult.IsValid)
                            {
                                var token = _deliveryExternalControl.Logar(new Auth() { Login = usuario, Senha = senha });
                                _deliveryExternalControl.EnviarPedido(pedido, token);
                                _logger.Information("A��o registrada com sucesso!");
                            }
                            else
                                _logger.Warning("Dados inv�lidos para a A��o");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            _logger.Warning("Erro na fila");
                        }

                       
                    }
                }
                catch (OperationCanceledException)
                {
                    consumer.Close();
                    _logger.Warning("Cancelada a execu��o do Consumer...");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Exce��o: {ex.GetType().FullName} | " +
                                $"Mensagem: {ex.Message}");
            }
        }

        private IConsumer<Ignore, string> GetConsumerBuilder()
        {
            var bootstrapServers = _configuration["Kafka_Broker"];

            _logger.Information($"BootstrapServers = {bootstrapServers}");

            var config = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = $"dronedelivery-consumer",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };
            _logger.Information($"GroupId = {config.GroupId}");

            return new ConsumerBuilder<Ignore, string>(config).Build();
        }
    }
}