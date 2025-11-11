using System.Net.Sockets;
using GaussianImageProcessingSystem.Models;
using GaussianImageProcessingSystem.Services;
using Newtonsoft.Json;

namespace GaussianImageProcessingSystem.Nodes
{
    /// <summary>
    /// Master узел для распределения задач с выбором по минимальному среднему времени
    /// </summary>
    public class MasterNode : NodeBase
    {
        private List<SlaveInfo> _registeredSlaves;
        private Dictionary<string, TcpClient> _slaveConnections;
        private Dictionary<string, TcpClient> _clientConnections;
        private Dictionary<string, ClientRequestInfo> _pendingRequests;
        private Queue<PendingTask> _taskQueue;
        private Dictionary<string, bool> _slaveBusyStatus;
        private int _totalTasksReceived = 0;
        private int _totalTasksCompleted = 0;
        private DateTime _firstTaskTime;
        private DateTime _lastTaskTime;

        public int RegisteredSlavesCount => _registeredSlaves.Count;

        public MasterNode(int port) : base(port)
        {
            _registeredSlaves = new List<SlaveInfo>();
            _slaveConnections = new Dictionary<string, TcpClient>();
            _clientConnections = new Dictionary<string, TcpClient>();
            _pendingRequests = new Dictionary<string, ClientRequestInfo>();
            _taskQueue = new Queue<PendingTask>();
            _slaveBusyStatus = new Dictionary<string, bool>();
        }

        public override void Start()
        {
            base.Start();
            Log("═══════════════════════════════════════════════════════");
            Log("                  MASTER УЗЕЛ ЗАПУЩЕН                  ");
            Log("         Алгоритм: выбор Slave с min средним временем  ");
            Log("═══════════════════════════════════════════════════════");
            Log("");
        }

