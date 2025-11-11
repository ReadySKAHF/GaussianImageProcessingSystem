using System.Net;
using System.Net.Sockets;
using System.Threading;
using GaussianImageProcessingSystem.Models;

namespace GaussianImageProcessingSystem.Services
{
    /// <summary>
    /// Сервис для работы с TCP протоколом
    /// </summary>
    public class TcpService : IDisposable
    {
        private TcpListener _tcpListener;
        private TcpClient _connectedClient;
        private bool _isListening;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly object _clientLock = new object();
        private List<Task> _clientTasks = new List<Task>();

        public int Port { get; private set; }
        public bool IsListening => _isListening;

        /// <summary>
        /// Событие получения сообщения
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        /// Событие возникновения ошибки
        /// </summary>
        public event EventHandler<ErrorEventArgs> ErrorOccurred;

        public TcpService(int port)
        {
            Port = port;
        }

        /// <summary>
        /// Запуск прослушивания TCP порта (для Server/Master)
        /// </summary>
        public void StartListening()
        {
            if (_isListening)
                return;

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, Port);
                _tcpListener.Start();
                _cancellationTokenSource = new CancellationTokenSource();
                _isListening = true;

                Task.Run(() => AcceptClientsAsync(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                OnError($"Ошибка запуска прослушивания на порту {Port}: {ex.Message}");
            }
        }

        /// <summary>
        /// Остановка прослушивания
        /// </summary>
        public void StopListening()
        {
            if (!_isListening)
                return;

            _isListening = false;
            _cancellationTokenSource?.Cancel();

            lock (_clientLock)
            {
                _connectedClient?.Close();
                _connectedClient = null;
            }

            _tcpListener?.Stop();
        }

        /// <summary>
        /// Асинхронное принятие клиентов
        /// </summary>
        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (_isListening && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();

                    // Обрабатываем каждого клиента в отдельной задаче
                    var task = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                    lock (_clientTasks)
                    {
                        _clientTasks.Add(task);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_isListening)
                    {
                        OnError($"Ошибка принятия клиента: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Обработка подключенного клиента
        /// </summary>
        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            NetworkStream stream = null;

            try
            {
                stream = client.GetStream();

                while (client.Connected && !cancellationToken.IsCancellationRequested)
                {
                    // Проверяем, есть ли доступные данные
                    if (!stream.DataAvailable)
                    {
                        await Task.Delay(10, cancellationToken);
                        continue;
                    }

                    // Читаем длину сообщения (4 байта)
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, cancellationToken);

                    if (bytesRead == 0)
                        break; // Клиент отключился

                    if (bytesRead < 4)
                        continue; // Неполные данные

                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                    if (messageLength <= 0 || messageLength > 50000000) // Защита от слишком больших сообщений
                        continue;

                    // Читаем само сообщение
                    byte[] messageBuffer = new byte[messageLength];
                    int totalRead = 0;

                    while (totalRead < messageLength && client.Connected)
                    {
                        bytesRead = await stream.ReadAsync(
                            messageBuffer,
                            totalRead,
                            messageLength - totalRead,
                            cancellationToken);

                        if (bytesRead == 0)
                            break;

                        totalRead += bytesRead;
                    }

                    if (totalRead == messageLength)
                    {
                        NetworkMessage message = NetworkMessage.Deserialize(messageBuffer);

                        // Получаем информацию об отправителе
                        var remoteEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
                        if (remoteEndpoint != null)
                        {
                            message.SenderIp = remoteEndpoint.Address.ToString();
                            message.SenderPort = remoteEndpoint.Port;
                        }

                        OnMessageReceived(message, client);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    OnError($"Ошибка обработки клиента: {ex.Message}");
                }
            }
            finally
            {
                try
                {
                    stream?.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// Подключение к серверу (для Client)
        /// </summary>
        public async Task<TcpClient> ConnectAsync(string targetIp, int targetPort)
        {
            try
            {
                TcpClient client = new TcpClient();
                await client.ConnectAsync(targetIp, targetPort);

                lock (_clientLock)
                {
                    _connectedClient = client;
                }

                return client;
            }
            catch (Exception ex)
            {
                OnError($"Ошибка подключения к {targetIp}:{targetPort}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Начать прием сообщений из существующего подключения
        /// </summary>
        public void StartReceivingAsync(TcpClient client)
        {
            if (client == null || !client.Connected)
                return;

            if (_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource = new CancellationTokenSource();

            var task = Task.Run(() => HandleClientAsync(client, _cancellationTokenSource.Token));
            lock (_clientTasks)
            {
                _clientTasks.Add(task);
            }
        }

        /// <summary>
        /// Отправка сообщения по существующему подключению
        /// </summary>
        public async Task<bool> SendMessageAsync(NetworkMessage message, TcpClient client)
        {
            try
            {
                if (client == null || !client.Connected)
                {
                    OnError("Клиент не подключен");
                    return false;
                }

                byte[] messageData = message.Serialize();

                // Добавляем префикс длины (4 байта)
                byte[] lengthPrefix = BitConverter.GetBytes(messageData.Length);
                byte[] dataToSend = new byte[lengthPrefix.Length + messageData.Length];

                Buffer.BlockCopy(lengthPrefix, 0, dataToSend, 0, lengthPrefix.Length);
                Buffer.BlockCopy(messageData, 0, dataToSend, lengthPrefix.Length, messageData.Length);

                NetworkStream stream = client.GetStream();

                await stream.WriteAsync(dataToSend, 0, dataToSend.Length);
                await stream.FlushAsync();

                return true;
            }
            catch (Exception ex)
            {
                OnError($"Ошибка отправки сообщения: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Отправка сообщения с автоматическим подключением
        /// </summary>
        public async Task<bool> SendMessageAsync(NetworkMessage message, string targetIp, int targetPort)
        {
            TcpClient client = null;

            try
            {
                client = new TcpClient();
                await client.ConnectAsync(targetIp, targetPort);

                byte[] messageData = message.Serialize();

                // Добавляем префикс длины (4 байта)
                byte[] lengthPrefix = BitConverter.GetBytes(messageData.Length);
                byte[] dataToSend = new byte[lengthPrefix.Length + messageData.Length];

                Buffer.BlockCopy(lengthPrefix, 0, dataToSend, 0, lengthPrefix.Length);
                Buffer.BlockCopy(messageData, 0, dataToSend, lengthPrefix.Length, messageData.Length);

                NetworkStream stream = client.GetStream();

                await stream.WriteAsync(dataToSend, 0, dataToSend.Length);
                await stream.FlushAsync();

                return true;
            }
            catch (Exception ex)
            {
                OnError($"Ошибка отправки сообщения на {targetIp}:{targetPort}: {ex.Message}");
                return false;
            }
            finally
            {
                client?.Close();
            }
        }

        private void OnMessageReceived(NetworkMessage message, TcpClient client)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs
            {
                Message = message,
                Client = client
            });
        }

        private void OnError(string errorMessage)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs
            {
                ErrorMessage = errorMessage,
                Timestamp = DateTime.Now
            });
        }

        public void Dispose()
        {
            StopListening();

            lock (_clientLock)
            {
                _connectedClient?.Close();
                _connectedClient = null;
            }

            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Аргументы события получения сообщения
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        public NetworkMessage Message { get; set; }
        public TcpClient Client { get; set; }
    }

    /// <summary>
    /// Аргументы события ошибки
    /// </summary>
    public class ErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }
}