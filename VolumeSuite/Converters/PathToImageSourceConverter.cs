using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace VolumeSuite.Converters;

public class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                // ★ 关键：FileShare.ReadWrite | FileShare.Delete 允许其他程序删除或修改
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                bmp.StreamSource = stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad; // 完全加载到内存
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}