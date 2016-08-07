using System;
using System.Net;
using System.Threading;
using System.Text;

namespace Simulationserver
{
    class Program
    {
        // Init the random number.
        private static Random Rand = new Random();

        public static void Main(string[] args)
        {

            //Windows har en urlacl (netsh http show urlacl) med addresser som vanliga användare får använda. Nedan är en sådan som är default så att man slipper hacka windows.
            WebServer ws = new WebServer(SendResponse, "http://+:80/Temporary_Listen_Addresses/");
            ws.Run();
            Console.WriteLine("A simple webserver. Press a key to quit.");
            Console.ReadKey();
            ws.Stop();
        }

        public static string SendResponse(HttpListenerRequest request)
        {
            // Måste använda en StreamReader för att kunna läsa requestBodyn.
            string requestBody;
            using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
            {
                requestBody = reader.ReadToEnd();
                Console.WriteLine("{0} {1} {2}", DateTime.Now, requestBody, request.LocalEndPoint.Address.MapToIPv4());
            }

            var regmatch = System.Text.RegularExpressions.Regex.Match(requestBody, "[A-Za-z]*$");
            //
            // Returnera xml-texten formaterad så som CUCM skickar den. Slumpad samtalstrafik och requestBodyn som text för räknaren.
            // 
            return string.Format(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
    <soapenv:Body>
      <ns1:perfmonCollectCounterDataResponse xmlns:ns1=""http://schemas.cisco.com/ast/soap/"" soapenv:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
      <ArrayOfCounterInfo xmlns:soapenc=""http://schemas.xmlsoap.org/soap/encoding/"" soapenc:arrayType=""ns1:CounterInfoType[40]"" xsi:type=""soapenc:Array"">
        <item xsi:type=""ns1:CounterInfoType"">
          <Name xsi:type=""ns1:CounterNameType"">\\{0}\{1}({4}Trunk_1)\CallsInProgress</Name>
          <Value xsi:type=""xsd:long"">{2}</Value>
          <CStatus xsi:type=""xsd:unsignedInt"">1</CStatus>
        </item>
        <item xsi:type=""ns1:CounterInfoType"">
          <Name xsi:type=""ns1:CounterNameType"">\\{0}\{1}({4}Trunk_2)\CallsInProgress</Name>
          <Value xsi:type=""xsd:long"">{3}</Value>
          <CStatus xsi:type=""xsd:unsignedInt"">1</CStatus>
        </item>
        <item xsi:type=""ns1:CounterInfoType"">
          <Name xsi:type=""ns1:CounterNameType"">\\{0}\{1}({4}Trunk_1)\PRIChannelsActive</Name>
          <Value xsi:type=""xsd:long"">{2}</Value>
          <CStatus xsi:type=""xsd:unsignedInt"">1</CStatus>
        </item>
        <item xsi:type=""ns1:CounterInfoType"">
          <Name xsi:type=""ns1:CounterNameType"">\\{0}\{1}({4}Trunk_2)\PRIChannelsActive</Name>
          <Value xsi:type=""xsd:long"">{3}</Value>
          <CStatus xsi:type=""xsd:unsignedInt"">1</CStatus>
        </item>
        <item xsi:type=""ns1:CounterInfoType"">
          <Name xsi:type=""ns1:CounterNameType"">\\{0}\{1}({4}Trunk_1)\CallsActive</Name>
          <Value xsi:type=""xsd:long"">{2}</Value>
          <CStatus xsi:type=""xsd:unsignedInt"">1</CStatus>
        </item>
        <item xsi:type=""ns1:CounterInfoType"">
          <Name xsi:type=""ns1:CounterNameType"">\\{0}\{1}({4}Trunk_2)\CallsActive</Name>
          <Value xsi:type=""xsd:long"">{3}</Value>
          <CStatus xsi:type=""xsd:unsignedInt"">1</CStatus>
        </item>
      </ArrayOfCounterInfo>
      </ns1:perfmonCollectCounterDataResponse>
    </soapenv:Body>
    </soapenv:Envelope>", request.LocalEndPoint.Address.MapToIPv4(), requestBody, Rand.Next(100), Rand.Next(100), regmatch + "_");
            // LocalEndPoint för att få olika IP i svaret. requestBody innehåller räknaren. Rand för att slumpa samtalstrafik
        }
    }
    // class WebServer är från Internet.
    public class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<HttpListenerRequest, string> _responderMethod;

        public WebServer(string[] prefixes, Func<HttpListenerRequest, string> method)
        {
            if (!HttpListener.IsSupported)
                throw new NotSupportedException(
                    "Needs Windows XP SP2, Server 2003 or later.");

            // URI prefixes are required, for example 
            // "http://localhost:8080/index/".
            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("prefixes");

            // A responder method is required
            if (method == null)
                throw new ArgumentException("method");

            foreach (string s in prefixes)
                _listener.Prefixes.Add(s);

            _responderMethod = method;
            _listener.Start();
        }

        public WebServer(Func<HttpListenerRequest, string> method, params string[] prefixes)
            : this(prefixes, method) { }

        public void Run()
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                Console.WriteLine("Webserver running...");
                try
                {
                    while (_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                string rstr = _responderMethod(ctx.Request);
                                byte[] buf = Encoding.UTF8.GetBytes(rstr);
                                ctx.Response.ContentLength64 = buf.Length;
                                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                            }
                            catch { } // suppress any exceptions
                            finally
                            {
                                // always close the stream
                                ctx.Response.OutputStream.Close();
                            }
                        }, _listener.GetContext());
                    }
                }
                catch { } // suppress any exceptions
            });
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}