        protected override void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                switch (e.Message.Type)
                {
                    case MessageType.SlaveRegister:
                        HandleSlaveRegistration(e);
                        break;

                    case MessageType.ImageRequest:
                        HandleImageRequest(e);
                        break;

                    case MessageType.ImageResponse:
                        HandleImageResponse(e);
                        break;

                    case MessageType.SlaveStatistics:
                        HandleSlaveStatistics(e);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки сообщения: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработка регистрации Slave узла
        /// </summary>
        private void HandleSlaveRegistration(MessageReceivedEventArgs e)
        {
            try
            {
                string dataJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                SlaveRegistrationData regData = JsonConvert.DeserializeObject<SlaveRegistrationData>(dataJson);

                // Проверяем, не зарегистрирован ли уже этот slave
                var existingSlave = _registeredSlaves.FirstOrDefault(s =>
                    s.IpAddress == regData.IpAddress && s.Port == regData.Port);

                if (existingSlave == null)
                {
                    SlaveInfo slaveInfo = new SlaveInfo
                    {
                        SlaveId = Guid.NewGuid().ToString(),
                        IpAddress = regData.IpAddress,
                        Port = regData.Port,
                        RegistrationTime = DateTime.Now,
                        TasksCompleted = 0,
                        TotalProcessingTime = 0,
                        AverageProcessingTime = 0
                    };

                    _registeredSlaves.Add(slaveInfo);

                    // Сохраняем подключение к Slave
                    string slaveKey = $"{slaveInfo.IpAddress}:{slaveInfo.Port}";
                    _slaveConnections[slaveKey] = e.Client;
                    _slaveBusyStatus[slaveKey] = false;

                    Log($"═══════════════════════════════════════════════════════");
                    Log($"   Зарегистрирован SLAVE #{_registeredSlaves.Count}");
                    Log($"   Адрес: {slaveInfo.IpAddress}:{slaveInfo.Port}");
                    Log($"   Всего Slave узлов: {_registeredSlaves.Count}");
                    Log($"═══════════════════════════════════════════════════════");

                    // Отправляем подтверждение
                    SendAcknowledgmentAsync(e.Client);

                    // Обрабатываем очередь задач
                    ProcessTaskQueue();
                }
                else
                {
                    Log($"⚠️ Slave узел уже зарегистрирован: {regData.IpAddress}:{regData.Port}");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка регистрации Slave: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработка запроса на обработку изображения от клиента
        /// </summary>
        private void HandleImageRequest(MessageReceivedEventArgs e)
        {
            try
            {
                if (_registeredSlaves.Count == 0)
                {
                    Log("Нет доступных Slave узлов для обработки", LogLevel.Warning);
                    return;
                }

                string packetJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                _totalTasksReceived++;

                if (_totalTasksReceived == 1)
                {
                    _firstTaskTime = DateTime.Now;
                }

                Log($"");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   ЗАДАЧА #{_totalTasksReceived}: {packet.FileName}");
                Log($"   PacketId: {packet.PacketId}");
                Log($"   Размер: {packet.ImageData.Length / 1024}KB");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                // Сохраняем подключение клиента
                string clientKey = $"{e.Message.SenderIp}:{e.Message.SenderPort}";
                if (!_clientConnections.ContainsKey(clientKey))
                {
                    _clientConnections[clientKey] = e.Client;
                }

                ClientRequestInfo clientInfo = new ClientRequestInfo
                {
                    ClientIp = e.Message.SenderIp,
                    ClientPort = e.Message.SenderPort,
                    RequestTime = DateTime.Now,
                    FileName = packet.FileName,
                    Client = e.Client
                };

                _pendingRequests[packet.PacketId] = clientInfo;

                PendingTask task = new PendingTask
                {
                    Message = e.Message,
                    PacketId = packet.PacketId,
                    FileName = packet.FileName,
                    ClientInfo = clientInfo
                };

                // Выбираем Slave с минимальным средним временем
                SlaveInfo bestSlave = SelectBestSlave();

                if (bestSlave != null)
                {
                    AssignTaskToSlave(task, bestSlave);
                }
                else
                {
                    _taskQueue.Enqueue(task);
                    Log($"  Все Slave заняты! Задача #{_totalTasksReceived} в очередь (позиция: {_taskQueue.Count})", LogLevel.Warning);
                    ShowSlaveStatus();
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки запроса изображения: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Выбор лучшего Slave по минимальному среднему времени обработки
        /// </summary>
        private SlaveInfo SelectBestSlave()
        {
            // Собираем свободные Slave
            var freeSlaves = _registeredSlaves
                .Where(s => !_slaveBusyStatus[$"{s.IpAddress}:{s.Port}"])
                .ToList();

            if (freeSlaves.Count == 0)
                return null;

            // Выбираем Slave с минимальным средним временем
            // Если у Slave еще не было задач (AverageProcessingTime == 0), считаем его приоритетным
            SlaveInfo bestSlave = freeSlaves
                .OrderBy(s => s.TasksCompleted == 0 ? -1 : s.AverageProcessingTime)
                .First();

            int slaveNumber = _registeredSlaves.FindIndex(s =>
                s.IpAddress == bestSlave.IpAddress && s.Port == bestSlave.Port) + 1;

            if (bestSlave.TasksCompleted == 0)
            {
                Log($"🎯 Выбран Slave #{slaveNumber} (новый, без истории)");
            }
            else
            {
                Log($"🎯 Выбран Slave #{slaveNumber} (среднее время: {bestSlave.AverageProcessingTime:F2} сек, задач: {bestSlave.TasksCompleted})");
            }

            return bestSlave;
        }

        /// <summary>
        /// Назначить задачу на Slave
        /// </summary>
        private async void AssignTaskToSlave(PendingTask task, SlaveInfo slave)
        {
            string slaveKey = $"{slave.IpAddress}:{slave.Port}";

            _slaveBusyStatus[slaveKey] = true;
            task.ClientInfo.RequestTime = DateTime.Now;

            if (_slaveConnections.TryGetValue(slaveKey, out TcpClient slaveClient))
            {
                NetworkMessage message = new NetworkMessage
                {
                    Type = MessageType.ImageRequest,
                    Data = task.Message.Data
                };

                bool sent = await _tcpService.SendMessageAsync(message, slaveClient);

                if (sent)
                {
                    int slaveNumber = _registeredSlaves.FindIndex(s =>
                        s.IpAddress == slave.IpAddress && s.Port == slave.Port) + 1;

                    Log($"  Задача {task.FileName} → Slave #{slaveNumber} ({slave.IpAddress}:{slave.Port})");

                    int busyCount = _slaveBusyStatus.Count(kvp => kvp.Value);
                    Log($"      Занято: {busyCount}/{_registeredSlaves.Count}, Свободно: {_registeredSlaves.Count - busyCount}");
                    Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                }
                else
                {
                    Log($"Не удалось отправить задачу Slave {slaveKey}", LogLevel.Error);
                    _slaveBusyStatus[slaveKey] = false;
                }
            }
        }

        /// <summary>
        /// Обработка статистики от Slave
        /// </summary>
        private void HandleSlaveStatistics(MessageReceivedEventArgs e)
        {
            try
            {
                string statsJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                var stats = JsonConvert.DeserializeObject<dynamic>(statsJson);

                int port = (int)stats.Port;
                var slave = _registeredSlaves.FirstOrDefault(s => s.Port == port);

                if (slave != null)
                {
                    slave.TasksCompleted = (int)stats.TasksCompleted;
                    slave.TotalProcessingTime = (double)stats.TotalProcessingTime;
                    slave.AverageProcessingTime = (double)stats.AverageProcessingTime;

                    Log($"📊 Обновлена статистика Slave (порт {port}): " +
                        $"задач={slave.TasksCompleted}, среднее время={slave.AverageProcessingTime:F2} сек");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки статистики: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработка ответа от Slave узла
        /// </summary>
        private async void HandleImageResponse(MessageReceivedEventArgs e)
        {
            try
            {
                string packetJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                string slaveKey = $"{e.Message.SenderIp}:{packet.SlavePort}";

                _totalTasksCompleted++;
                _lastTaskTime = DateTime.Now;

                Log($"");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   РЕЗУЛЬТАТ от Slave: {packet.FileName}");
                Log($"   Размер: {e.Message.Data.Length / 1024}KB");

                if (_pendingRequests.TryGetValue(packet.PacketId, out ClientRequestInfo clientInfo))
                {
                    TimeSpan processingTime = DateTime.Now - clientInfo.RequestTime;

                    int slaveNumber = _registeredSlaves.FindIndex(s =>
                        s.Port == packet.SlavePort) + 1;

                    Log($"      Время обработки: {processingTime.TotalSeconds:F2} сек");
                    Log($"      Обработал: Slave #{slaveNumber}");

                    // Помечаем Slave как свободный
                    if (_slaveBusyStatus.ContainsKey(slaveKey))
                    {
                        _slaveBusyStatus[slaveKey] = false;
                        Log($"   Slave {slaveKey} теперь СВОБОДЕН!");
                    }

                    // Отправляем результат клиенту
                    if (clientInfo.Client != null && clientInfo.Client.Connected)
                    {
                        NetworkMessage clientMessage = new NetworkMessage
                        {
                            Type = MessageType.ImageResponse,
                            Data = e.Message.Data
                        };

                        bool sent = await _tcpService.SendMessageAsync(clientMessage, clientInfo.Client);

                        if (sent)
                        {
                            Log($"   Результат отправлен клиенту");
                        }
                    }

                    _pendingRequests.Remove(packet.PacketId);
                }

                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   Прогресс: {_totalTasksCompleted}/{_totalTasksReceived} завершено");

                if (_totalTasksCompleted == _totalTasksReceived && _totalTasksReceived > 0)
                {
                    ShowFinalStatistics();
                }

                ProcessTaskQueue();
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки ответа от Slave: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработать очередь задач
        /// </summary>
        private void ProcessTaskQueue()
        {
            while (_taskQueue.Count > 0)
            {
                SlaveInfo bestSlave = SelectBestSlave();

                if (bestSlave == null)
                {
                    Log($"Очередь: {_taskQueue.Count} задач ожидают, но нет свободных Slave", LogLevel.Warning);
                    ShowSlaveStatus();
                    break;
                }

                PendingTask task = _taskQueue.Dequeue();
                Log($"Задача {task.FileName} извлечена из очереди (осталось в очереди: {_taskQueue.Count})");

                AssignTaskToSlave(task, bestSlave);
            }
        }

        /// <summary>
        /// Показать статус всех Slave узлов
        /// </summary>
        private void ShowSlaveStatus()
        {
            Log("╔═══════════════════════════════════════════════════════╗");
            Log("║               СТАТУС ВСЕХ SLAVE УЗЛОВ                 ║");
            Log("╚═══════════════════════════════════════════════════════╝");

            if (_registeredSlaves.Count == 0)
            {
                Log("  ⚠️ Нет зарегистрированных Slave узлов!");
                return;
            }

            for (int i = 0; i < _registeredSlaves.Count; i++)
            {
                var slave = _registeredSlaves[i];
                string key = $"{slave.IpAddress}:{slave.Port}";
                bool isBusy = _slaveBusyStatus.ContainsKey(key) && _slaveBusyStatus[key];
                string status = isBusy ? "🔴 ЗАНЯТ" : "🟢 СВОБОДЕН";

                Log($"  [{i + 1}] {slave.IpAddress}:{slave.Port.ToString().PadRight(5)} - {status}");
                Log($"      Задач: {slave.TasksCompleted}, Среднее время: {slave.AverageProcessingTime:F2} сек");
            }

            int busyCount = _slaveBusyStatus.Count(kvp => kvp.Value);
            int freeCount = _slaveBusyStatus.Count(kvp => !kvp.Value);

            Log($"╔═══════════════════════════════════════════════════════╗");
            Log($"║ Всего: {_registeredSlaves.Count}  |  🔴 Занято: {busyCount}  |  🟢 Свободно: {freeCount}      ║");
            Log($"╚═══════════════════════════════════════════════════════╝");
        }

        /// <summary>
        /// Показать итоговую статистику
        /// </summary>
        private void ShowFinalStatistics()
        {
            TimeSpan totalTime = _lastTaskTime - _firstTaskTime;

            Log($"");
            Log($"╔═══════════════════════════════════════════════════════════════╗");
            Log($"║                     ВСЕ ЗАДАЧИ ЗАВЕРШЕНЫ!                     ║");
            Log($"╚═══════════════════════════════════════════════════════════════╝");
            Log($"");
            Log($" Итоговая статистика производительности:");
            Log($"");
            Log($"┌───────────────────────────────────────────────────────────┐");
            Log($"│ Общие показатели                                          │");
            Log($"├───────────────────────────────────────────────────────────┤");
            Log($"│ Всего задач обработано:     {_totalTasksCompleted}                            │");
            Log($"│ Количество Slave узлов:     {_registeredSlaves.Count}                            │");
            Log($"│ Общее время обработки:      {totalTime.TotalSeconds:F2} сек                 │");
            Log($"│ Среднее время на задачу:    {(totalTime.TotalSeconds / _totalTasksCompleted):F2} сек                 │");
            Log($"└───────────────────────────────────────────────────────────┘");
            Log($"");
            Log($"┌───────────────────────────────────────────────────────────┐");
            Log($"│         Производительность Slave (алгоритм выбора)        │");
            Log($"├───────────────────────────────────────────────────────────┤");

            for (int i = 0; i < _registeredSlaves.Count; i++)
            {
                var slave = _registeredSlaves[i];
                double percentage = _totalTasksCompleted > 0 ?
                    (slave.TasksCompleted * 100.0 / _totalTasksCompleted) : 0;

                string bar = new string('█', (int)(percentage / 5));

                Log($"│ Slave #{i + 1} (порт {slave.Port}):                              │");
                Log($"│   Задач обработано: {slave.TasksCompleted} ({percentage:F1}%)                      │");
                Log($"│   Среднее время: {slave.AverageProcessingTime:F2} сек/задача                    │");
                Log($"│   Нагрузка: {bar}                                     │");
                Log($"├───────────────────────────────────────────────────────────┤");
            }

            Log($"└───────────────────────────────────────────────────────────┘");
        }

        private async void SendAcknowledgmentAsync(TcpClient client)
        {
            NetworkMessage ackMessage = new NetworkMessage
            {
                Type = MessageType.Acknowledgment,
                Data = System.Text.Encoding.UTF8.GetBytes("OK")
            };

            await _tcpService.SendMessageAsync(ackMessage, client);
        }
    }

    /// <summary>
    /// Информация о Slave узле
    /// </summary>
    public class SlaveInfo
    {
        public string SlaveId { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public DateTime RegistrationTime { get; set; }
        public int TasksCompleted { get; set; }
        public double TotalProcessingTime { get; set; }
        public double AverageProcessingTime { get; set; }
    }

    /// <summary>
    /// Информация о запросе клиента
    /// </summary>
    public class ClientRequestInfo
    {
        public string ClientIp { get; set; }
        public int ClientPort { get; set; }
        public DateTime RequestTime { get; set; }
        public string FileName { get; set; }
        public TcpClient Client { get; set; }
    }

    /// <summary>
    /// Задача в очереди
    /// </summary>
    public class PendingTask
    {
        public NetworkMessage Message { get; set; }
        public string PacketId { get; set; }
        public string FileName { get; set; }
        public ClientRequestInfo ClientInfo { get; set; }
    }
}