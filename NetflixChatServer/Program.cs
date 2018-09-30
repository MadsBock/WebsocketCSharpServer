using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace NetflixChatServer
{
    class Program
    {
        static object _lock = new object();
        static void Main(string[] args)
        {
            for (int i = 0; i < 10; i++)
            {
                new Task(Test).Start();
            }

            Console.ReadKey();
        }

        static void Test()
        {
            lock (_lock)
            {
                Console.WriteLine("Beginning Task");
                Thread.Sleep(500);
                Console.WriteLine("Ending Task");
                Thread.Sleep(250);
            }
        }
    }
}
