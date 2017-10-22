using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Ninject;
using Ninject.Syntax;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MessageDomain.Interfaces;
using Newtonsoft.Json;
using Serilog;
using MessageData.DTO;

namespace SatMessageTx
{
    /// <summary>
    /// This class is used to listen for messages in the outgoing (AcmeTx)
    /// queue and in turn externally publishes the messages with an HttpClient
    /// </summary>
    public class MessageOutListener
    {
        private readonly IModel _channelTxSub;
        private readonly ILogger _log;
        private readonly IOutgoingService _outgoingService;
        private readonly IHttpClientService _externalPublisher;

        private const string Uri = "https://secure.anonymous.com";  //production 
        private const string RequestUri = "anonymous/MT";  //production  

        public MessageOutListener(IResolutionRoot kernel, IModel channel)
        {
            _channelTxSub = channel;
            _log = kernel.Get<ILogger>();
            _outgoingService = kernel.Get<IOutgoingService>();
            _externalPublisher = kernel.Get<IHttpClientService>();
        }

        /// <summary>
        /// Receives Message from outgoing queue, deserializes as an MtMessage
        /// DTO then in turn builds up a FormDataCollection of KVPs to be published 
        /// externally
        /// </summary>
        public void ListenForAcmeMessages()
        {
            ulong eaTag = 0;

            try
            {
                var consumer = new EventingBasicConsumer(_channelTxSub);
                consumer.Received += (ch, ea) =>
                {
                    var body = ea.Body;
                    eaTag = ea.DeliveryTag;
                    // ... process the message
                    byte[] payload = null;
                    Type msgType = null;

                    var msgOut = Encoding.UTF8.GetString(body);
                    var deserial = JsonConvert.DeserializeObject<MtMessageDTO>(msgOut);
                 
                    var kvpList = _outgoingService.ConvertToKvpList(deserial);
                    var fue = new FormUrlEncodedContent(kvpList);
                    var handler = new WebRequestHandler();

                    string path = System.Reflection.Assembly.GetExecutingAssembly().Location;

                    if (!string.IsNullOrEmpty(path))
                    {

                        var certPath = System.IO.Path.Combine(path, "../Certificates/anonymouscom.crt");

                        handler.ServerCertificateValidationCallback = Target;
                        X509Certificate2 clientCertificate = new X509Certificate2(certPath);
                        handler.ClientCertificates.Add(clientCertificate);

                        var outcome = _externalPublisher.ClientPost(Uri, RequestUri, handler, false, fue, null);


                        if (outcome.Result != null && outcome.Result.Contains("OK"))
                        {
                            _channelTxSub.BasicAck(ea.DeliveryTag, false);
                            //_channelTxSub.BasicNack(ea.DeliveryTag, false, true);
                            _log.Information("Successfully published Message | Result: " + outcome.Result + "  | IMEI: " +
                                             kvpList.ToArray().FirstOrDefault(x => x.Key.ToLower() == "imei").Value);
                        }
                        else   
                        {
                           
                            //if not published mark header with retry attempt and increment counter
                            object i = 0;
                            if (ea.BasicProperties.Headers != null)
                            {
                                if (ea.BasicProperties.Headers.ContainsKey("x-redelivery-count"))
                                //check to see if message has retry count header
                                {
                                    ea.BasicProperties.Headers.TryGetValue("x-redelivery-count", out i);
                                }
                            }
                            if (i == null || (int)i < 2) //limit retry attempts to 2
                            {
                                ea.BasicProperties.Headers?.Remove("x-redelivery-count");
                                //if not null remove header for replacement/update
                                var bindingOneHeaders = new Dictionary<string, object>();
                                bindingOneHeaders.Add("x-redelivery-count", (int?)i + 1 ?? 1);
                                ea.BasicProperties.Headers = bindingOneHeaders;

                                _channelTxSub.BasicPublish(exchange: "AcmeOut", routingKey: "AcmeMT",
                                    basicProperties: ea.BasicProperties, body: ea.Body);
                                _channelTxSub.BasicReject(ea.DeliveryTag, false);
                                _log.Warning("FAILED EXTERNAL PUBLISH | " + "Error: " + outcome.Result + " | IMEI: " + kvpList.ToArray().FirstOrDefault(x => x.Key.ToLower() == "imei").Value + " Redelivery Attempt: " + (int?)i);
                            }
                            else //move to dead letter queue 
                            {
                                _channelTxSub.BasicReject(ea.DeliveryTag, false);
                            }
                        }
                    }
                };
               _channelTxSub.BasicConsume("AcmeTx", false, consumer);
            }
            catch (Exception ex)
            {
                _channelTxSub.BasicReject(eaTag, false); //on exception reject message 
                _log.Error("MessageOutListener ListenForAcmeMessages Exception | " + ex.Message);
            }
        }

        private static bool Target(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}
