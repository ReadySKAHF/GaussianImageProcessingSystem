using System.Net.Sockets;
using GaussianImageProcessingSystem.Models;
using GaussianImageProcessingSystem.Services;
using Newtonsoft.Json;

namespace GaussianImageProcessingSystem.Nodes
{
    /// <summary>
    /// Master узел для распределения задач с использованием Round Robin
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

        // Для Round Robin
        private int _currentSlaveIndex = 0;
        private readonly object _slaveSelectionLock = new object();

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
            Log("              Алгоритм: Round Robin (RR)               ");
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

                    string slaveKey = $"{slaveInfo.IpAddress}:{slaveInfo.Port}";
                    _slaveConnections[slaveKey] = e.Client;
                    _slaveBusyStatus[slaveKey] = false;

                    Log($"═══════════════════════════════════════════════════════");
                    Log($"   Зарегистрирован SLAVE #{_registeredSlaves.Count}");
                    Log($"   Адрес: {slaveInfo.IpAddress}:{slaveInfo.Port}");
                    Log($"   Всего Slave узлов: {_registeredSlaves.Count}");
                    Log($"═══════════════════════════════════════════════════════");

                    SendAcknowledgmentAsync(e.Client);
                    ProcessTaskQueue();
                }
                else
                {
                    Log($"Slave узел уже зарегистрирован: {regData.IpAddress}:{regData.Port}");
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
                Log($"   Фильтр: {packet.FilterSize}x{packet.FilterSize}");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

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

                SlaveInfo selectedSlave = SelectSlaveRoundRobin();

                if (selectedSlave != null)
                {
                    AssignTaskToSlave(task, selectedSlave);
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
        /// Выбор Slave узла по алгоритму Round Robin
        /// </summary>
        private SlaveInfo SelectSlaveRoundRobin()
        {
            lock (_slaveSelectionLock)
            {
                var freeSlaves = _registeredSlaves
                    .Where(s => !_slaveBusyStatus[$"{s.IpAddress}:{s.Port}"])
                    .ToList();

                if (freeSlaves.Count == 0)
                    return null;

                SlaveInfo selected = freeSlaves[_currentSlaveIndex % freeSlaves.Count];
                _currentSlaveIndex++;

                if (_currentSlaveIndex > 1000000)
                    _currentSlaveIndex = 0;

                int slaveNumber = _registeredSlaves.FindIndex(s =>
                    s.IpAddress == selected.IpAddress && s.Port == selected.Port) + 1;

                Log($"Round Robin -> Slave #{slaveNumber} " +
                    $"(задач: {selected.TasksCompleted}, среднее: {selected.AverageProcessingTime:F2} сек)");

                return selected;
            }
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

                    Log($"  -> Задача {task.FileName} -> Slave #{slaveNumber} ({slave.IpAddress}:{slave.Port})");

                    int busyCount = _slaveBusyStatus.Count(kvp => kvp.Value);
                    int freeCount = _slaveBusyStatus.Count(kvp => !kvp.Value);
                    Log($"      Занято: {busyCount}/{_registeredSlaves.Count}, Свободно: {freeCount}");
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

                    int slaveNumber = _registeredSlaves.FindIndex(s => s.Port == port) + 1;
                    Log($"Статистика Slave #{slaveNumber}: задач={slave.TasksCompleted}, среднее={slave.AverageProcessingTime:F2} сек");
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
                Log($"   Фильтр: {packet.FilterSize}x{packet.FilterSize}");

                if (_pendingRequests.TryGetValue(packet.PacketId, out ClientRequestInfo clientInfo))
                {
                    TimeSpan processingTime = DateTime.Now - clientInfo.RequestTime;

                    int slaveNumber = _registeredSlaves.FindIndex(s =>
                        s.Port == packet.SlavePort) + 1;

                    Log($"      Время обработки: {processingTime.TotalSeconds:F2} сек");
                    Log($"      Обработал: Slave #{slaveNumber}");

                    if (_slaveBusyStatus.ContainsKey(slaveKey))
                    {
                        _slaveBusyStatus[slaveKey] = false;
                        Log($"   Slave #{slaveNumber} теперь СВОБОДЕН!");
                    }

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
                SlaveInfo selectedSlave = SelectSlaveRoundRobin();

                if (selectedSlave == null)
                {
                    Log($"Очередь: {_taskQueue.Count} задач ожидают, но нет свободных Slave", LogLevel.Warning);
                    ShowSlaveStatus();
                    break;
                }

                PendingTask task = _taskQueue.Dequeue();
                Log($"Задача {task.FileName} извлечена из очереди (осталось: {_taskQueue.Count})");

                AssignTaskToSlave(task, selectedSlave);
            }
        }

        /// <summary>
        /// Показать статус всех Slave узлов
        /// </summary>
        private void ShowSlaveStatus()
        {
            Log("СТАТУС ВСЕХ SLAVE УЗЛОВ");

            if (_registeredSlaves.Count == 0)
            {
                Log("  Нет зарегистрированных Slave узлов!");
                return;
            }

            for (int i = 0; i < _registeredSlaves.Count; i++)
            {
                var slave = _registeredSlaves[i];
                string key = $"{slave.IpAddress}:{slave.Port}";
                bool isBusy = _slaveBusyStatus.ContainsKey(key) && _slaveBusyStatus[key];
                string status = isBusy ? "ЗАНЯТ" : "СВОБОДЕН";

                Log($"  [{i + 1}] {slave.IpAddress}:{slave.Port.ToString().PadRight(5)} - {status}");
                Log($"      Задач: {slave.TasksCompleted}, Среднее: {slave.AverageProcessingTime:F2} сек");
            }

            int busyCount = _slaveBusyStatus.Count(kvp => kvp.Value);
            int freeCount = _slaveBusyStatus.Count(kvp => !kvp.Value);

            Log($"Всего: {_registeredSlaves.Count}  |  Занято: {busyCount}  |  Свободно: {freeCount}");
        }

        /// <summary>
        /// Показать итоговую статистику
        /// </summary>
        private void ShowFinalStatistics()
        {
            TimeSpan totalTime = _lastTaskTime - _firstTaskTime;
            double avgTimePerTask = _totalTasksCompleted > 0 ? totalTime.TotalSeconds / _totalTasksCompleted : 0;
            double throughput = totalTime.TotalSeconds > 0 ? _totalTasksCompleted / totalTime.TotalSeconds : 0;

            Log($"");
            Log($"═══════════════════════════════════════════════════════════════");
            Log($"                   ВСЕ ЗАДАЧИ ЗАВЕРШЕНЫ!                       ");
            Log($"═══════════════════════════════════════════════════════════════");
            Log($"");
            Log($"╔══════════════════════════════════════════════════════════════╗");
            Log($"║             ИТОГОВАЯ СТАТИСТИКА ПРОИЗВОДИТЕЛЬНОСТИ           ║");
            Log($"╚══════════════════════════════════════════════════════════════╝");
            Log($"");
            Log($"┌──────────────────────────────────────────────────────────────┐");
            Log($"│ ОБЩИЕ ПОКАЗАТЕЛИ                                             │");
            Log($"├──────────────────────────────────────────────────────────────┤");
            Log($"│ Всего задач обработано:      {_totalTasksCompleted,4}                         │");
            Log($"│ Количество Slave узлов:      {_registeredSlaves.Count,4}                         │");
            Log($"│ Общее время обработки:       {totalTime.TotalSeconds,7:F2} сек                 │");
            Log($"│ Среднее время на задачу:     {avgTimePerTask,7:F2} сек                 │");
            Log($"│ Производительность:          {throughput,7:F2} задач/сек            │");
            Log($"└──────────────────────────────────────────────────────────────┘");
            Log($"");
            Log($"┌──────────────────────────────────────────────────────────────┐");
            Log($"│ ПРОИЗВОДИТЕЛЬНОСТЬ SLAVE УЗЛОВ (Round Robin)                 │");
            Log($"├──────────────────────────────────────────────────────────────┤");

            for (int i = 0; i < _registeredSlaves.Count; i++)
            {
                var slave = _registeredSlaves[i];
                double percentage = _totalTasksCompleted > 0 ?
                    (slave.TasksCompleted * 100.0 / _totalTasksCompleted) : 0;

                string bar = new string('█', Math.Min((int)(percentage / 5), 20));

                Log($"│                                                              │");
                Log($"│ Slave #{i + 1} (порт {slave.Port}):                                  │");
                Log($"│   Задач обработано:  {slave.TasksCompleted,4} ({percentage,5:F1}%)                        │");
                Log($"│   Среднее время:     {slave.AverageProcessingTime,7:F2} сек/задача                  │");
                Log($"│   Нагрузка: {bar,-20}                         │");
            }

            Log($"└──────────────────────────────────────────────────────────────┘");
            Log($"");

            // Эффективность балансировки
            double idealPercentage = 100.0 / _registeredSlaves.Count;
            double maxDeviation = _registeredSlaves
                .Select(s => Math.Abs((s.TasksCompleted * 100.0 / _totalTasksCompleted) - idealPercentage))
                .Max();

            Log($"┌──────────────────────────────────────────────────────────────┐");
            Log($"│ ЭФФЕКТИВНОСТЬ БАЛАНСИРОВКИ (Round Robin)                     │");
            Log($"├──────────────────────────────────────────────────────────────┤");
            Log($"│ Идеальное распределение:  {idealPercentage,6:F1}% на каждый Slave           │");
            Log($"│ Максимальное отклонение:  {maxDeviation,6:F1}%                             │");
            Log($"│                                                              │");

            if (maxDeviation < 5)
            {
                Log($"│ Оценка балансировки:      ⭐⭐⭐ ОТЛИЧНО!                  │");
            }
            else if (maxDeviation < 10)
            {
                Log($"│ Оценка балансировки:      ⭐⭐ ХОРОШО                      │");
            }
            else
            {
                Log($"│ Оценка балансировки:      ⭐ УДОВЛЕТВОРИТЕЛЬНО            │");
            }

            Log($"└──────────────────────────────────────────────────────────────┘");
            Log($"");

            // Временная шкала
            Log($"┌──────────────────────────────────────────────────────────────┐");
            Log($"│ ВРЕМЕННАЯ ШКАЛА                                              │");
            Log($"├──────────────────────────────────────────────────────────────┤");
            Log($"│ Начало обработки:         {_firstTaskTime:HH:mm:ss.fff}                 │");
            Log($"│ Окончание обработки:      {_lastTaskTime:HH:mm:ss.fff}                 │");
            Log($"│ Продолжительность:        {totalTime.Hours:D2}:{totalTime.Minutes:D2}:{totalTime.Seconds:D2}.{totalTime.Milliseconds:D3}          │");
            Log($"└──────────────────────────────────────────────────────────────┘");
            Log($"");
            Log($"═══════════════════════════════════════════════════════════════");
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