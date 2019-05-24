using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Net;

namespace BeeServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Server server = new Server();
            server.Start(9080);
            Console.Read();
        }
    }
}
