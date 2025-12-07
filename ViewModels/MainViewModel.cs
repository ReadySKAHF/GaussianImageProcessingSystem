using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GaussianImageProcessingSystem.Nodes;
using Microsoft.Win32;
using SysDrawing = System.Drawing;

namespace GaussianImageProcessingSystem.ViewModels
{
    /// <summary>
    /// ViewModel для главного окна
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private INode _currentNode;
        private string _selectedNodeType;
        private bool _isRunning;
        private string _localPort;
        private string _masterIp;
        private string _masterPort;
        private string _selectedFilterSize;
        private ObservableCollection<string> _logMessages;
        private ObservableCollection<ImageItemViewModel> _images;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<string> NodeTypes { get; set; }
        public ObservableCollection<string> FilterSizes { get; set; }

        public ObservableCollection<string> LogMessages
        {
            get => _logMessages;
            set
            {
                _logMessages = value;
                OnPropertyChanged(nameof(LogMessages));
            }
        }

        public ObservableCollection<ImageItemViewModel> Images
        {
            get => _images;
            set
            {
                _images = value;
                OnPropertyChanged(nameof(Images));
            }
        }

        public string SelectedNodeType
        {
            get => _selectedNodeType;
            set
            {
                _selectedNodeType = value;
                OnPropertyChanged(nameof(SelectedNodeType));
                OnPropertyChanged(nameof(IsClientNode));
                OnPropertyChanged(nameof(IsMasterNode));
                OnPropertyChanged(nameof(IsSlaveNode));
            }
        }

        public string SelectedFilterSize
        {
            get => _selectedFilterSize;
            set
            {
                _selectedFilterSize = value;
                OnPropertyChanged(nameof(SelectedFilterSize));
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
            }
        }

        public string LocalPort
        {
            get => _localPort;
            set
            {
                _localPort = value;
                OnPropertyChanged(nameof(LocalPort));
            }
        }

        public string MasterIp
        {
            get => _masterIp;
            set
            {
                _masterIp = value;
                OnPropertyChanged(nameof(MasterIp));
            }
        }

        public string MasterPort
        {
            get => _masterPort;
            set
            {
                _masterPort = value;
                OnPropertyChanged(nameof(MasterPort));
            }
        }

        public bool IsClientNode => SelectedNodeType == "Client";
        public bool IsMasterNode => SelectedNodeType == "Master";
        public bool IsSlaveNode => SelectedNodeType == "Slave";
        public bool CanStart => !IsRunning;
        public bool CanStop => IsRunning;

        public ICommand StartNodeCommand { get; }
        public ICommand StopNodeCommand { get; }
        public ICommand LoadImagesCommand { get; }
        public ICommand SendImagesCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand SaveProcessedImagesCommand { get; }

        public MainViewModel()
        {
            NodeTypes = new ObservableCollection<string> { "Client", "Master", "Slave" };

            FilterSizes = new ObservableCollection<string> { "15x15", "25x25", "35x35" };
            LogMessages = new ObservableCollection<string>();
            Images = new ObservableCollection<ImageItemViewModel>();

            SelectedNodeType = "Client";
            SelectedFilterSize = "15x15";
            LocalPort = "5000";
            MasterIp = "127.0.0.1";
            MasterPort = "5001";

            StartNodeCommand = new RelayCommand(StartNode, () => CanStart);
            StopNodeCommand = new RelayCommand(StopNode, () => CanStop);
            LoadImagesCommand = new RelayCommand(LoadImages, () => IsClientNode);
            SendImagesCommand = new RelayCommand(SendImages, () => IsClientNode && IsRunning && Images.Any(i => !i.IsProcessed));
            ClearLogsCommand = new RelayCommand(ClearLogs);
            SaveProcessedImagesCommand = new RelayCommand(SaveProcessedImages, () => Images.Any(i => i.IsProcessed));
        }

        private int GetFilterSizeValue()
        {
            if (string.IsNullOrEmpty(SelectedFilterSize))
                return 15;

            string sizeStr = SelectedFilterSize.Split('x')[0];
            if (int.TryParse(sizeStr, out int size))
                return size;

            return 15;
        }

        private void StartNode()
        {
            try
            {
                if (!int.TryParse(LocalPort, out int port))
                {
                    AddLog("Неверный формат порта", true);
                    return;
                }

                switch (SelectedNodeType)
                {
                    case "Client":
                        if (!int.TryParse(MasterPort, out int masterPort))
                        {
                            AddLog("Неверный формат порта Master", true);
                            return;
                        }
                        var clientNode = new ClientNode(port, MasterIp, masterPort);
                        clientNode.ImageProcessed += OnImageProcessed;
                        _currentNode = clientNode;
                        break;

                    case "Master":
                        _currentNode = new MasterNode(port);
                        break;

                    case "Slave":
                        if (!int.TryParse(MasterPort, out masterPort))
                        {
                            AddLog("Неверный формат порта Master", true);
                            return;
                        }
                        _currentNode = new SlaveNode(port, MasterIp, masterPort);
                        break;
                }

                _currentNode.LogMessage += OnNodeLogMessage;
                _currentNode.Start();
                IsRunning = true;
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка запуска узла: {ex.Message}", true);
            }
        }

