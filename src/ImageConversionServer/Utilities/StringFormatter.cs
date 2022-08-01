using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageConversionServer.Utilities
{
    public class StringFormatter
    {
        public string Text { get; set; }

        public Dictionary<string, object> Parameters { get; set; }

        public StringFormatter(string text)
        {
            Text = text;
            Parameters = new Dictionary<string, object>();
        }

        public void Add(string key, object val)
        {
            Parameters.Add(key, val);
        }

        public override string ToString()
        {
            return Parameters.Aggregate(Text, (current, parameter) => current.Replace(parameter.Key, parameter.Value.ToString()));
        }
    }
}
