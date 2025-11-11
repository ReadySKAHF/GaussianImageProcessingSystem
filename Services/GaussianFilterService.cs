using System.IO;
using SysDrawing = System.Drawing;
using SysDrawingImaging = System.Drawing.Imaging;

namespace GaussianImageProcessingSystem.Services
{
    /// <summary>
    /// Сервис применения фильтра Гаусса к изображениям
    /// </summary>
    public class GaussianFilterService
    {
        /// <summary>
        /// Применение фильтра Гаусса к изображению
        /// </summary>
        /// <param name="imageData">Данные изображения</param>
        /// <param name="sigma">Сигма для фильтра Гаусса (по умолчанию 2.0)</param>
        /// <param name="kernelSize">Размер ядра фильтра (по умолчанию 5x5)</param>
        /// <returns>Обработанное изображение</returns>
        public byte[] ApplyGaussianFilter(byte[] imageData, double sigma = 2.0, int kernelSize = 5)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(imageData))
                using (SysDrawing.Bitmap originalImage = new SysDrawing.Bitmap(ms))
                {
                    SysDrawing.Bitmap filteredImage = ApplyGaussianFilterToBitmap(originalImage, sigma, kernelSize);

                    using (MemoryStream outputMs = new MemoryStream())
                    {
                        filteredImage.Save(outputMs, SysDrawingImaging.ImageFormat.Png);
                        filteredImage.Dispose();
                        return outputMs.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка применения фильтра Гаусса: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Применение фильтра Гаусса к Bitmap
        /// </summary>
        private SysDrawing.Bitmap ApplyGaussianFilterToBitmap(SysDrawing.Bitmap original, double sigma, int kernelSize)
        {
            // Генерация ядра Гаусса
            double[,] kernel = GenerateGaussianKernel(kernelSize, sigma);

            int width = original.Width;
            int height = original.Height;
            SysDrawing.Bitmap result = new SysDrawing.Bitmap(width, height);

            // Блокируем биты для быстрого доступа
            SysDrawingImaging.BitmapData originalData = original.LockBits(
                new SysDrawing.Rectangle(0, 0, width, height),
                SysDrawingImaging.ImageLockMode.ReadOnly,
                SysDrawingImaging.PixelFormat.Format24bppRgb);

            SysDrawingImaging.BitmapData resultData = result.LockBits(
                new SysDrawing.Rectangle(0, 0, width, height),
                SysDrawingImaging.ImageLockMode.WriteOnly,
                SysDrawingImaging.PixelFormat.Format24bppRgb);

            int bytesPerPixel = 3;
            int stride = originalData.Stride;
            int offset = kernelSize / 2;

            unsafe
            {
                byte* originalPtr = (byte*)originalData.Scan0.ToPointer();
                byte* resultPtr = (byte*)resultData.Scan0.ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        double blueSum = 0, greenSum = 0, redSum = 0;

                        // Применяем ядро Гаусса
                        for (int ky = -offset; ky <= offset; ky++)
                        {
                            for (int kx = -offset; kx <= offset; kx++)
                            {
                                int newX = x + kx;
                                int newY = y + ky;

                                // Проверка границ с зеркальным отражением
                                if (newX < 0) newX = -newX;
                                if (newX >= width) newX = 2 * width - newX - 1;
                                if (newY < 0) newY = -newY;
                                if (newY >= height) newY = 2 * height - newY - 1;

                                int pixelOffset = newY * stride + newX * bytesPerPixel;
                                double kernelValue = kernel[ky + offset, kx + offset];

                                blueSum += originalPtr[pixelOffset] * kernelValue;
                                greenSum += originalPtr[pixelOffset + 1] * kernelValue;
                                redSum += originalPtr[pixelOffset + 2] * kernelValue;
                            }
                        }

                        // Ограничиваем значения диапазоном [0, 255]
                        int resultPixelOffset = y * stride + x * bytesPerPixel;
                        resultPtr[resultPixelOffset] = (byte)Math.Max(0, Math.Min(255, blueSum));
                        resultPtr[resultPixelOffset + 1] = (byte)Math.Max(0, Math.Min(255, greenSum));
                        resultPtr[resultPixelOffset + 2] = (byte)Math.Max(0, Math.Min(255, redSum));
                    }
                }
            }

            original.UnlockBits(originalData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Генерация ядра фильтра Гаусса
        /// </summary>
        private double[,] GenerateGaussianKernel(int size, double sigma)
        {
            double[,] kernel = new double[size, size];
            int center = size / 2;
            double sum = 0;

            // Вычисление значений ядра
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = x - center;
                    int dy = y - center;
                    
                    double value = Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma));
                    kernel[y, x] = value;
                    sum += value;
                }
            }

            // Нормализация ядра
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    kernel[y, x] /= sum;
                }
            }

            return kernel;
        }

        /// <summary>
        /// Получение размеров изображения из байтового массива
        /// </summary>
        public (int width, int height) GetImageDimensions(byte[] imageData)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(imageData))
                using (SysDrawing.Bitmap image = new SysDrawing.Bitmap(ms))
                {
                    return (image.Width, image.Height);
                }
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>
        /// Сжатие изображения с уменьшением качества для передачи по TCP
        /// </summary>
        public byte[] CompressImage(byte[] imageData, long quality = 85L)
        {
            try
            {
                using (MemoryStream inputMs = new MemoryStream(imageData))
                using (SysDrawing.Bitmap original = new SysDrawing.Bitmap(inputMs))
                using (MemoryStream outputMs = new MemoryStream())
                {
                    // Получаем encoder для JPEG
                    SysDrawingImaging.ImageCodecInfo jpegEncoder = GetEncoder(SysDrawingImaging.ImageFormat.Jpeg);

                    // Настройки качества
                    SysDrawingImaging.Encoder encoder = SysDrawingImaging.Encoder.Quality;
                    SysDrawingImaging.EncoderParameters encoderParams = new SysDrawingImaging.EncoderParameters(1);
                    encoderParams.Param[0] = new SysDrawingImaging.EncoderParameter(encoder, quality);

                    // Сохраняем с compression
                    original.Save(outputMs, jpegEncoder, encoderParams);

                    return outputMs.ToArray();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка сжатия изображения: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Получение encoder для формата изображения
        /// </summary>
        private SysDrawingImaging.ImageCodecInfo GetEncoder(SysDrawingImaging.ImageFormat format)
        {
            SysDrawingImaging.ImageCodecInfo[] codecs = SysDrawingImaging.ImageCodecInfo.GetImageEncoders();
            foreach (SysDrawingImaging.ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
