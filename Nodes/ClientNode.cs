using System.IO;
using System.Net.Sockets;
using GaussianImageProcessingSystem.Models;
using GaussianImageProcessingSystem.Services;
using Newtonsoft.Json;
using SysDrawing = System.Drawing;

namespace GaussianImageProcessingSystem.Nodes
{
    /// <summary>
    /// Клиентский узел для отправки изображений
    /// </summary>
    public class ClientNode : NodeBase
    {
        private string _masterIp;
        private int _masterPort;
        private Dictionary<string, ImageInfo> _pendingImages;
        private TcpClient _masterConnection;

        public List<ImageInfo> ProcessedImages { get; private set; }
        public event EventHandler<ImageProcessedEventArgs> ImageProcessed;

        public ClientNode(int port, string masterIp, int masterPort) : base(port)
        {
            _masterIp = masterIp;
            _masterPort = masterPort;
            _pendingImages = new Dictionary<string, ImageInfo>();
            ProcessedImages = new List<ImageInfo>();
        }

        public override async void Start()
        {
            base.Start();

            // Подключаемся к Master узлу
            await ConnectToMasterAsync();
        }

        /// <summary>
        /// Подключение к Master узлу
        /// </summary>
        private async Task ConnectToMasterAsync()
        {
            try
            {
                Log($"Подключение к Master узлу {_masterIp}:{_masterPort}...");
                _masterConnection = await _tcpService.ConnectAsync(_masterIp, _masterPort);

                if (_masterConnection != null && _masterConnection.Connected)
                {
                    Log($"Успешно подключен к Master узлу!", LogLevel.Success);
                }
                else
                {
                    Log($"Не удалось подключиться к Master узлу", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка подключения к Master: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Загрузка изображений из файлов
        /// </summary>
        public List<ImageInfo> LoadImages(string[] filePaths)
        {
            List<ImageInfo> images = new List<ImageInfo>();

            foreach (string filePath in filePaths)
            {
                try
                {
                    byte[] imageBytes = File.ReadAllBytes(filePath);

                    using (MemoryStream ms = new MemoryStream(imageBytes))
                    using (SysDrawing.Bitmap bitmap = new SysDrawing.Bitmap(ms))
                    {
                        ImageInfo info = new ImageInfo
                        {
                            FileName = Path.GetFileName(filePath),
                            OriginalData = imageBytes,
                            Width = bitmap.Width,
                            Height = bitmap.Height,
                            Format = bitmap.RawFormat.ToString()
                        };

                        images.Add(info);
                        Log($"Загружено изображение: {info.FileName} ({info.Width}x{info.Height})");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Ошибка загрузки файла {filePath}: {ex.Message}", LogLevel.Error);
                }
            }

            return images;
        }

        /// <summary>
        /// Отправка изображения на Master узел
        /// </summary>
        public async Task<bool> SendImageAsync(ImageInfo imageInfo)
        {
            try
            {
                if (_masterConnection == null || !_masterConnection.Connected)
                {
                    Log("Нет подключения к Master узлу", LogLevel.Error);
                    return false;
                }

                ImagePacket packet = new ImagePacket
                {
                    ImageData = imageInfo.OriginalData,
                    FileName = imageInfo.FileName,
                    Width = imageInfo.Width,
                    Height = imageInfo.Height,
                    Format = imageInfo.Format
                };

                string packetJson = JsonConvert.SerializeObject(packet);
                byte[] packetData = System.Text.Encoding.UTF8.GetBytes(packetJson);

                NetworkMessage message = new NetworkMessage
                {
                    Type = MessageType.ImageRequest,
                    Data = packetData,
                    SenderIp = "127.0.0.1",
                    SenderPort = _tcpService.Port
                };

                _pendingImages[packet.PacketId] = imageInfo;

                bool sent = await _tcpService.SendMessageAsync(message, _masterConnection);

                if (sent)
                {
                    Log($"Отправлено изображение {imageInfo.FileName} на Master");
                    return true;
                }
                else
                {
                    Log($"Не удалось отправить изображение {imageInfo.FileName}", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка отправки изображения: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Отправка всех изображений
        /// </summary>
        public async Task SendImagesAsync(List<ImageInfo> images)
        {
            foreach (var image in images)
            {
                await SendImageAsync(image);
                await Task.Delay(100); // Небольшая задержка между отправками
            }
        }

        protected override void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                if (e.Message.Type == MessageType.ImageResponse)
                {
                    string packetJson = System.Text.Encoding.UTF8.GetString(e.Message.Data);
                    ImagePacket packet = JsonConvert.DeserializeObject<ImagePacket>(packetJson);

                    Log($"Получен ответ: {packet.FileName}");

                    if (_pendingImages.TryGetValue(packet.PacketId, out ImageInfo originalInfo))
                    {
                        originalInfo.ProcessedData = packet.ImageData;
                        ProcessedImages.Add(originalInfo);
                        _pendingImages.Remove(packet.PacketId);

                        Log($"Получено обработанное изображение: {packet.FileName}", LogLevel.Success);

                        ImageProcessed?.Invoke(this, new ImageProcessedEventArgs { ImageInfo = originalInfo });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки полученного сообщения: {ex.Message}", LogLevel.Error);
            }
        }

        public override void Dispose()
        {
            _masterConnection?.Close();
            base.Dispose();
        }
    }

    /// <summary>
    /// Информация об изображении
    /// </summary>
    public class ImageInfo
    {
        public string FileName { get; set; }
        public byte[] OriginalData { get; set; }
        public byte[] ProcessedData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }
    }

    /// <summary>
    /// Аргументы события обработки изображения
    /// </summary>
    public class ImageProcessedEventArgs : EventArgs
    {
        public ImageInfo ImageInfo { get; set; }
    }
}