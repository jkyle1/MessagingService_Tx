using System;
using Serilog;
using Topshelf;

namespace SatMessageTx
{
    /// <summary>
    /// This application will listen for messages in the outgoing queue and 
    /// post them externally
    /// </summary>
    class TxMessenger
    {
        static void Main(string[] args)
        {
            try
            {
                Log.Logger = new LoggerConfiguration()
                    .WriteTo.RollingFile(@"C:\Acme TxMessenger Logs\TxMessengerServiceLog-{Date}.txt")
                    .CreateLogger();

                HostFactory.Run(x =>
                {
                    x.DependsOn("RabbitMQ");
                    x.DependsOn("Acme Encoder");
                    x.UseSerilog();
                    x.RunAsLocalSystem();
                    x.SetServiceName("Acme TxMessenger");
                    x.SetDescription("Acme TxMessenger Service (Start Order:  4 / Depends on Acme Encoder)");

                    x.Service<TxMessengerServiceControl>(s =>
                    {
                        s.ConstructUsing(name => new TxMessengerServiceControl());
                        s.WhenStarted(dsc => dsc.Start());
                        s.WhenStopped(dsc => dsc.Stop());
                    });

                    x.EnableShutdown();
                    x.EnablePauseAndContinue();
                    x.StartAutomatically();
                    x.EnableServiceRecovery(r =>
                    {
                        r.RestartService(0);
                        r.RestartComputer(5, "TxMessenger Service Failed - Server restart");
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Logger.Error("TxMessenger Main Exception | " + ex.Message);
            }
        }
    }
}
