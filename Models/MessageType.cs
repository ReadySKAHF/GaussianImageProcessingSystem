namespace GaussianImageProcessingSystem.Models
{
    /// <summary>
    /// Типы сообщений в TCP системе
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// Запрос на обработку изображения
        /// </summary>
        ImageRequest,

        /// <summary>
        /// Ответ с обработанным изображением
        /// </summary>
        ImageResponse,

        /// <summary>
        /// Регистрация Slave узла
        /// </summary>
        SlaveRegister,

        /// <summary>
        /// Подтверждение получения
        /// </summary>
        Acknowledgment,

        /// <summary>
        /// Информация о статистике Slave
        /// </summary>
        SlaveStatistics
    }
}