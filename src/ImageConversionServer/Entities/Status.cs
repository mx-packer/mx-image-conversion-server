using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageConversionServer.Entities
{
    public class Status
    {
        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public Status(string code, string message)
        {
            Code = code;
            Message = message;
        }
    }
}
