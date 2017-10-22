using System;
using System.Threading;
using Ninject;
using RabbitMQ.Client;
using Serilog;

namespace SatMessageTx
{
    public class TxMessengerServiceControl 
    {
        private static ConnectionFactory _factory;
        private static IConnection _connection;
        private static IModel _channelTxSub;
        private static int _retries;
        private static IKernel _kernel;
        private static readonly ILogger Log = Serilog.Log.Logger;


        public bool Start()
        {
            try
            {
                _kernel = new StandardKernel();
                _kernel.Load(new TxModule());

                Cleanup();

                _factory = new ConnectionFactory()
                {
                    HostName = "localhost",
                    RequestedHeartbeat = 30,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
                };

                while (!Connect())
                {
                    Thread.Sleep(new TimeSpan(0, 0, 30));
                }
            }
            catch (Exception ex)
            {
                Log.Error("TxMessengerServiceControl Start Exception: | " + ex.Message);
            }
            return false;
        }

        public bool Stop()
        {
            try
            {
                return Cleanup();
            }
            catch (Exception ex)
            {
                Log.Error("TxMessengerServiceControl Stop Exception | " + ex.Message);
            }
            return false;
        }

        private static bool Connect()
        {
            try
            {
                _connection = _factory.CreateConnection();
                _channelTxSub = _connection.CreateModel();
                _channelTxSub.QueueBind("AcmeTx", "AcmeOut", "AcmeMT", null);
                _channelTxSub.BasicRecover(true);
                var messageListener = new MessageOutListener(_kernel, _channelTxSub);
                messageListener.ListenForAcmeMessages();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TxMessengerServiceControl Connect Exception | " + ex.Message);
            }
            return false;
        }

        private static bool Cleanup()
        {
            try
            {
                while (_channelTxSub != null && _channelTxSub.IsOpen)
                {
                    _channelTxSub.Close();
                    _channelTxSub = null;
                }

                if (_connection != null && _connection.IsOpen)
                {
                    _connection.Close();
                    _connection = null;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("TxMessengerServiceControl Cleanup Exception | " + ex.Message);
            }
            return false;
        }

    }
}
