using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LogicReinc.BlendFarm.Converters
{
    public class DictionaryItemConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values != null && values.Length >= 2)
            {
                var myDict = values[0] as IDictionary;
                var myKey = values[1] as string;
                if (myDict != null && myKey != null)
                    return myDict[myKey].ToString();
            }
            return null;
        }

        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert(values.ToArray(), targetType, parameter, culture);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
