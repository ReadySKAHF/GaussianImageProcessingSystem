using System.IO;
using SysDrawing = System.Drawing;
using SysDrawingImaging = System.Drawing.Imaging;

namespace GaussianImageProcessingSystem.Services
{
    /// <summary>
    /// 🔥 УСЛОЖНЕННАЯ ВЕРСИЯ - обработка в 10-20 раз медленнее!
    /// Для демонстрации эффекта параллелизма при работе с несколькими Slave узлами
    /// </summary>
    public class GaussianFilterService
    {
        /// <summary>
        /// Применение УСИЛЕННОГО фильтра Гаусса с множественными проходами
        /// </summary>
        public byte[] ApplyGaussianFilter(byte[] imageData, double sigma = 2.0, int kernelSize = 5)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(imageData))
                using (SysDrawing.Bitmap originalImage = new SysDrawing.Bitmap(ms))
                {
                    SysDrawing.Bitmap processedImage = originalImage;

                    // ════════════════════════════════════════════════════
                    // УСЛОЖНЕНИЕ #1: Увеличенное ядро 15x15
                    // ════════════════════════════════════════════════════
                    // Было: 5x5 = 25 операций на пиксель
                    // Стало: 15x15 = 225 операций на пиксель (в 9 раз больше!)

                    int heavyKernelSize = 15;
                    double heavySigma = 3.5;

                    // ════════════════════════════════════════════════════
                    // УСЛОЖНЕНИЕ #2: Многопроходная обработка (5 проходов)
                    // ════════════════════════════════════════════════════
                    // Применяем фильтр Гаусса 5 РАЗ ПОДРЯД!

                    for (int pass = 1; pass <= 5; pass++)
                    {
                        SysDrawing.Bitmap tempResult = ApplyGaussianFilterToBitmap(
                            processedImage,
                            heavySigma,
                            heavyKernelSize);

                        if (pass > 1)
                            processedImage.Dispose();

                        processedImage = tempResult;
                    }

                    // ════════════════════════════════════════════════════
                    // УСЛОЖНЕНИЕ #3: Фильтр резкости
                    // ════════════════════════════════════════════════════
                    // Дополнительная свёрточная операция 3x3

                    SysDrawing.Bitmap sharpenedImage = ApplySharpenFilter(processedImage);
                    processedImage.Dispose();
                    processedImage = sharpenedImage;

                    // ════════════════════════════════════════════════════
                    // УСЛОЖНЕНИЕ #4: Фильтр контраста
                    // ════════════════════════════════════════════════════
                    // Попиксельная обработка всего изображения

                    SysDrawing.Bitmap contrastedImage = ApplyContrastFilter(processedImage, 1.2);
                    processedImage.Dispose();
                    processedImage = contrastedImage;

                    // ════════════════════════════════════════════════════
                    // УСЛОЖНЕНИЕ #5: Финальное размытие (большое ядро 11x11)
                    // ════════════════════════════════════════════════════
                    // Еще один проход с ядром 11x11 = 121 операция на пиксель

                    SysDrawing.Bitmap finalImage = ApplyGaussianFilterToBitmap(
                        processedImage,
                        2.0,
                        11);
                    processedImage.Dispose();

                    // ════════════════════════════════════════════════════
                    // УСЛОЖНЕНИЕ #6: Дополнительный проход для яркости
                    // ════════════════════════════════════════════════════

                    SysDrawing.Bitmap brightenedImage = ApplyBrightnessFilter(finalImage, 1.05);
                    finalImage.Dispose();

                    // Сохранение результата
                    using (MemoryStream outputMs = new MemoryStream())
                    {
                        brightenedImage.Save(outputMs, SysDrawingImaging.ImageFormat.Png);
                        brightenedImage.Dispose();
                        return outputMs.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка применения усиленного фильтра: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Применение фильтра Гаусса к Bitmap
        /// </summary>
        private SysDrawing.Bitmap ApplyGaussianFilterToBitmap(SysDrawing.Bitmap original, double sigma, int kernelSize)
        {
            double[,] kernel = GenerateGaussianKernel(kernelSize, sigma);

            int width = original.Width;
            int height = original.Height;
            SysDrawing.Bitmap result = new SysDrawing.Bitmap(width, height);

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

                        for (int ky = -offset; ky <= offset; ky++)
                        {
                            for (int kx = -offset; kx <= offset; kx++)
                            {
                                int newX = x + kx;
                                int newY = y + ky;

                                // Зеркальное отражение на границах
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
        /// Фильтр резкости (Sharpen) - свёртка 3x3
        /// </summary>
        private SysDrawing.Bitmap ApplySharpenFilter(SysDrawing.Bitmap original)
        {
            double[,] sharpenKernel = new double[3, 3]
            {
                { -1, -1, -1 },
                { -1,  9, -1 },
                { -1, -1, -1 }
            };

            return ApplyConvolutionFilter(original, sharpenKernel);
        }

        /// <summary>
        /// Фильтр контраста - попиксельная обработка
        /// </summary>
        private SysDrawing.Bitmap ApplyContrastFilter(SysDrawing.Bitmap original, double contrast)
        {
            int width = original.Width;
            int height = original.Height;
            SysDrawing.Bitmap result = new SysDrawing.Bitmap(width, height);

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

            unsafe
            {
                byte* originalPtr = (byte*)originalData.Scan0.ToPointer();
                byte* resultPtr = (byte*)resultData.Scan0.ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int pixelOffset = y * stride + x * bytesPerPixel;

                        for (int c = 0; c < 3; c++)
                        {
                            double pixel = originalPtr[pixelOffset + c];
                            pixel = ((pixel / 255.0 - 0.5) * contrast + 0.5) * 255.0;
                            resultPtr[pixelOffset + c] = (byte)Math.Max(0, Math.Min(255, pixel));
                        }
                    }
                }
            }

            original.UnlockBits(originalData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Фильтр яркости - попиксельная обработка
        /// </summary>
        private SysDrawing.Bitmap ApplyBrightnessFilter(SysDrawing.Bitmap original, double brightnessFactor)
        {
            int width = original.Width;
            int height = original.Height;
            SysDrawing.Bitmap result = new SysDrawing.Bitmap(width, height);

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

            unsafe
            {
                byte* originalPtr = (byte*)originalData.Scan0.ToPointer();
                byte* resultPtr = (byte*)resultData.Scan0.ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int pixelOffset = y * stride + x * bytesPerPixel;

                        for (int c = 0; c < 3; c++)
                        {
                            double pixel = originalPtr[pixelOffset + c] * brightnessFactor;
                            resultPtr[pixelOffset + c] = (byte)Math.Max(0, Math.Min(255, pixel));
                        }
                    }
                }
            }

            original.UnlockBits(originalData);
            result.UnlockBits(resultData);

            return result;
        }

        /// <summary>
        /// Применение произвольного свёрточного фильтра
        /// </summary>
        private SysDrawing.Bitmap ApplyConvolutionFilter(SysDrawing.Bitmap original, double[,] kernel)
        {
            int width = original.Width;
            int height = original.Height;
            int kernelSize = kernel.GetLength(0);
            int offset = kernelSize / 2;

            SysDrawing.Bitmap result = new SysDrawing.Bitmap(width, height);

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

            unsafe
            {
                byte* originalPtr = (byte*)originalData.Scan0.ToPointer();
                byte* resultPtr = (byte*)resultData.Scan0.ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        double blueSum = 0, greenSum = 0, redSum = 0;

                        for (int ky = 0; ky < kernelSize; ky++)
                        {
                            for (int kx = 0; kx < kernelSize; kx++)
                            {
                                int newX = x + kx - offset;
                                int newY = y + ky - offset;

                                if (newX < 0) newX = 0;
                                if (newX >= width) newX = width - 1;
                                if (newY < 0) newY = 0;
                                if (newY >= height) newY = height - 1;

                                int pixelOffset = newY * stride + newX * bytesPerPixel;
                                double kernelValue = kernel[ky, kx];

                                blueSum += originalPtr[pixelOffset] * kernelValue;
                                greenSum += originalPtr[pixelOffset + 1] * kernelValue;
                                redSum += originalPtr[pixelOffset + 2] * kernelValue;
                            }
                        }

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

            // Нормализация
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
        /// Получение размеров изображения
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
        /// Сжатие изображения для передачи по TCP
        /// </summary>
        public byte[] CompressImage(byte[] imageData, long quality = 85L)
        {
            try
            {
                using (MemoryStream inputMs = new MemoryStream(imageData))
                using (SysDrawing.Bitmap original = new SysDrawing.Bitmap(inputMs))
                using (MemoryStream outputMs = new MemoryStream())
                {
                    SysDrawingImaging.ImageCodecInfo jpegEncoder = GetEncoder(SysDrawingImaging.ImageFormat.Jpeg);
                    SysDrawingImaging.Encoder encoder = SysDrawingImaging.Encoder.Quality;
                    SysDrawingImaging.EncoderParameters encoderParams = new SysDrawingImaging.EncoderParameters(1);
                    encoderParams.Param[0] = new SysDrawingImaging.EncoderParameter(encoder, quality);

                    original.Save(outputMs, jpegEncoder, encoderParams);
                    return outputMs.ToArray();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка сжатия изображения: {ex.Message}", ex);
            }
        }

        private SysDrawingImaging.ImageCodecInfo GetEncoder(SysDrawingImaging.ImageFormat format)
        {
            SysDrawingImaging.ImageCodecInfo[] codecs = SysDrawingImaging.ImageCodecInfo.GetImageEncoders();
            foreach (SysDrawingImaging.ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }
    }
}