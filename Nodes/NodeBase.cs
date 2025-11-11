using GaussianImageProcessingSystem.Services;

namespace GaussianImageProcessingSystem.Nodes
{
    /// <summary>
    /// Базовый класс для всех узлов системы
    /// </summary>
    public abstract class NodeBase : INode, IDisposable
    {
        protected TcpService _tcpService;
        protected bool _isRunning;

        public bool IsRunning => _isRunning;

        public event EventHandler<LogEventArgs> LogMessage;

        protected NodeBase(int port)
        {
            _tcpService = new TcpService(port);
            _tcpService.MessageReceived += OnMessageReceived;
            _tcpService.ErrorOccurred += OnTcpError;
        }

        public virtual void Start()
        {
            if (_isRunning)
            {
                Log("Узел уже запущен", LogLevel.Warning);
                return;
            }

            try
            {
                _tcpService.StartListening();
                _isRunning = true;
                Log($"Узел запущен на порту {_tcpService.Port}", LogLevel.Success);
            }
            catch (Exception ex)
            {
                Log($"Ошибка запуска узла: {ex.Message}", LogLevel.Error);
            }
        }

        public virtual void Stop()
        {
            if (!_isRunning)
            {
                Log("Узел уже остановлен", LogLevel.Warning);
                return;
            }

            try
            {
                _tcpService.StopListening();
                _isRunning = false;
                Log("Узел остановлен", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"Ошибка остановки узла: {ex.Message}", LogLevel.Error);
            }
        }

        protected abstract void OnMessageReceived(object sender, MessageReceivedEventArgs e);

        protected virtual void OnTcpError(object sender, Services.ErrorEventArgs e)
        {
            Log($"TCP ошибка: {e.ErrorMessage}", LogLevel.Error);
        }

        protected void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(this, new LogEventArgs(message, level));
        }

        public virtual void Dispose()
        {
            Stop();
            _tcpService?.Dispose();
        }
    }
}
