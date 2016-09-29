using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Hucksters.Gripari.Input;
using Hucksters.Gripari.Output;

namespace Hucksters.Gripari
{
    class Program
    {
        static void Main(string[] args)
        {
            int number = 1;
            Console.WriteLine(number);

            var input1c = new OneC();
            var outputWeb = new WebOut();

            input1c.GotEventLog += outputWeb.OnEventLog;

            input1c.run();
        }
    }
    
}
