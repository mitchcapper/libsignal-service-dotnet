using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Coe.WebSocketWrapper
{
    public class WebSocketWrapper
    {
        //https://gist.github.com/xamlmonkey/4737291
        public static readonly string TAG = "[WebSocketWrapper] ";

        public BlockingCollection<byte[]> OutgoingQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
        private Task HandleOutgoing;
        private Task HandleIncoming;

        private const int ReceiveChunkSize = 1024;
        private const int SendChunkSize = 1024;

        private ClientWebSocket _ws;
        private readonly Uri _uri;
        private readonly CancellationToken Token;

        private Action _onConnected;
        private Action<byte[]> _onMessage;

        public WebSocketWrapper(string uri, CancellationToken token)
        {
            CreateSocket();
            _uri = new Uri(uri);
            Token = token;
        }
        private void CreateSocket()
        {
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        }

        public void HandleOutgoingWS()
        {
            Debug.WriteLine(TAG + "HandleOutgoingWS started");
            byte[] buf = null;
            while (!Token.IsCancellationRequested)
            {
                
                try
                {
                    if (buf == null)
                        buf = OutgoingQueue.Take(Token);
                    reconnect_task?.Wait();
                    _ws.SendAsync(new ArraySegment<byte>(buf, 0, buf.Length), WebSocketMessageType.Binary, true, Token).Wait();
                    buf = null; //set to null so we do not retry the same block
                }
                catch (TaskCanceledException e)
                {
                    if (!Token.IsCancellationRequested)
                        reconnect().Wait();
                    Debug.WriteLine(TAG + "HandleOutgoingWS shutting down");
                }
                catch (Exception e)
                {
                    Debug.WriteLine(TAG + "WS SendAsync failed: " + e.Message);
                    reconnect().Wait();
                }
            }
            //TODO dispose
            Debug.WriteLine(TAG + "HandleOutgoingWS finished");
        }
        private Task reconnect_task;
        public async Task reconnect()
        {
            if (Token.IsCancellationRequested)
                return;
            if (reconnect_task != null)
            {
                await reconnect_task;
                return;
            }
            var task = new TaskCompletionSource<bool>();
            reconnect_task = task.Task;
            var tries = 0;
            try
            {
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "goodbye", Token);
            }
            catch(Exception e)
            {
                Debug.WriteLine("exception while closing of: " + e.Message);
            }
            if (Token.IsCancellationRequested)
                return;
            while (true)
            {
                try
                {
                    tries++;
                    CreateSocket();
                    await _ws.ConnectAsync(_uri, Token);
                    break;
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Unable to reconnect due to: " + e.Message);
                    var delay_length = 15;
                    if (tries > 20)
                        delay_length = 60 * 5;
                    if (tries > 10)
                        delay_length = 60;
                    if (tries > 5)
                        delay_length = 30;
                    await Task.Delay(1000 * delay_length);
                }
            }
            task.SetResult(true);
            reconnect_task = null;
            Debug.WriteLine("SUCCESSFUL RECONNECT");

        }
        public void HandleIncomingWS()
        {
            Debug.WriteLine(TAG + "HandleIncomingWS started");
            var buffer = new byte[ReceiveChunkSize];
            while (!Token.IsCancellationRequested)
            {
                var message = new MemoryStream();
                WebSocketReceiveResult result;
                try
                {
                    do
                    {
                        reconnect_task?.Wait();
                        result = _ws.ReceiveAsync(new ArraySegment<byte>(buffer), Token).Result;
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).Wait();
                            throw new Exception("Got a close message requesting reconnect");
                        }
                        else
                        {
                            message.Write(buffer, 0, result.Count);
                        }
                    } while (!result.EndOfMessage);
                    CallOnMessage(message.ToArray());
                }
                catch (TaskCanceledException e)
                {
                    if (!Token.IsCancellationRequested)
                        reconnect().Wait();
                    Debug.WriteLine(TAG + "HandleIncomingWS shutting down");
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine(e.StackTrace);
                    reconnect().Wait();
                }
            }
            //TODO dispose
            Debug.WriteLine(TAG + "HandleIncomingWS finished");
        }

        /// <summary>
        /// Connects to the WebSocket server.
        /// </summary>
        /// <returns></returns>
        public WebSocketWrapper Connect()
        {
            ConnectAsync();
            return this;
        }

        /// <summary>
        /// Set the Action to call when the connection has been established.
        /// </summary>
        /// <param name="onConnect">The Action to call.</param>
        /// <returns></returns>
        public WebSocketWrapper OnConnect(Action onConnect)
        {
            _onConnected = onConnect;
            return this;
        }

        /// <summary>
        /// Set the Action to call when a messages has been received.
        /// </summary>
        /// <param name="onMessage">The Action to call.</param>
        /// <returns></returns>
        public void OnMessage(Action<byte[]> onMessage)
        {
            _onMessage = onMessage;
        }

        private async void ConnectAsync()
        {
            try
            {
                await _ws.ConnectAsync(_uri, Token);
                HandleOutgoing = Task.Factory.StartNew(HandleOutgoingWS, TaskCreationOptions.LongRunning);
                HandleIncoming = Task.Factory.StartNew(HandleIncomingWS, TaskCreationOptions.LongRunning);
                CallOnConnected();
            }
            catch (Exception e)
            {
                Debug.WriteLine("ConnectAsync crashed.");
                Debug.WriteLine(e.Message);
                Debug.WriteLine(e.StackTrace);
            }
        }

        private void CallOnMessage(byte[] result)
        {
            if (_onMessage != null)
                RunInTask(() => _onMessage(result));
        }

        private void CallOnConnected()
        {
            if (_onConnected != null)
                RunInTask(() => _onConnected());
        }

        private static void RunInTask(Action action)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.WriteLine("RunInTask failed. " + action);
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine(e.StackTrace);
                }
            });
        }
    }
}