        private void StopNode()
        {
            try
            {
                _currentNode?.Stop();
                _currentNode = null;
                IsRunning = false;
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка остановки узла: {ex.Message}", true);
            }
        }

        private void LoadImages()
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog
                {
                    Multiselect = true,
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif"
                };

                if (dialog.ShowDialog() == true)
                {
                    Images.Clear();

                    foreach (string filePath in dialog.FileNames)
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
                                    Format = bitmap.RawFormat.ToString(),
                                    FilterSize = GetFilterSizeValue()
                                };

                                Images.Add(new ImageItemViewModel
                                {
                                    FileName = info.FileName,
                                    ImageInfo = info,
                                    OriginalImage = ByteArrayToBitmapImage(info.OriginalData)
                                });

                                AddLog($"Загружено изображение: {info.FileName} ({info.Width}x{info.Height})");
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"Ошибка загрузки файла {Path.GetFileName(filePath)}: {ex.Message}", true);
                        }
                    }

                    AddLog($"Всего загружено изображений: {Images.Count}");
                    AddLog($"Размер фильтра: {SelectedFilterSize}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка загрузки изображений: {ex.Message}", true);
            }
        }

        private async void SendImages()
        {
            try
            {
                var clientNode = _currentNode as ClientNode;
                if (clientNode == null)
                    return;

                var imagesToSend = Images.Where(i => !i.IsProcessed).Select(i => i.ImageInfo).ToList();
                await clientNode.SendImagesAsync(imagesToSend);
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка отправки изображений: {ex.Message}", true);
            }
        }

        private void SaveProcessedImages()
        {
            try
            {
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Выберите папку для сохранения обработанных изображений"
                };

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    int savedCount = 0;
                    foreach (var image in Images.Where(i => i.IsProcessed))
                    {
                        string savePath = Path.Combine(folderDialog.SelectedPath,
                            $"gaussian_{image.FileName}");
                        File.WriteAllBytes(savePath, image.ImageInfo.ProcessedData);
                        savedCount++;
                    }

                    AddLog($"Сохранено изображений: {savedCount}");
                    MessageBox.Show($"Сохранено {savedCount} изображений", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка сохранения изображений: {ex.Message}", true);
            }
        }

        private void ClearLogs()
        {
            LogMessages.Clear();
        }

        private void OnNodeLogMessage(object sender, LogEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"[{e.Timestamp:HH:mm:ss}] {e.Message}", e.Level == LogLevel.Error);
            });
        }

        private void OnImageProcessed(object sender, ImageProcessedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var imageVm = Images.FirstOrDefault(i => i.FileName == e.ImageInfo.FileName);
                if (imageVm != null && e.ImageInfo.ProcessedData != null)
                {
                    imageVm.ProcessedImage = ByteArrayToBitmapImage(e.ImageInfo.ProcessedData);
                    imageVm.IsProcessed = true;
                    AddLog($"Изображение обработано: {e.ImageInfo.FileName}");
                }
            });
        }

        private void AddLog(string message, bool isError = false)
        {
            string prefix = isError ? "[ОШИБКА] " : "";
            LogMessages.Insert(0, $"{prefix}{message}");

            if (LogMessages.Count > 100)
            {
                LogMessages.RemoveAt(LogMessages.Count - 1);
            }
        }

        private BitmapImage ByteArrayToBitmapImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            try
            {
                BitmapImage image = new BitmapImage();
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    stream.Position = 0;
                    image.BeginInit();
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                AddLog($"Ошибка конвертации изображения: {ex.Message}", true);
                return null;
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ViewModel для элемента изображения
    /// </summary>
    public class ImageItemViewModel : INotifyPropertyChanged
    {
        private BitmapImage _originalImage;
        private BitmapImage _processedImage;
        private bool _isProcessed;

        public string FileName { get; set; }
        public ImageInfo ImageInfo { get; set; }

        public BitmapImage OriginalImage
        {
            get => _originalImage;
            set
            {
                _originalImage = value;
                OnPropertyChanged(nameof(OriginalImage));
            }
        }

        public BitmapImage ProcessedImage
        {
            get => _processedImage;
            set
            {
                _processedImage = value;
                OnPropertyChanged(nameof(ProcessedImage));
                OnPropertyChanged(nameof(HasProcessedImage));
            }
        }

        public bool IsProcessed
        {
            get => _isProcessed;
            set
            {
                _isProcessed = value;
                OnPropertyChanged(nameof(IsProcessed));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(HasProcessedImage));
            }
        }

        public bool HasProcessedImage => ProcessedImage != null;
        public string StatusText => IsProcessed ? "Обработано" : "Ожидание";

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}