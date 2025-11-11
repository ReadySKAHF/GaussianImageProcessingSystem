using System.Net.Sockets;
using GaussianImageProcessingSystem.Models;
using GaussianImageProcessingSystem.Services;
using Newtonsoft.Json;

namespace GaussianImageProcessingSystem.Nodes
{
    /// <summary>
    /// Slave узел для обработки изображений
    /// </summary>
    public class SlaveNode : NodeBase
    {
        private string _masterIp;
        private int _masterPort;
        private GaussianFilterService _filterService;
        private TcpClient _masterConnection;
        private int _tasksCompleted = 0;
        private double _totalProcessingTime = 0;

        public SlaveNode(int port, string masterIp, int masterPort) : base(port)
        {
            _masterIp = masterIp;
            _masterPort = masterPort;
            _filterService = new GaussianFilterService();
        }

        public override async void Start()
        {
            base.Start();
            await RegisterWithMasterAsync();
        }

        /// <summary>
        /// Регистрация на Master узле
        /// </summary>
        private async Task RegisterWithMasterAsync()
        {
            try
            {
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"🔗 РЕГИСТРАЦИЯ НА MASTER УЗЛЕ");
                Log($"   Master адрес: {_masterIp}:{_masterPort}");
                Log($"   Локальный порт: {_tcpService.Port}");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                _masterConnection = await _tcpService.ConnectAsync(_masterIp, _masterPort);

                if (_masterConnection != null && _masterConnection.Connected)
                {
                    SlaveRegistrationData regData = new SlaveRegistrationData
                    {
                        IpAddress = "127.0.0.1",
                        Port = _tcpService.Port
                    };

                    string dataJson = JsonConvert.SerializeObject(regData);
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(dataJson);

                    NetworkMessage message = new NetworkMessage
                    {
                        Type = MessageType.SlaveRegister,
                        Data = data
                    };

                    Log($"📤 Отправка запроса на регистрацию...");
                    bool sent = await _tcpService.SendMessageAsync(message, _masterConnection);

                    if (sent)
                    {
                        Log($"✅ Запрос отправлен, ожидание подтверждения...");
                    }
                    else
                    {
                        Log($"❌ Не удалось отправить запрос на регистрацию", LogLevel.Error);
                    }
                }
                else
                {
                    Log($"❌ Не удалось подключиться к Master узлу", LogLevel.Error);
                    Log($"   Проверьте что Master узел запущен на {_masterIp}:{_masterPort}", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка регистрации: {ex.Message}", LogLevel.Error);
            }
        }

        protected override void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                switch (e.Message.Type)
                {
                    case MessageType.Acknowledgment:
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        Log($"✅ РЕГИСТРАЦИЯ ПОДТВЕРЖДЕНА!", LogLevel.Success);
                        Log($"   Slave узел успешно зарегистрирован на Master");
                        Log($"   Готов к приёму задач на обработку");
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        break;

                    case MessageType.ImageRequest:
                        ProcessImageRequestAsync(e.Message);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Ошибка обработки сообщения: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Обработка запроса на обработку изображения
        /// </summary>
        private async void ProcessImageRequestAsync(NetworkMessage message)
        {
            try
            {
                string packetJson = System.Text.Encoding.UTF8.GetString(message.Data);
                ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log($"   НОВАЯ ЗАДАЧА: {packet.FileName}");
                Log($"   PacketId: {packet.PacketId}");
                Log($"   Размер: {packet.ImageData.Length / 1024}KB");
                Log($"   Разрешение: {packet.Width}x{packet.Height}");
                Log($"   Фильтр: Гаусса (sigma=2.0, kernel=5x5)");
                Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                await Task.Run(() =>
                {
                    try
                    {
                        DateTime startTime = DateTime.Now;
                        Log($"   Начало обработки изображения: {packet.FileName}");

                        // Применяем фильтр Гаусса
                        byte[] processedData = _filterService.ApplyGaussianFilter(
                            packet.ImageData,
                            sigma: 2.0,
                            kernelSize: 5);

                        TimeSpan processingTime = DateTime.Now - startTime;

                        // Обновляем статистику
                        _tasksCompleted++;
                        _totalProcessingTime += processingTime.TotalSeconds;
                        double avgTime = _totalProcessingTime / _tasksCompleted;

                        Log($"   Фильтр применён за {processingTime.TotalSeconds:F2} сек");
                        Log($"   Статистика: задач выполнено: {_tasksCompleted}, среднее время: {avgTime:F2} сек");

                        // Сжатие если нужно
                        int originalSize = processedData.Length;
                        if (originalSize > 500000)
                        {
                            Log($"   Сжатие изображения (было {originalSize / 1024}KB)...");
                            processedData = _filterService.CompressImage(processedData, 75L);
                            Log($"   После сжатия: {processedData.Length / 1024}KB");
                        }

                        // Создаем пакет с обработанным изображением
                        ImagePacket responsePacket = new ImagePacket
                        {
                            ImageData = processedData,
                            FileName = packet.FileName,
                            Width = packet.Width,
                            Height = packet.Height,
                            Format = packet.Format,
                            PacketId = packet.PacketId,
                            SlavePort = _tcpService.Port
                        };

                        // Отправляем результат обратно Master узлу
                        SendProcessedImageAsync(responsePacket);

                        Log($"   Обработка завершена: {packet.FileName}", LogLevel.Success);
                        Log($"   Общее время: {processingTime.TotalSeconds:F2} сек");
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    }
                    catch (Exception ex)
                    {
                        Log($"   Ошибка обработки изображения {packet.FileName}: {ex.Message}", LogLevel.Error);
                        Log($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"   Ошибка обработки запроса: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Отправка обработанного изображения Master узлу
        /// </summary>
        private async void SendProcessedImageAsync(ImagePacket packet)
        {
            try
            {
                string packetJson = JsonConvert.SerializeObject(packet);
                byte[] packetData = System.Text.Encoding.UTF8.GetBytes(packetJson);

                Log($"   Отправка результата Master узлу...");

                // Отправляем статистику перед результатом
                await SendStatisticsToMasterAsync();

                NetworkMessage message = new NetworkMessage
                {
                    Type = MessageType.ImageResponse,
                    Data = packetData
                };

                bool sent = await _tcpService.SendMessageAsync(message, _masterConnection);

                if (sent)
                {
                    Log($"   Результат успешно отправлен Master узлу", LogLevel.Success);
                }
                else
                {
                    Log($"   Не удалось отправить результат {packet.FileName}", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Log($"   Ошибка отправки результата: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Отправка статистики Master узлу
        /// </summary>
        private async Task SendStatisticsToMasterAsync()
        {
            try
            {
                var stats = new
                {
                    Port = _tcpService.Port,
                    TasksCompleted = _tasksCompleted,
                    AverageProcessingTime = _tasksCompleted > 0 ? _totalProcessingTime / _tasksCompleted : 0
                };

                string statsJson = JsonConvert.SerializeObject(stats);
                byte[] statsData = System.Text.Encoding.UTF8.GetBytes(statsJson);

                NetworkMessage message = new NetworkMessage
                {
                    Type = MessageType.SlaveStatistics,
                    Data = statsData
                };

                await _tcpService.SendMessageAsync(message, _masterConnection);
            }
            catch (Exception ex)
            {
                Log($"Ошибка отправки статистики: {ex.Message}", LogLevel.Error);
            }
        }

        public override void Dispose()
        {
            _masterConnection?.Close();
            base.Dispose();
        }
    }

    /// <summary>
    /// Данные регистрации Slave
    /// </summary>
    public class SlaveRegistrationData
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
    }
}