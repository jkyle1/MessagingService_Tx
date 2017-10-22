using System;
using MessageDomain.Interfaces;
using MessageDomain.Services;
using Ninject.Web.Common;
using RabbitMQ.Client;
using Serilog;
using Serilog.Events;

namespace SatMessageTx
{
    public class TxModule : Ninject.Modules.NinjectModule
    {
        public override void Load()
        {
            try
            {
                Bind<IConnectionFactory>().To<ConnectionFactory>().InSingletonScope();
                Bind<IOutgoingService>().To<OutgoingService>().InRequestScope();
                Bind<IHttpClientService>().To<HttpClientService>().InRequestScope();
                Bind<IEmailService>().To<EmailService>().InRequestScope();

                Bind<ILogger>()
                    .ToMethod(
                        x =>
                            new LoggerConfiguration().WriteTo.RollingFile(
                                @"C:\MessagingRxTxLogs\SatMessageTxLog-{Date}.txt", LogEventLevel.Debug).CreateLogger()).InSingletonScope();
            }
            catch (Exception ex)
            {
                Console.WriteLine(@"Configuration error(s):  " + ex.Message);
            }
        }
    }
}
