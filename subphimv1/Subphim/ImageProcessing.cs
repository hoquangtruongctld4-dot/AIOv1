using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Diagnostics;

namespace subphimv1.Subphim
{
    public static class ImageProcessingUtils
    {
        public static bool CropHorizontalTextRegion(string imagePath, string savePath, byte blackThreshold = 128, bool overwrite = true)
        {
            Bitmap sourceBitmap = null;
            Bitmap workingBitmap = null;
            BitmapData bmpData = null;
            int foundLeft = -1;
            int foundRight = -1;

            try
            {
                if (!File.Exists(imagePath))
                {
                    return false;
                }
                sourceBitmap = (Bitmap)Image.FromFile(imagePath);
                if (sourceBitmap.PixelFormat != PixelFormat.Format24bppRgb &&
                    sourceBitmap.PixelFormat != PixelFormat.Format32bppArgb &&
                    sourceBitmap.PixelFormat != PixelFormat.Format32bppRgb)
                {
                    workingBitmap = new Bitmap(sourceBitmap.Width, sourceBitmap.Height, PixelFormat.Format24bppRgb);
                    using (Graphics gr = Graphics.FromImage(workingBitmap))
                    {
                        gr.Clear(Color.White);
                        gr.DrawImage(sourceBitmap, 0, 0, sourceBitmap.Width, sourceBitmap.Height);
                    }
                }
                else
                {
                    workingBitmap = (Bitmap)sourceBitmap.Clone();
                }
                if (!string.Equals(Path.GetFullPath(imagePath), Path.GetFullPath(savePath), StringComparison.OrdinalIgnoreCase))
                {
                    sourceBitmap?.Dispose();
                    sourceBitmap = null;
                }

                int width = workingBitmap.Width;
                int height = workingBitmap.Height;
                Rectangle lockRect = new Rectangle(0, 0, width, height);
                bmpData = workingBitmap.LockBits(lockRect, ImageLockMode.ReadOnly, workingBitmap.PixelFormat);

                nint ptr = bmpData.Scan0;
                int stride = bmpData.Stride;
                int bytesPerPixel = Image.GetPixelFormatSize(workingBitmap.PixelFormat) / 8;
                int totalImageBytes = Math.Abs(stride) * height;
                byte[] pixelData = new byte[totalImageBytes];
                System.Runtime.InteropServices.Marshal.Copy(ptr, pixelData, 0, totalImageBytes);

                for (int x = 0; x < width; x++)
                {
                    bool columnContainsBlack = false;
                    for (int y = 0; y < height; y++)
                    {
                        int offset = y * stride + x * bytesPerPixel;
                        byte b = pixelData[offset];
                        byte g = pixelData[offset + 1];
                        byte r = pixelData[offset + 2];
                        byte gray = (byte)(r * 0.299 + g * 0.587 + b * 0.114);

                        if (gray < blackThreshold)
                        {
                            columnContainsBlack = true;
                            break;
                        }
                    }
                    if (columnContainsBlack)
                    {
                        foundLeft = x;
                        break;
                    }
                }
                if (foundLeft != -1)
                {
                    for (int x = width - 1; x >= foundLeft; x--)
                    {
                        bool columnContainsBlack = false;
                        for (int y = 0; y < height; y++)
                        {
                            int offset = y * stride + x * bytesPerPixel;
                            byte b = pixelData[offset];
                            byte g = pixelData[offset + 1];
                            byte r = pixelData[offset + 2];
                            byte gray = (byte)(r * 0.299 + g * 0.587 + b * 0.114);

                            if (gray < blackThreshold)
                            {
                                columnContainsBlack = true;
                                break;
                            }
                        }
                        if (columnContainsBlack)
                        {
                            foundRight = x;
                            break;
                        }
                    }
                }

                workingBitmap.UnlockBits(bmpData);
                bmpData = null;

                if (foundLeft != -1 && foundRight != -1 && foundRight >= foundLeft)
                {
                    int cropX = foundLeft;
                    int cropWidth = foundRight - foundLeft + 1;

                    if (cropWidth <= 0) return false;

                    Rectangle cropRectangle = new Rectangle(cropX, 0, cropWidth, height);

                    string tempSavePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.GetExtension(savePath));

                    try
                    {
                        using (Bitmap croppedBmp = workingBitmap.Clone(cropRectangle, workingBitmap.PixelFormat))
                        {
                            ImageFormat format = GetImageFormat(savePath);
                            croppedBmp.Save(tempSavePath, format);
                        }

                        // Safely dispose bitmaps before file operations
                        workingBitmap?.Dispose(); workingBitmap = null;
                        sourceBitmap?.Dispose(); sourceBitmap = null;
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        if (File.Exists(savePath))
                        {
                            if (overwrite) File.Delete(savePath);
                            else { File.Delete(tempSavePath); return false; }
                        }
                        File.Move(tempSavePath, savePath);
                        return true;
                    }
                    catch
                    {
                        if (File.Exists(tempSavePath)) { try { File.Delete(tempSavePath); } catch { } }
                        return false;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (bmpData != null && workingBitmap != null)
                {
                    try { workingBitmap.UnlockBits(bmpData); } catch { }
                }
                workingBitmap?.Dispose();
                sourceBitmap?.Dispose();
            }
        }

        private static ImageFormat GetImageFormat(string filePath)
        {
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            return ext switch
            {
                ".png" => ImageFormat.Png,
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                ".gif" => ImageFormat.Gif,
                ".tiff" => ImageFormat.Tiff,
                _ => ImageFormat.Png
            };
        }
    }
}