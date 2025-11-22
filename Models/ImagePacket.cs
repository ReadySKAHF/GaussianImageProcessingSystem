namespace GaussianImageProcessingSystem.Models
{
    /// <summary>
    /// Пакет с данными изображения
    /// </summary>
    [Serializable]
    public class ImagePacket
    {
        /// <summary>
        /// Данные изображения
        /// </summary>
        public byte[] ImageData { get; set; }

        /// <summary>
        /// Имя файла
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Ширина изображения
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Высота изображения
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Формат изображения (PNG, JPEG и т.д.)
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Идентификатор пакета для отслеживания
        /// </summary>
        public string PacketId { get; set; }

        /// <summary>
        /// Порт Slave узла (для идентификации)
        /// </summary>
        public int SlavePort { get; set; }

        /// <summary>
        /// Размер ядра фильтра Гаусса (например, 10, 15, 20)
        /// </summary>
        public int FilterSize { get; set; }

        public ImagePacket()
        {
            PacketId = Guid.NewGuid().ToString();
            FilterSize = 15; // Значение по умолчанию
        }

        public ImagePacket(byte[] imageData, string fileName, int width, int height, string format, int filterSize = 15)
        {
            ImageData = imageData;
            FileName = fileName;
            Width = width;
            Height = height;
            Format = format;
            PacketId = Guid.NewGuid().ToString();
            FilterSize = filterSize;
        }
    }
}