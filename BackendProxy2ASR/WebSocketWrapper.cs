using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using database_and_log;
using Serilog;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;


namespace BackendProxy2ASR
{
    public class WebSocketWrapper
    {
        private const int ReceiveChunkSize = 4096;
        private const int SendChunkSize = 4096;

        private readonly ClientWebSocket _ws;
        private readonly Uri _uri;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private Action<WebSocketWrapper> _onConnected;
        private Action<string, WebSocketWrapper> _onMessage;
        private Action<WebSocketWrapper> _onDisconnected;

        private ILogger _logger = LogHelper.GetLogger<WebSocketWrapper>();

        protected WebSocketWrapper(string uri, Dictionary<string, string> headers)
        {
            _ws = new ClientWebSocket();
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> entry in headers)
                {
                    _ws.Options.SetRequestHeader(entry.Key, entry.Value);
                }
            }
            _ws.Options.KeepAliveInterval = TimeSpan.Zero;
            _ws.Options.RemoteCertificateValidationCallback = delegate { return true; };
            _uri = new Uri(uri);
            _cancellationToken = _cancellationTokenSource.Token;
        }

        //----------------------------------------------------------------------------------------->
        // Creates a new instance.
        //      uri: The URI of the WebSocket server
        //----------------------------------------------------------------------------------------->
        public static WebSocketWrapper Create(string uri, Dictionary<string, string> headers = null)
        {
            return new WebSocketWrapper(uri, headers);
        }

        public WebSocketState GetWebSocketState()
        {
            return _ws.State;
        }

        //----------------------------------------------------------------------------------------->
        // Connects to WebSocket server
        //      public interface
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper Connect()
        {
            ConnectAsync();
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when the connection has been established.
        //      onConnect: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnConnect(Action<WebSocketWrapper> onConnect)
        {
            _onConnected = onConnect;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Disconnects from WebSocket server.
        //----------------------------------------------------------------------------------------->
        public async Task Disconnect()
        {
            CallOnDisconnected();
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, _cancellationToken);
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when the connection has been terminated.
        //      onDisconnect: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnDisconnect(Action<WebSocketWrapper> onDisconnect)
        {
            _onDisconnected = onDisconnect;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to WebSocket server
        //      public interface
        //----------------------------------------------------------------------------------------->
        public async Task SendBytes(byte[] bytes)
        {
            try
            {
                await SendBytesAsync(bytes);
            }
            catch (Exception e)
            {
                //Console.WriteLine("Catch error from sendbytes");
                _logger.Error("Catch error from sendbytes" + e.Message);
            }
            
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when a messages has been received.
        //      onMessage: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnMessage(Action<string, WebSocketWrapper> onMessage)
        {
            _onMessage = onMessage;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Send a message to the WebSocket server
        //      public interface
        //      message: The text message to send
        //----------------------------------------------------------------------------------------->
        public void SendMessage(string message)
        {
            SendMessageAsync(message);
        }

        //----------------------------------------------------------------------------------------->
        // Send a message to the WebSocket server
        //      actual implementation for the associated public interface
        //      message: The text message to send
        //----------------------------------------------------------------------------------------->
        private async void SendMessageAsync(string message)
        {
            try
            {
                if (_ws.State != WebSocketState.Open)
                {
                    throw new Exception("Connection is not open.");
                }

                var messageBuffer = Encoding.UTF8.GetBytes(message);
                var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

                for (var i = 0; i < messagesCount; i++)
                {
                    var offset = (SendChunkSize * i);
                    var count = SendChunkSize;
                    var lastMessage = ((i + 1) == messagesCount);

                    if ((count * (i + 1)) > messageBuffer.Length)
                    {
                        count = messageBuffer.Length - offset;
                    }

                    await _ws.SendAsync(new ArraySegment<byte>(messageBuffer, offset, count), WebSocketMessageType.Text, lastMessage, _cancellationToken);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "In SendMessageAsync: " + e.Message);
                throw;
            }
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to WebSocket server
        //      the actual implementation for the public interface
        //----------------------------------------------------------------------------------------->
        private async Task SendBytesAsync(byte[] bytes)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(FatalExceptionHandler);
            try
            {
                if (_ws.State != WebSocketState.Open)
                {
                    //throw new Exception("Connection is not open.");
                    _logger.Error("Connection is not open.");
                    return;
                }

                var messageBuffer = bytes;
                var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

                for (var i = 0; i < messagesCount; i++)
                {
                    var offset = (SendChunkSize * i);
                    var count = SendChunkSize;
                    var lastMessage = ((i + 1) == messagesCount);

                    if ((count * (i + 1)) > messageBuffer.Length)
                    {
                        count = messageBuffer.Length - offset;
                    }

                    await _ws.SendAsync(new ArraySegment<byte>(bytes, offset, count), WebSocketMessageType.Binary, lastMessage, _cancellationToken);
                }
            }
            catch (WebSocketException WebEx)
            {
                _logger.Error(WebEx, "In SendBytesAsync: " + WebEx.Message);
                _ws.Dispose();
                //throw;
            }
            catch (OperationCanceledException OpEx)
            {
                _logger.Error(OpEx, "In SendBytesAsync: " + OpEx.Message);
                _ws.Dispose();
                //throw;
            }
            catch (Exception Ex)
            {
                _logger.Error(Ex, "In SendBytesAsync: " + Ex.Message);
                _ws.Dispose();
                //throw;
            }
        }


        //----------------------------------------------------------------------------------------->
        // Connects to WebSocket server
        //      actual implementation for the associated public interface
        //----------------------------------------------------------------------------------------->
        private async void ConnectAsync()
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(FatalExceptionHandler);
            try
            {
                await _ws.ConnectAsync(_uri, _cancellationToken);
                CallOnConnected();
                StartListen();
            }
            catch(WebSocketException WebEx)
            {
                _logger.Error(WebEx, "In ConnectAsync: " + WebEx.Message);
                _ws.Dispose();
            }
            catch (OperationCanceledException OpEx)
            {
                _logger.Error(OpEx, "In ConnectAsync: " + OpEx.Message);
                _ws.Dispose();
            }
            catch (Exception Ex)
            {
                _logger.Error(Ex, "In ConnectAsync: " + Ex.Message);
                _ws.Dispose();
            }
        }

        private async void StartListen()
        {
            var buffer = new byte[ReceiveChunkSize];
            CancellationTokenSource source = new CancellationTokenSource(1000);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(FatalExceptionHandler);
            try
            {

                while (_ws.State == WebSocketState.Open )
                {
                    var stringResult = new StringBuilder();

                    WebSocketReceiveResult result = null;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationToken);
                        //result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), source.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            if (_ws.State == WebSocketState.Closed)
                            {
                                CallOnDisconnected();
                                _ws.Dispose();
                                return;
                            } else
                            {
                                await
                                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                                CallOnDisconnected();
                            }
                        }
                        else
                        {
                            var str = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            stringResult.Append(str);
                        }
                    } while (result != null && !result.EndOfMessage);

                    CallOnMessage(stringResult);

                }
            }
            catch (WebSocketException e)
            {
                _logger.Error(e, "In StartListen: " + e.Message);
                //throw;
                //CallOnDisconnected();
            }
            finally
            {
                _ws.Dispose();
            }
        }

        private void FatalExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            CallOnDisconnected();
            _ws.Dispose();
            Console.WriteLine(" MyHandler caught : " + e.Message);
            Console.WriteLine(" Runtime terminating: {0}", args.IsTerminating);
            
        }

        private void CallOnMessage(StringBuilder stringResult)
        {
            if (_onMessage != null)
                RunInTask(() => _onMessage(stringResult.ToString(), this));
        }

        private void CallOnDisconnected()
        {
            if (_onDisconnected != null)
                RunInTask(() => _onDisconnected(this));
        }

        private void CallOnConnected()
        {
            if (_onConnected != null)
                RunInTask(() => _onConnected(this));
        }

        private static void RunInTask(Action action)
        {
            Task.Factory.StartNew(action);
        }
    }
}
