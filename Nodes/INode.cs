namespace GaussianImageProcessingSystem.Nodes
{
    /// <summary>
    /// Интерфейс узла системы
    /// </summary>
    public interface INode
    {
        /// <summary>
        /// Запуск узла
        /// </summary>
        void Start();

        /// <summary>
        /// Остановка узла
        /// </summary>
        void Stop();

        /// <summary>
        /// Проверка, запущен ли узел
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Событие логирования
        /// </summary>
        event EventHandler<LogEventArgs> LogMessage;
    }

    /// <summary>
    /// Аргументы события логирования
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }

        public LogEventArgs(string message, LogLevel level = LogLevel.Info)
        {
            Message = message;
            Timestamp = DateTime.Now;
            Level = level;
        }
    }

    /// <summary>
    /// Уровень логирования
    /// </summary>
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }
}
